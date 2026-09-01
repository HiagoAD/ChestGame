# The minigame framework

`MinigameBaseSO`, `MinigameContainer` and `MinigameManager` register and instantiate minigames
through ScriptableObject definitions, with VContainer resolving dependencies. Adding a minigame needs
no change to the framework, which is the property worth having. It is not, however, a small job: see
[what adding one actually takes](#what-adding-a-minigame-actually-takes) below.

Two types share the `MinigameBase.cs` file and the docs name both. `MinigameBaseSO` is the abstract
non-generic base that carries the authored fields and the content hook, and it is what
`IMinigameCatalog` and `MinigameContentPreloader` hold. `MinigameBase<TController, TView, TMinigame>`
is the generic subclass a concrete definition derives from, and it adds the view reference and the
typed hook.

## A definition names its content, it does not hold it

The view is an `AssetReferenceGameObject` and the chests config document is an
`AssetReferenceT<TextAsset>`. Both serialize as a GUID string rather than as an object reference,
which is the whole point: a direct field would make loading the descriptor drag the minigame's entire
bundle in behind it. A direct field would also undo the mechanism silently, so treat those fields as
load-bearing.

`MinigameBaseSO` carries four authored things:

- `Id`, what the shell asks for. Authored on the asset rather than derived from the container type,
  which is what lets the shell start a minigame without referencing the assembly that defines it.
  Serialized fields on an abstract ScriptableObject base do serialize, so every concrete definition
  carries the slot.
- `ContentLabel`, the label every asset this minigame owns carries.
- `LoadPolicy`, how that content is meant to arrive. See
  [content-delivery.md](content-delivery.md).
- `ViewRef`, on the generic `MinigameBase<TController, TView, TMinigame>`.

The descriptor is the only thing that both names a minigame and is cheap to hold, which is what makes
"which content belongs to this minigame" answerable without loading any of it.

## Nothing loads while the container is built

`MinigameManager.Get` constructs the container and injects it, and stops there. Injecting the
controller at that point would land before its own content did, so that ordering is the container's
to keep.

`MinigameContainer.BeginAsync` does the rest, in one order that is a framework promise rather than an
accident of layout:

1. Fetch the minigame's content if its policy says on demand.
2. Load the view.
3. Run the definition's `ConfigureControllerAsync` hook.
4. Inject the controller.
5. Instantiate the view.

`ConfigureControllerAsync` is the one extension point a concrete minigame gets. It is how a controller
builds state from its own config document and is still injected on top of it. A minigame needing no
content of its own overrides nothing. It is asynchronous because that content is behind an
`AssetReference` and is not there until something asks. `ReleaseContent` is its other half, so
whatever the hook loaded is dropped without `End` having to become asynchronous.

The view is instantiated through the resolver rather than through `Addressables.InstantiateAsync`, so
it and everything under it are injected the way every other object in the game is.

The configure-before-inject ordering is pinned by
`MinigameContainerContentTests`. `ChestsMinigameSO` depends on it directly, because the chest list is
sized from `ChestCount`.

## Starting twice is loud

`BeginAsync` on a running container throws `MinigameAlreadyRunningException`. Deliberately not
symmetrical with `End`, which has to be safe to call unconditionally because teardown runs from paths
that cannot know what happened. Starting twice is nobody's teardown; it is a caller that lost track of
its own container.

Returning quietly would also leak. Each start takes a ref-count on the view, and the single `End` that
follows can only give one back.

## Failure during a start

If anything in `BeginAsync` throws, the catch releases what was already taken and destroys the view
instance if one exists, then rethrows.

Nothing else could ever let those go. `End` is a no-op until `_running` is true, which is the last
line of the try block, so a load that threw or a start that was cancelled halfway would otherwise
leave whatever already arrived resident for the rest of the session with no handle on it anywhere.

Releasing there rather than making `End` unconditional is deliberate: `End` has to stay safe on a
container that never began, and "release what I took" is the only statement that is true in both
cases. The view instance is destroyed in the same place for the same reason, one object further on. A
view created just before `SetController` threw would otherwise sit in the scene, unreferenced and
undestroyable, for the rest of the session.

## Teardown

`End` disposes the controller, destroys the view, and releases the handles `BeginAsync` took. It is
idempotent and safe on a minigame that was never begun, so callers can tear down unconditionally.

It stays synchronous because it releases the assets rather than the instance, and releasing a handle
needs no waiting.

## What adding a minigame actually takes

The framework does not change, and no existing file is edited except the authoring list. That is the
claim worth making. The count of new things is still around a dozen, and most of it is authoring
rather than code:

Code, in a new assembly:

1. An `.asmdef`. `Company.ChestGame.Minigame.Chests` carries ten references, and a new one needs the
   same set minus whatever it genuinely does not use.
2. A container type: `public class YourMinigame : MinigameContainer { }`. Needed because `TMinigame`
   is constrained `new()` and the catalog keys on the container type, so two minigames cannot share
   one.
3. A controller deriving from `MinigameControllerBase`, with a public parameterless constructor,
   because `TController` is also constrained `new()`.
4. A view deriving from `MinigameViewBase`.
5. A definition deriving from `MinigameBase<TController, TView, TMinigame>`, with `[CreateAssetMenu]`.
   To load its own content it overrides the **protected** typed `ConfigureControllerAsync` and
   `ReleaseContent`. The public untyped `ConfigureControllerAsync` is `sealed` on the generic base, so
   overriding that one will not compile.

Authoring:

6. The definition asset itself, with `_id`, `_contentLabel`, `_loadPolicy` and `_viewRef` filled in.
7. The view prefab, plus whatever else the minigame owns.
8. An addressable group, with both a `BundledAssetGroupSchema` and a `ContentUpdateGroupSchema`. The
   existing groups keep theirs in `AddressableAssetsData/AssetGroups/Schemas/`.
9. Build and load paths on that schema, local or remote.
10. The label on every entry in the group, matching `_contentLabel` exactly. Nothing checks the two
    against each other except `GameBootstrapperTests`.
11. An entry in `MinigameList.asset`, which is the one existing file that changes.
12. A content build, if the group is remote.

The shell needs no change: `GameManager` starts whatever id it is given.

## The chests minigame

The only minigame in the project. Its code is `Company.ChestGame.Minigame.Chests` under
`_Project/Scripts/Minigames/Implementation/Minigames/`; its assets are under
`_Project/Minigames/Chests/`.

### Its own config document

`ChestsMinigameConfig` holds chest count, attempts and open time. These three values mean nothing to
the rest of the game, so nothing else gets to name them, and the chests code needs no reference to the
config assembly at all. `ChestsMinigameSO` names the document with an `AssetReferenceT<TextAsset>`,
fetches it when the minigame begins, and `ChestsMinigameConfig.Parse` validates it.

An empty inspector slot is checked before the load rather than after. The provider would otherwise
report a `MissingAssetException` naming an empty GUID, which is neither traceable back to the asset
nor the failure a reader is looking for.

The config class is immutable once built, like `LocalJsonGameConfig`, because "validated" has to be a
durable guarantee rather than something a later assignment can undo. `Create` and `Parse` are the only
two ways in and both validate.

Its constructor is private and parameterless on purpose. Json.NET picks a public parameterized
constructor when it finds one, so validation living in a constructor would run during deserialization
and surface wrapped in `JsonSerializationException`, which `Parse` would then report as "not valid
JSON". `Validate()` runs after deserialization instead.

The rules: `ChestCount <= 0`, `AttempsCount <= 0` and a negative open time are rejected. A document
can parse cleanly and still describe a round that can never be played or never end.

### The controller

`ChestsMinigameController` holds the chest states, the remaining attempts, the async opening and the
reward distribution. It touches neither `UnityEngine.Random` nor `Time` directly. Both arrive through
the seams described in [architecture.md](architecture.md).

`Configure` lands before injection and sizes the chest list from the config.

A click spawns two concurrent UniTasks under one cancellation token: one updates the chest's progress
every frame through `IGameClock.NextFrame`, which lasts exactly one update loop the way
`yield return null` does on a coroutine, and the other awaits the delay and then opens the chest. The
delay is read per click rather than cached at start, which supports the time varying between pulls.

Cancellation is registered to close the chest again, and a new game or a second click cancels whatever
was opening. No locks are needed: Unity handles two simultaneous touches in series, one after the
other, so there is no true multithreading here.

`NewGame` cancels any opening chest and closes all of them, which supports restarts. It does not
support the number of chests changing between games.

### Prize odds

The prize location is calculated on every attempt rather than stored, to avoid being discoverable
through memory inspection. That is unrealistic for a production game and any real anti-cheat effort
would need far more, but it works as a demonstration. The simpler approach would be to save the
winning chest index at new game.

The odds model exactly one prize among the chests. With N chests and k already opened empty, the one
being opened now holds it with probability 1/(N - k). `Attempts` has already been incremented by that
point, so k is `Attempts - 1` and the divisor is `Chests.Count - Attempts + 1`. Dropping the `+ 1`
makes the odds reach certainty one chest early, so the last chest could never hold the prize.

### The views

`ChestsMinigameView` owns the board and reacts to controller events.
`ChestsMinigameChestElementView` drives one chest from its model state and offers a slider during the
opening state.

The models belong to the controller and outlive the views showing them, so a view that stops showing
a model has to let go of it, or the next state change drives a MonoBehaviour that is destroyed or
parked. `ChestsMinigameView` unsubscribes in `OnDestroy`; the controller normally clears its own
events in `Dispose` first, but a view torn down on its own must not leave handlers behind either.

#### A chest has two lifetimes now

Pooling splits the element view in half, because `Awake` runs once per instance while an acquire runs
on every reuse:

- The click listener is per instance. `Awake` adds it, `OnDestroy` drops it.
- The model subscription is per acquire. `Init` subscribes, `Release` unsubscribes and drops both the
  model and the click callback, and `OnDestroy` routes through `Release` so a destroyed view lets go
  of its model too.

`Init` does not release first. A caller owes a `Release` between two `Init`s, and guarding it there
would hide a caller that forgot and would blunt the tests that prove the release path is the one
doing the work. `Release` resets nothing visual either: `Init` drives the whole of it from the model
it is handed, so clearing it twice would let a broken `Init` still look right.

A released chest that kept its subscription is the pooling bug that looks like nothing: the instance
is still alive, throws nothing, and simply follows a chest it is no longer showing. Under
`ParkedPool` it is not even deactivated, so a button that kept its callback would still reach the
controller.

#### The board is rebuilt every game

`NewGame` releases the whole board back to the pool and takes it again. The rebuild used to be skipped
because it was expensive, and making it cheap is what the pool is for - a rebuild that never happens
is a saving nobody can measure.

The pool comes from `Company.ChestGame.Pooling` behind a `[SerializeField] PoolStrategy`, defaulting
to `ParkedPool` because it measured fastest on exactly this rebuild - the numbers, and why `SetActive`
is what costs the difference under uGUI, are in
[design-decisions.md](design-decisions.md#why-parkedpool-is-the-default), and the seam those four
strategies implement is in
[design-decisions.md](design-decisions.md#the-seam-itself). The value is authored on the prefab, so
changing it means editing the asset rather than the field initializer. It
is owned by the view and disposed in `OnDestroy`: the chest prefab lives in the chests bundle, which
`MinigameContainer.End` releases, so a pooled instance outliving the view would be holding assets that
can be unloaded. Its bound is the board size, because the board is handed back whole and taken again
whole.

The holder the pool parks under is built at runtime as a child of the view, with a `Canvas` component
switched off. It cannot go under `_chestsParent`, which carries the `GridLayoutGroup` - parking under
a layout group is most of the cost pooling was meant to remove - and it cannot be deactivated, because
`ParkedPool` refuses an inactive holder for the same reason it exists. A disabled `Canvas` draws
nothing, keeps every GameObject under it active, cuts the subtree out of the canvas above, and carries
no `GraphicRaycaster`, so nothing parked can be clicked.

The view uses the pool and knows nothing about the demonstration of it. The four-way race lives in
`Company.ChestGame.Pooling.Demo` as its own prefab in `Game.unity`, and this assembly does not
reference it - see [architecture.md](architecture.md#assembly-layout). It used to be an overlay this
view built, which meant the minigame carried a reference to a demo it did not need and the demo could
only be seen by entering the minigame.

The fill runs through `FrameBudgetedLoop` (see [architecture.md](architecture.md)), so a large board
costs a few cheap frames rather than one long one. Each fill gets a `CancellationTokenSource` linked
to the view's destroy token, cancelled by the next fill and by teardown. The cancellation path
deliberately hands nothing back: the only two things that cancel a fill are the next fill, which
releases the board before it starts anyway, and teardown, where the continuation resumes after
`OnDestroy` has already disposed the pool.

`ChestsMinigameChestModel.SetOpening` and `SetOpen` are both guarded so an opened chest never walks
back to `Opening`. The two tasks driving a chest resume in the same frame, so a progress tick arriving
just after the chest opened would otherwise reopen it visually.
