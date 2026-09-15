# Architecture

The map of the codebase: what the assemblies are, how the game boots, and where each piece of the
shared machinery lives. Read this first. The deeper notes on asset loading, delivery, the minigame
framework and the test suite are in the sibling files listed in [README.md](README.md).

## Assembly layout

Every script sits inside an assembly definition, so nothing compiles into `Assembly-CSharp`. The
split is what keeps the dependency directions honest: a reference that would create a cycle fails to
compile instead of quietly working.

```
Company.ChestGame.Common      _Project/Scripts/Common/     leaf: engine seams, FrameBudgetedLoop, exceptions, catalog policy
Company.ChestGame.Pooling     _Project/Scripts/Pooling/    leaf: the prefab pool seam and its four strategies
Company.ChestGame.Pooling.Demo _Project/Scripts/PoolingDemo/ the standalone race panel; nothing in the game references it
Company.ChestGame.Saving      _Project/Scripts/Saving/     the ISaveService seam, its stores, codecs, protectors and scheduler
Company.ChestGame.Saving.Demo _Project/Scripts/SavingDemo/ the save inspector; nothing in the game references it
Company.ChestGame.Assets      _Project/Scripts/Assets/     the only assembly that calls Addressables
Company.ChestGame.Config      _Project/Scripts/Config/
Company.ChestGame.Currency    _Project/Scripts/Currency/
Company.ChestGame.Popups      _Project/Scripts/Popups/
Company.ChestGame.Minigame    _Project/Scripts/Minigames/  the framework, no minigame in it
Company.ChestGame.Rewards     _Project/Scripts/Rewards/
Company.ChestGame.Gameplay    _Project/Scripts/Gameplay/   the shell: GameManager and nothing else
Company.ChestGame.UI          _Project/Scripts/UI/
Company.ChestGame.Core        _Project/Scripts/Core/       composition root, both LifetimeScopes
Company.ChestGame.Editor      _Project/Scripts/Editor/     content build, save corpus and save inspector prefab generators

Company.ChestGame.Minigame.Chests  _Project/Scripts/Minigames/Implementation/Minigames/

TapNation.Modules             AssetLibrary/                vendored Resource Bank

Company.ChestGame.Tests.Common    Tests/Common/            fakes, shared by both suites
Company.ChestGame.Tests.EditMode  Tests/EditMode/
Company.ChestGame.Tests.PlayMode  Tests/PlayMode/
```

The chests minigame is an assembly of its own and the shell does not reference it. `GameManager`
asks for a minigame by authored id, which is the only reason it can start one without naming its
type. Its code lives under `Scripts/Minigames/Implementation/Minigames/`; its assets (definition
asset, prefabs, sprite, config document) live under `_Project/Minigames/Chests/`. The assembly
boundary is what makes "what belongs to this minigame" a question the compiler answers, and the
addressable group does the same job for the assets. See
[content-delivery.md](content-delivery.md).

`Company.ChestGame.Pooling` is the other leaf, and it references no other assembly at all. It knows
nothing about chests, minigames or UI: it is a seam over where an instance comes from - the
interface is `IPrefabPool<T>` - with a hand-rolled pool, a reparenting one, a wrapper over the
engine's `ObjectPool` and an `Instantiate`/`Destroy` baseline behind it. The contract rules that a
signature does not carry are in
[design-decisions.md](design-decisions.md#the-seam-itself). It is deliberately synchronous and frame-agnostic -
spreading a large fill over frames is the caller's job, through `FrameBudgetedLoop`. The two meet at
the call site rather than in each other, which is why `Common` has no pooling reference and `Pooling`
has no UniTask one.

`Company.ChestGame.Pooling.Demo` races all four strategies against each other at once so the
difference between them can be watched rather than asserted. It is a demonstration and nothing else:
it is not referenced by the game, by the chests minigame, or by anything except the test suite, and
deleting the folder and its prefab would leave the game working. It races whatever prefab its
serialized field points at - the chest element included - and ships a plain tile as the default,
because a square reads better at two thousand items than a detailed sprite does.

It ships in the player build, deliberately: `Game.unity` is an enabled build scene, the prefab is
an active root in it, and the assembly carries no define constraints. This project is a technical
demonstration, so a player being able to open the race panel is the point rather than a leak. If
that ever changes, gating the assembly alone is not enough - it would leave a missing-script
reference in the shipped scene, so the prefab has to come out of the scene in the same change.

It lives as a prefab at the root of `Game.unity`, carrying its own Canvas, its own `CanvasScaler` and
its own `PanelSettings`, so it owns its scaling and sorting instead of inheriting whatever it was
dropped under. Its chrome is authored - `PoolingDemo.uxml` for the tree, `PoolingDemo.uss` for the
styling, both under `_Project/UI/PoolingDemo/` - and the panel class only binds to it: query by name,
set text, toggle a class. The lanes stay uGUI and stay built at runtime, because they hold real pooled
Components and a `VisualElement` can host neither a Component nor a GameObject. The two systems meet
at one place: an empty `lanes-slot` element that the stylesheet sizes, which the panel measures and
moves the uGUI lanes onto. That is deliberate - the alternative is a height constant kept in step with
the stylesheet by hand.

`Common` is deliberately a leaf and references only UniTask. The engine seams below would otherwise
be a natural fit for `Core`, but `Core` already depends on `Rewards`, and `Rewards` needs the seams,
so putting them there would close a reference cycle.

## Engine seams: clock and random

`UnityEngine.Random` and the player loop (`Time.deltaTime`, `UniTask.Yield`, `UniTask.Delay`) are the
two pieces of the engine that gameplay logic would otherwise reach for directly. Both defeat a unit
test. One answers differently on every run; the other only advances while something is driving
frames.

`IRandomProvider` and `IGameClock` stand in front of them. `UnityRandomProvider` and `UnityGameClock`
are the production implementations, registered in the root scope alongside everything else that needs
no loaded asset to exist. Both clock waits respect `Time.timeScale`, so pausing the game pauses any
chest mid-open.

`IGameClock` answers three things, and the third is the odd one. `DeltaTime` and the two waits are
about frames; `ElapsedMilliseconds` is a monotonic reading that also moves *within* a frame, which
`Time.deltaTime` cannot do because it is fixed for the whole of one. `FrameBudgetedLoop` is what
needs it: it runs N units of work and yields a frame whenever the time spent since this frame started
passes a budget, so a screen spawning hundreds of objects costs several cheap frames instead of one
visible hitch. A budget in time rather than a count per frame is the whole point of it - a count
makes every caller finish in the same number of frames whatever a unit costs it, which erases exactly
the difference a comparison between two ways of doing the work is looking for.

This is the piece the rest of the testing story hangs on. Because the chest-opening flow draws time
and randomness through these, the whole thing, two parallel UniTasks and every cancellation path,
runs in edit mode with no player loop and no real waiting. `FakeGameClock` parks awaiters and
releases them from `AdvanceFrame()`, and the continuations resume synchronously inside that call, so
a test can assert the moment it returns.

### Why RunAsync is split, and the ordering inside the loop

`RunAsync` validates its arguments synchronously before handing off to a private `RunCoreAsync` for the
loop itself. The split matters because an async method captures what it throws into the task it returns
rather than throwing at the call site, and a fill started from a `MonoBehaviour` that never awaits the
returned task would lose that exception entirely; keeping the checks in the synchronous half means a bad
call throws where it was made.

Inside the loop, cancellation is checked before each unit runs, not after, so a token cancelled while the
previous unit was running gets no further work out of the loop. The last unit skips the yield afterward,
since there is nothing left to place and a yield there would only buy a frame to do nothing in. The
budget itself is checked in the opposite order: after a unit has run rather than before, which is what
guarantees every frame places at least one unit. Checking it first would let a single unit costing more
than the whole budget yield forever and place nothing - which is also why the constructor rejects a
budget of zero rather than treating it as "no budget": with the check running after each unit, zero would
mean exactly one unit per frame, the per-frame cost the class exists to avoid, while reading at the call
site like the budgeting had simply been switched off.

## Boot

The game starts in `Scenes/Boot.unity`, not in the game scene. Opening `Game.unity` directly will not
work: its scope expects a parent that only the boot scene builds.

`GameLifetimeScope` lives in the boot scene, registers everything that needs no asset, and survives
the scene load through `DontDestroyOnLoad`. `GameBootstrapper` then does five things, in the order
the whole design rests on:

1. `RecordLaunchAsync` loads the meta save, increments the launch count and stamps the play times,
   then hands it to the meta scheduler. First, so a content failure below still gets recorded as a
   launch.
2. `GameContentLoader` reads every source.
3. `RegisterLoadedServices` builds a child scope from what came back.
4. `MinigameContentPreloader` fetches whatever asked to arrive up front.
5. `Game.unity` opens, and its own `GameSceneLifetimeScope` is parented to that scope through
   `LifetimeScope.EnqueueParent`.

`GameBootstrapper` resolves `MinigameContentPreloader` from the child scope it just built
(`_gameScope`), because the preloader needs the catalog, which does not exist until the content it
was built from arrived. The scene load is wrapped in `using (LifetimeScope.EnqueueParent(_gameScope))`
so `Game.unity`'s own scope can be parented to the one built at boot without that scene holding a
reference to an object that did not exist when it was authored.

Step 1 is the only one whose failure is not a boot failure. A meta save this build cannot read is
logged and reset to a fresh document, because meta holds nothing a player earned; the identical
choice for currency would not be safe. Everything past the load is unguarded, so a failure there
fails boot like any other. See [saving.md](saving.md) for what meta holds.

`GameSceneLifetimeScope` is the game scene's scope and it registers nothing: its `Configure` is
empty. It exists to be the scene's injection root, so `GameManager` and `CurrencyWatcher` are
auto-injected from a scope that can see both halves of the registration split. Its auto-inject list
holds two scene objects, `Canvas` and `GameManager`, and injection reaches every child of a listed
object: the two `CurrencyWatcher`s are injected because they sit under
`Canvas/SafeArea/TopBar/ConsumablesArea`. A new object that needs injection either goes under one of
those two or is added to the list. The root scope cannot
resolve `IMinigameManager` at all, which is why the auto-inject list has to live here rather than in
the boot scene. `GameBootstrapperTests` pins that.

It lives in `Company.ChestGame.Core` alongside the root scope, which is what lets
`Company.ChestGame.Gameplay` stay `GameManager` and nothing else.

No service ever exists with its data not yet arrived, so nothing anywhere has to ask whether loading
has finished. `LoadedContent` is a carrier and nothing else, with no loading, parsing or validation
in it, which keeps that guarantee structural: a service holding one of those fields cannot be
constructed before the content arrived.

`GameContentLoader` is a plain class with no scene or scope in it, which is what keeps the untestable
part of booting down to the three lines in the bootstrapper. It reads the four sources sequentially
rather than in parallel. Nothing there is slow enough for the difference to matter, and one at a time
means a failure names the source that caused it instead of whichever of four raced to the exception
first. `LoadAsync_ReadsEverySourceExactlyOnce` asserts an exact count rather than at least once,
because a source read twice is a source downloaded twice.

### Registration, in two halves

`GameLifetimeScope` keeps its registration lists apart from `Configure` so tests can assert against
the real composition root instead of a hand-copied duplicate. `GameLifetimeScopeTests` runs against
`RegisterCoreServices` and `RegisterLoadedServices` themselves, so dropping a registration from the
composition root fails there.

`RegisterCoreServices` holds everything that can be built the moment the container is: the two engine
seams, `IAssetProvider`, the four content sources, the whole save composition, the save handler,
`CurrencyManager`, `GameContentLoader` and the bootstrapper. That is what lets the boot scene resolve
the loader and the bootstrapper before a single file has been read.

The save half of that list is five registrations rather than one. `ISaveFlushRegistry`; the single
`ISaveService` every key in this composition saves through, assembled by hand because it needs a
legacy import; and a `SaveScheduler<T>` each for currency and for meta, every one of them followed by
a `RegisterBuildCallback` that resolves it. The callbacks are load-bearing rather than tidiness:
registering a scheduler with `ISaveFlushRegistry` is a side effect of resolving it, so a scheduler
nothing resolves is a scheduler nothing registered, and an unregistered save is simply never flushed
at pause or quit while looking identical in every other respect.

`RegisterLoadedServices` holds the half that cannot exist until content has arrived. It registers
seven things, in two shapes.

Four are registered as already-built instances, because each is derived straight from a loaded asset:
`IGameConfig` (from the config document), `IMinigameCatalog` and `IPopupCatalog` (from the two
authored lists), and `IPopupParentProvider` (from the popup parent prefab). Registering the instance
rather than the type is what makes the ordering guarantee structural: there is no moment at which one
of these exists without its data.

The other three are ordinary type registrations, because they take their loaded dependencies through
their constructors rather than holding content themselves: `IPopupManager`, `IMinigameManager` and
`IRewardsManager`. `MinigameContentPreloader` is registered the same way, and belongs to this half
rather than to core because it needs the catalog.

The bootstrapper is registered as its interfaces rather than through `RegisterEntryPoint`. A
`LifetimeScope` installs the entry point dispatcher itself, so the real game still runs it, while a
container a test builds by hand stays inert and does not boot the game from a registration assertion.

### The pause/quit flush

`GameLifetimeScope` is also where saves are forced to disk. It resolves `ISaveFlushRegistry` once in
`Awake`, and both `OnApplicationPause(true)` and `OnApplicationQuit()` call `FlushAll()` on it,
catching and logging rather than letting a Unity callback throw. Without it, a scheduler's coalescing
window is a real data-loss window every time the OS suspends or kills a backgrounded app.

Schedulers register themselves rather than being named here, which is what lets the chests minigame -
in an assembly `Core` deliberately does not reference - have its own run save flushed from the same
place. See [saving.md](saving.md), "The pause/quit flush lives on GameLifetimeScope".

### Telling the player what boot is doing

Each step reports through `IBootStatus`. An interface rather than a label, because the bootstrapper is
a plain class and reaching a TextMeshPro component from it would put a scene object in the one part
of booting that has none. `BootStatusLabel` is the boot scene's implementation and holds the label
that scene already had. `SilentBootStatus` is what gets registered when there is no label: a container
built by a test, or a boot scene whose slot was never wired. Registering a silent one rather than
nothing keeps the bootstrapper free of a null check at every call site.

`Configure` converts `_bootStatus` through a Unity-overloaded comparison before handing it to
`RegisterCoreServices`: `_bootStatus != null ? _bootStatus : null`, never `is not null`. A missing or
destroyed `BootStatusLabel` is Unity-null - the C# reference itself is not null, and only the
overloaded `==`/`!=` operators on `UnityEngine.Object` know to treat it as gone. `RegisterCoreServices`
then decides whether to register a `SilentBootStatus` with `status ?? new SilentBootStatus()`, and the
plain `??` operator does not call that overloaded operator - it only tests the raw reference. Passing
`_bootStatus` straight through on `is not null` (or skipping the conversion) would leave a destroyed
component's dead reference sitting in the container instead of the `SilentBootStatus` fallback.

A failure during boot is reported to that label and then rethrown. Swallowing it would make
`StartAsync` return normally, which is a lie the rest of boot is built on: the game scene was never
loaded and no service downstream exists. Rethrowing also keeps the exception reaching a developer,
since VContainer hands an unhandled async startable to UniTask, which logs it. The player gets the
sentence and not the stack trace, because `Message` is a sentence while `ToString` adds a stack trace
that tells a player nothing and hides the one line that might.

Cancellation is filtered out of that catch. Boot being cancelled is the scope disposing as the
application quits, not boot failing, and there is nobody left to read a message by then.

Saying why at all is the reason the `Core` group ships local. The config, the popup and this label
are the three things that have to be present before the game can explain that nothing else is.

`StartAsync_WhenContentCannotBeLoaded_TellsThePlayerWhy` exists because a corrupt bundle or a
malformed content document used to escape this reporting entirely, through VContainer, and leave the
boot screen narrating a step that had already failed instead of saying why boot stopped.

## Entry point and game flow

`GameManager` is the shell, and it deliberately knows no minigame by type. It holds an authored id
(`chests` in the shipped scene), asks `IMinigameManager` for whatever is registered under it, and
drives it through the framework's own surface.

Asking for the minigame already running just restarts it. Asking for a different one tears the current
one down first, which is why the active id is tracked alongside the active container: the container's
type no longer identifies which minigame it is, because the shell only ever sees the base type back
from the manager.

Starting is asynchronous, so the shell guards it. A `_starting` flag stops a second press building a
second container while the first start is in flight, and the button is made non-interactable for the
duration, because a start that goes to the network can take long enough for a player to conclude the
button is broken. The cancellation token is the component's own, so a scene change mid-load unwinds
the start instead of finishing into a destroyed shell.

A failed start becomes a `ContentUnavailablePopup` carrying a plain sentence, not the exception's own
message, which names keys and labels the player has no use for. The catch is on `ChestGameException`
on purpose: a missing key and a broken download arrive as different types and read identically to
whoever is holding the phone, and anything not under that base is a bug rather than a delivery
problem, so it is left to blow up where it can be seen.

`OnDestroy` ends whatever is running, so the controller is disposed and the view destroyed rather
than left to the garbage collector with live subscriptions. `SetStartButtonInteractable` guards
`_startButton` with a null check because the button is gone by the time a start cancelled by this
object's own destruction unwinds, which is the ordinary shutdown path rather than an error.

An unfinished chests run persists, and so do currencies, including between sessions.
`ChestsRunSaveDocument` carries exactly two members, the chest count and which chests are open, and
the controller builds its own `SaveScheduler<ChestsRunSaveDocument>` because `Core` cannot reach into
this assembly to build one for it.

A restored run resumes its attempt count rather than resetting it: `Attempts` comes back as the
number of chests that were open. A run is discarded instead of restored when the saved chest count no
longer matches the configuration, when the indices are out of range or repeated, or when the run had
already used its attempts. Each of those is a state a save can legitimately be in, from a config
change or a hand-edited file, rather than defensive paranoia. Finishing
a run overwrites the save with an empty document in the same call, so a finished run never resumes.

The save cannot name where the prize is, and that is structural rather than a convention: see design
decision [#17](design-decisions.md#17-decision-9-is-enforced-by-the-save-models-shape-not-by-a-comment),
which also records the one thing this deliberately does not close: the attempt budget is re-rollable
by force-quitting inside the coalescing window.

## Config pipeline

Config is two documents, not one, because the values had two different owners.

`Content/GameConfig.json` holds what the whole game shares, currently the two reward amounts, and
reaches the game through three steps with one job each:

- `IGameConfigSource` fetches the raw document asynchronously. `AddressablesGameConfigSource` is the
  only class that knows the key, and it goes through `IAssetProvider` to turn that key into bytes.
- `LocalJsonGameConfig` parses and validates it. It loads nothing itself, and takes the document
  rather than the source, so parse-and-validate stays a synchronous constructor. This is deliberately
  a one-shot parse over an already-fetched document; a live remote config that could push updates
  while the game is running would likely need it to grow callbacks instead.
- `IGameConfig` is what the rest of the game consumes.

Pointing the game at a real remote config means registering a different source and changing nothing
else. The same three-step shape covers the other content: `IMinigameListSource`, `IPopupListSource`
and `IPopupParentSource` fetch, and the catalogs and provider they feed take plain, already-loaded
data.

A source that reached its document slot and found nothing hands back null, because "no config
shipped" is the parser's failure to describe. A source that cannot reach the document at all throws
`MissingAssetException` or `AssetLoadException` instead, because that is a different failure and the
caller can do different things about it.

`Minigames/Chests/ChestsMinigameConfig.json` holds the chests minigame's own values and is owned end
to end by that minigame. See [minigames.md](minigames.md).

Both documents validate at the boundary through `ConfigValidation` and throw `GameConfigException`,
which lives in `Common` so neither owner needs a reference to the other's assembly. A document can
parse cleanly and still describe something unplayable: a field the server renamed, or one this client
predates, deserializes to 0. Rewards cannot be negative, because a negative reward would be handed to
`AddCurrency`, which rejects it and logs an error on every single win.

An unrecognized field in the document is ignored rather than rejected, so a server rolling out a new
field does not break clients that predate it; `UnknownFields_AreIgnoredSoTheConfigCanGrowServerSide`
feeds a document carrying the chests minigame's own fields, which `LocalJsonGameConfig` no longer
knows about, alongside the two it does. Zero is accepted as distinct from negative: it is a legitimate
tuning value, a currency the game currently gives none of, and `ConfigValidation` only rejects a
reward going negative.

## Catalogs

The same three layers show up for both minigames and popups, with one concrete type per layer per
feature:

| Layer | Minigames | Popups |
|---|---|---|
| Authoring asset | `MinigameListSO` | `PopupListSO` |
| Lookup | `MinigameCatalog` (`IMinigameCatalog`) | `PopupCatalog` (`IPopupCatalog`) |
| Fetching | `AddressablesMinigameListSource` | `AddressablesPopupListSource` |

The `*ListSO` assets are pure authoring data, the list as the inspector holds it, holes and all.
`OnValidate` reports problems rather than throwing, because it runs during asset import and on every
inspector edit, where an exception aborts the surrounding Unity operation.

The catalogs take a plain `IReadOnlyList` and build the lookup, which makes them constructible in a
test with no asset involved. `MinigameCatalog` builds two lookups over the same entries: `Minigames`
keyed by container type for callers that already have the type, and `MinigamesById` keyed by authored
id for the shell, which must not.

The source classes know the addressable key and nothing else. They fetch the authoring asset through
`IAssetProvider` and hand the entries on. Two more follow the same shape without a catalog behind
them: `AddressablesGameConfigSource` and `AddressablesPopupParentSource`. All four keys are listed in
[content-delivery.md](content-delivery.md).

`CatalogBuilder` holds the shared policy. An empty slot is skipped with a warning, because the rest
of the game is still playable. A duplicate key throws `InvalidCatalogException`, because there is no
right answer for which entry wins. The `TEntry : UnityEngine.Object` constraint is deliberate: it
makes the null check use Unity's overloaded equality, which also catches destroyed objects.

`BuildById` adds the one rule a generic key cannot express. An id that was never authored is blank,
and blank is not a key, so that entry is skipped from the id lookup with a warning. It follows the
empty-slot reasoning, since the entry is still reachable by type and the game still runs, and it stops
two unauthored entries from colliding as a duplicate nobody wrote. An empty slot passes silently
there, because the type-keyed build over the same entries has already warned about it.

An empty inspector slot is the most common authoring mistake, and `OnValidate` itself leaves one
behind whenever it clears a duplicate it caught. `OnValidate` only guards inspector edits, though: a
merge or a hand-edited data file can still produce a duplicate type or id that reaches
`MinigameCatalog`'s constructor directly, which is why the catalog itself still has to check for one
rather than trusting authoring time to have caught it.

Within one `MinigameCatalog`, the type-keyed build runs before the id-keyed one. Two entries that
share a container type throw from the type-keyed lookup before the id-keyed lookup is ever reached, so
proving the id-keyed lookup's own duplicate check needs two entries of distinct container types
sharing an id.

## Popups

`PopupBase<TPopup, TData>` and `PopupManager` are a typed popup framework: popups receive
strongly-typed data on initialization rather than a stringly-typed dictionary. `PopupManager` takes an
`IPopupCatalog` and an `IPopupParentProvider` rather than loading anything itself, which leaves it
doing only what it is about: picking a prefab, picking a parent, handing over the data.

`PopupParentProvider` creates the shared `DontDestroyOnLoad` canvas lazily, on first use, from a
prefab that was handed to it already loaded. Resolving `IPopupManager` therefore has no side effects,
which matters because a `DontDestroyOnLoad` object built during resolution would leak into every
consumer of the container, tests included. There is a test pinning exactly that.

`IPopupParentProvider` is the one content source among the four `GameBootstrapperTests` exercises
whose result nothing else in that fixture would notice going missing: `PopupParentProvider` holds the
prefab it was given untouched until a popup is actually shown, so asking it for `Default` is what
forces the shipped prefab to have been real.

`AddressablesPopupParentSource` asks for the prefab as a `GameObject` and reads the component off it,
rather than asking for `PopupParent` directly. Whether a loader can hand back a component off a
prefab depends on the loader, and this way the answer does not have to be the same in every play mode
script.

`ContentUnavailablePopup` is what the player is shown when something the game had to fetch did not
arrive. It carries a message rather than a failure type, so one popup covers a missing key and a
broken download alike, and nothing about it names Addressables.

## Exception hierarchy

`ChestGameException` is the base. Under it: `MissingAssetException` (nothing ships under that key),
`AssetLoadException` (the key resolved and the load itself failed), `ContentDownloadTimeoutException`
(the fetch never answered), `InvalidCatalogException` (the asset is there and its contents are
wrong), `GameConfigException`, `MinigameNotFoundException`, `MinigameAlreadyRunningException`,
`PopupNotFoundException`, and `SaveException` with its subclass `SaveTamperedException` (a full disk,
a save a newer build wrote, a file that got truncated - all things that happen to a player who wired
the game correctly).

No bare `throw new Exception` remains in game code. A test asserting "this throws" should not be
satisfied by an unrelated `NullReferenceException` from somewhere inside the call, and a caller
should be able to tell a missing asset from a malformed one.

Five typed failures sit deliberately **outside** that base, all under `InvalidOperationException`:
`PoolException`, `FrameBudgetException`, `SaveMigrationException`, `SaveInspectorException` and
`PoolRaceException`. Being under `ChestGameException` is not a label in this project, it is behaviour.
`GameManager` catches exactly that base, turns whatever it caught into a content-unavailable popup and
treats it as handled, on the understanding that anything outside it is a bug and is left to blow up
where it can be seen. Everything those five types report is a wiring mistake: an unassigned prefab
slot, a holder that was never built, a view that was never injected, two migrations claiming the same
`FromVersion`, the pooling demo set up with an unset-up race, an unknown solo strategy, or an
unassigned document or prefab. Reporting one of those as a delivery failure would tell a player their
connection is bad and swallow the bug that caused it. `PrefabPoolTests` and `FrameBudgetedLoopTests`
each pin that with an `IsNotInstanceOf<ChestGameException>`, so a later tidy-up of the hierarchy cannot
quietly undo it.

Saving draws the same line twice, which is why it appears on both sides above: a save a build has no
path forward for is data and stays `SaveException.NoMigrationPath`, while a broken migration chain is
wiring and is `SaveMigrationException`. See [saving.md](saving.md), "Exceptions".

`InvalidCatalogException` carries its offending key as `object`, because the catalogs index by
different things: a container type for the type-keyed lookups, an authored string id for the
id-keyed one. Type keys keep their original wording; anything else is quoted in the message, because
a blank-looking id is otherwise invisible.

`MissingAssetException` carries its path as a plain string, whatever key the loader that raised it was
given, rather than an Addressables-specific type; `Common` holds no opinion about which loader that is.
The overload taking an inner exception exists because the loader that raised it knows why the lookup
failed; without it, only the key would reach the caller's report.

## Currency and rewards

`CurrencyManager` wraps [Resource Bank](https://gitlab.com/tn-asset-library/resource-bank), providing
events (`OnCurrencyChanged`, `OnCurrencyCollected`, `OnCurrencySpent`) and persistence. Add
currencies by extending the `CurrencyType` enum. It takes an
`IResourceBankSaveHandler<CurrencyType>` as its only constructor argument, registered in the scope,
so a test can hand it an in-memory save instead of the real one.

`OnCurrencyChanged` and `OnCurrencySpent` do not report the same number for the same spend:
`OnCurrencyChanged` always reports the delta applied to the balance (negative for a spend, positive
for an add), while `OnCurrencySpent` reports the size of the withdrawal as a positive amount. This
mirrors what `ResourceBankCallbacks` itself documents, and `FakeCurrencyManager` reproduces the same
asymmetry for tests that stand in for the real bank.

The balance lives in a file, not in PlayerPrefs: `CurrencyResourceBankSaveHandle` writes through
`ISaveService` to `<persistentDataPath>/Saves/currency.sav`, as readable, unprotected JSON swapped
into place rather than overwritten. PlayerPrefs holds only the one-time legacy import - the
`ResourceBankSaveData_CurrencyType` entry an already-installed player still has, which is read once
and then renamed to `ResourceBankSaveData_CurrencyType.migrated` rather than deleted. Do not read
that entry as the live balance; it is spent. See [saving.md](saving.md), "The legacy import".

The planned analytics hooks and purchase flow are recorded in [WIP.md](WIP.md).

One simplification against the library's own example: `ResourceBank.ResourceIdMap`, which maps enum
values to strings, was dropped. See
`Assets/AssetLibrary/ResourceBank/Examples/CurrencyManager/CurrencyManagerExample.cs` for the full
version.

`CurrencyWatcher` subscribes to the events and updates TextMeshPro labels in the UI. `RewardsManager`
picks a random currency reward from the config values and shows a `RewardReceivedPopup`.
