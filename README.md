# ChestGame

Project made in Unity 6000.3.11f1

## Table of Contents
- [0. What is this?](#0-what-is-this)
- [1. High-Level Architecture](#1-high-level-architecture)
- [2. Testing](#2-testing)
- [3. Key Design Decisions](#3-key-design-decisions)
- [4. Instructions to Build and Run](#4-instructions-to-build-and-run)

## 0. What is this?

The project started as a demonstration of async operations in Unity, specifically
managing the delayed opening of chests using UniTask with parallel tasks and
cancellation tokens.

It has since evolved into my personal study and showcase project. The goal is not to
build a functional, fun game, but rather to demonstrate architecture choices and
features commonly used across the mobile game industry: dependency injection,
event-driven currency systems, generic minigame and popup frameworks, and remote
config simulation.

### AI
This is not a vibe-coded project, but it is AI-enhanced. Up until the Hand-Written tag,
nothing was done by AI, but my goal is to use it to write down the
boilerplate, while I handle the architecture and high-level decisions.
Since then, I've used it to write the code, while I do the architecture
and design decisions. 

This section 0 is the only part of this document that
can't be changed by AI and is all hand-written.

The notes from the AI sessions are under `docs`.

## 1. High-Level Architecture

The project uses the following libraries:
- [UniTask](https://github.com/Cysharp/UniTask) — async/await for Unity with no allocations
- [VContainer](https://github.com/hadashiA/VContainer) — Dependency Injection
- [Resource Bank](https://gitlab.com/tn-asset-library/resource-bank) — persistent currency management
- [Newtonsoft.Json](https://www.newtonsoft.com/json) — JSON deserialization for config loading
- [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@2.9/manual/index.html) — content
  loading by key or by authored reference, kept behind a seam of the project's own
- [Unity Test Framework](https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/manual/index.html) — NUnit test runner for both suites

Game code lives under `Assets/_Project`, tests under `Assets/Tests`, and the vendored copy of
Resource Bank under `Assets/AssetLibrary`. Most of the content the game loads by key lives under
`_Project/Content`; there is no `Resources` folder of ours any more. Dependencies are registered
as singletons in `Assets/_Project/Scripts/Core/GameLifetimeScope.cs`.

### Assembly layout

Every script is inside an assembly definition, so nothing compiles into `Assembly-CSharp`
anymore. The split is what keeps the dependency directions honest — a reference that would
create a cycle fails to compile instead of quietly working.

```
Company.ChestGame.Common      _Project/Scripts/Common/     (leaf: engine seams, exceptions, catalog policy)
Company.ChestGame.Assets      _Project/Scripts/Assets/     (the only assembly that calls Addressables)
Company.ChestGame.Config      _Project/Scripts/Config/
Company.ChestGame.Currency    _Project/Scripts/Currency/
Company.ChestGame.Popups      _Project/Scripts/Popups/
Company.ChestGame.Minigame    _Project/Scripts/Minigames/  (the framework, no minigame in it)
Company.ChestGame.Rewards     _Project/Scripts/Rewards/
Company.ChestGame.Gameplay    _Project/Scripts/Gameplay/    (the shell: GameManager and nothing else)
Company.ChestGame.UI          _Project/Scripts/UI/
Company.ChestGame.Core        _Project/Scripts/Core/       (composition root)

Company.ChestGame.Minigame.Chests  _Project/Minigames/Chests/Scripts/   (one minigame, code and assets)

TapNation.Modules             AssetLibrary/                (vendored Resource Bank)

Company.ChestGame.Tests.Common    Tests/Common/            (fakes, shared by both suites)
Company.ChestGame.Tests.EditMode  Tests/EditMode/
Company.ChestGame.Tests.PlayMode  Tests/PlayMode/
```

The chests minigame is an assembly of its own, and the shell does not reference it. Everything the
minigame owns — controller, view, model, definition asset, prefabs, sprite, config document — sits
under `_Project/Minigames/Chests/`, so "what belongs to this minigame" has an answer the compiler
agrees with rather than one held up by convention. `GameManager` asks for a minigame by authored id,
which is the only reason it can start one without naming its type.

`Common` is deliberately a leaf — it references only UniTask. The engine seams below would
otherwise be a natural fit for `Core`, but `Core` already depends on `Rewards`, and `Rewards`
needs the seams, so putting them there would close a reference cycle.

### Engine seams: clock and random

`IRandomProvider` and `IGameClock` are thin interfaces over the two pieces of the engine
gameplay logic can't be tested around: `UnityEngine.Random`, and the player loop
(`Time.deltaTime`, `UniTask.Yield`, `UniTask.Delay`). `UnityRandomProvider` and `UnityGameClock`
are the production implementations, and both are registered in the scope like anything else.

This is the piece the rest of the testing story hangs on. Because the chest-opening flow draws
time and randomness through these, the whole thing — two parallel UniTasks, cancellation paths
and all — runs in edit mode with no player loop and no real waiting. The test double
`FakeGameClock` parks awaiters and releases them from `AdvanceFrame()`, and because the
continuations resume synchronously inside that call, a test can assert the moment it returns.

### Asset loading

`IAssetProvider` is the seam over how an asset is fetched. It has two routes in, because content is
named two different ways: a key, for a source that owns one, and an `AssetReference`, for a
definition asset that was authored pointing at its own content. `Release` is the third member, and
it takes a reference rather than a handle, so nothing outside the seam ever holds an Addressables
type. `AddressablesAssetProvider` is the production implementation and the only class in the project
that calls Addressables.

That is the same "one place knows the path" discipline the sources always had, raised one level to
"one assembly knows the loading technology". Swapping Addressables for something else is one
registration and one class.

The exception is the type `AssetReference` itself. A serialized reference is only authorable in an
assembly that can see the type, so the assemblies that author content references — `Minigame` and
`Minigame.Chests` — reference the package too, and so does every assembly that merely *calls*
`IAssetProvider`, because an overload naming `AssetReference` has to be resolvable at the call site.
What still holds, and what the seam is actually for, is that exactly one assembly *calls* the
library.

The provider's second job is translating failures. Addressables reports its own exception types,
which would leak the technology into every catch site, so a key or reference that is not in the
catalog becomes `MissingAssetException` and any other load failure becomes `AssetLoadException` —
both under `ChestGameException`, like everything else the game throws.

Shared content sits in `_Project/Content`, addressable under a single local group called `Core` —
with one exception worth knowing before you go looking for it: `RewardReceivedPopup.prefab` is a
`Core` entry addressed `Popups/RewardReceivedPopup`, and it still lives at
`_Project/Prefabs/UI/Rewards/` with the rest of the UI prefabs rather than under `_Project/Content`.
`ContentUnavailablePopup.prefab` sits beside it and is not a group entry at all; it reaches the
bundle as a dependency of `PopupList.asset`, which is. Group membership is what decides what ships
where, so neither is a bug — but the folder is not the authority on it, and only the group is.

A minigame's own content sits with the minigame and has a group of its own — `Minigame.Chests`, whose
four entries all carry the label `minigame.chests`. The committed settings leave the play mode
script on **Use Asset Database (fastest)** so both test suites run with no content build.

### Content delivery

Two groups, delivered differently.

| Group | Build path | Load path | Why |
|---|---|---|---|
| `Core` | `Local.BuildPath` | `Local.LoadPath` | Ships inside the player |
| `Minigame.Chests` | `ServerData/[BuildTarget]` | `http://localhost:8080/[BuildTarget]` | Fetched over the wire |

`Core` is local deliberately, and it is not an oversight that the two are not treated alike: the
game config has to parse before anything can render, and the thing that reports a failed download
is a popup, whose prefab and parent are themselves content. A remote `Core` would mean a game that
cannot tell you why it could not start.

`BuildRemoteCatalog` is on, so the catalog is fetched from the same server as the bundles. That is
what makes a content update a content update: change the chests config or its art, run the content
build, upload `ServerData/`, and the shipped app picks it up with no store release.

How a minigame's content arrives is authored on its own descriptor, as a `MinigameLoadPolicy` next
to the label naming that content:

- **`Preload`** — fetched during boot, before the player can press anything.
  `MinigameContentPreloader` walks the catalog, sums the download sizes of every preloaded label,
  and downloads them reporting one aggregate progress figure to the boot scene's status label. A
  minigame set to preload but naming no label is skipped with a warning, the same policy the
  catalogs apply to a blank id.
- **`OnDemand`** — fetched by `MinigameContainer.BeginAsync`, which is the one moment the game knows
  the content is about to be needed. It asks for the size first and downloads only if there is
  something to download, so the ordinary case — content already cached, or a build that shipped it
  local — costs one query and no wait. The chests minigame is `OnDemand`.

`GameManager` makes the start button non-interactable while a start is in flight and turns a failed
one into a `ContentUnavailablePopup` rather than leaving a button that silently does nothing. There
is no progress bar in the game scene; the only place a download is narrated is boot.

**A minigame's code is always local.** C# assemblies cannot ship inside an AssetBundle — Unity
builds managed code into the player, not into content — so `Company.ChestGame.Minigame.Chests` is in
every build whether or not its content ever arrives. What the split buys is that the *assets* (a
prefab, a sprite, a config document — nearly all of the megabyte) are what travels, and they travel
without an app update. A minigame that could be added after ship would need scripting to be data
too, which is a different project.

### Boot phase

The game starts in `Scenes/Boot.unity`, not in the game scene. `GameLifetimeScope` lives there,
registers everything that needs no asset, and survives the scene load. `GameBootstrapper` then does
four things in the order the whole design rests on: `GameContentLoader` reads every source,
`RegisterLoadedServices` builds a child scope from what came back, `MinigameContentPreloader` fetches
whatever asked to arrive up front, and only then does `Game.unity` open — parented to that scope
through `LifetimeScope.EnqueueParent`.

Each step reports through `IBootStatus`, whose only implementation in the scene is `BootStatusLabel`,
a component holding the label the boot scene already had. The bootstrapper therefore narrates the
wait without naming a TextMeshPro component, and a container built by a test resolves a silent one.

The point is that no service ever exists with its data not yet arrived, so nothing anywhere has to
ask whether loading has finished. `GameContentLoader` is a plain class with no scene or scope in it,
which is what keeps the untestable part of booting down to the three lines in the bootstrapper.

Opening `Game.unity` directly will not work: its scope expects a parent that only the boot scene
builds.

### Entry point & game flow
- `GameManager.cs` is the main entry point. On button press it asks `IMinigameManager` for the
  container registered under its serialized `_minigameId` and starts a new game. Asking for the
  minigame already running just restarts it, asking for a different one tears the current one down
  first, which is why the active id is tracked alongside the active container. `OnDestroy` ends
  whatever is running, so the controller is disposed and the view destroyed rather than left to the
  garbage collector with live subscriptions. It names no minigame type, and its assembly references
  no minigame's assembly.
- `ChestsMinigameController.cs` holds the core logic: chest states, remaining attempts, async
  opening (two parallel UniTasks), and reward distribution. It touches neither `UnityEngine.Random`
  nor `Time` directly — both arrive through the seams above.
- `ChestsMinigameChestModel.cs` is the per-chest state, owned by the controller.
- `ChestsMinigameView.cs` and `ChestsMinigameChestElementView.cs` handle the UI, instantiating
  chest prefabs and reacting to model state changes. Views unsubscribe in `OnDestroy`, because
  the models belong to the controller and outlive the views showing them.

### Config pipeline

Config is two documents, not one, because the values had two different owners.

`Content/GameConfig.json` holds what the whole game shares, currently the two reward amounts,
and reaches the game through three steps, each with one job:

- `IGameConfigSource` fetches the raw document, asynchronously. `AddressablesGameConfigSource` is the
  only class that knows the key, and it goes through `IAssetProvider` to turn that key into bytes —
  so the wait in the signature is now a real one.
- `LocalJsonGameConfig` parses and validates it. It loads nothing itself.
- `IGameConfig` is what the rest of the game consumes.

Pointing the game at a real remote config means registering a different source and changing nothing
else. The same three-step shape now covers the other content too: `IMinigameListSource`,
`IPopupListSource` and `IPopupParentSource` fetch, and the catalogs and provider they feed take
plain, already-loaded data.

`Minigames/Chests/ChestsMinigameConfig.json` holds the chests minigame's own values — chest count,
attempts, open time — and is owned end to end by that minigame. `ChestsMinigameSO` names the
document with an `AssetReferenceT<TextAsset>`, fetches it when the minigame begins, and
`ChestsMinigameConfig.Parse` validates it. Nothing outside the chests
assembly can name a field called `ChestCount`, and the chests code needs no reference to the config
assembly at all. That is the point: a minigame that owns its own content can later ship as one unit.

Both documents validate at the boundary and throw `GameConfigException`, which lives in `Common`
so neither owner has to reach for the other's assembly. The rules are that rewards cannot be
negative, and that a chests round cannot describe itself as unplayable: `ChestCount <= 0`,
`AttempsCount <= 0`, and a negative open time are rejected, because a document can parse cleanly
and still describe a round that can never be played or never end.

### Generic minigame framework

`MinigameBase`, `MinigameContainer`, and `MinigameManager` register and instantiate minigames
through ScriptableObject definitions, with VContainer resolving dependencies.

A definition asset **names** its content rather than holding it. The view is an
`AssetReferenceGameObject` and the chests config document is an `AssetReferenceT<TextAsset>`, both
of which serialize as a GUID string rather than as an object reference — which is the whole point,
because a direct field would make loading the descriptor drag the minigame's entire bundle in
behind it. `MinigameBaseSO` also carries a `ContentLabel` and a `MinigameLoadPolicy`, the two
fields the delivery choices above are authored on.

Because a reference cannot be resolved synchronously, nothing content-shaped can happen while the
container is being built. `MinigameManager.Get` constructs the container and injects it, and stops
there. `MinigameContainer.BeginAsync` does the rest, in one order that is a framework promise rather
than an accident of layout: load the view, run the definition's `ConfigureControllerAsync` hook,
inject the controller, instantiate the view. That hook is the one extension point a concrete
minigame gets — it is how a controller builds state from its own config document and is still
injected on top of it. A minigame needing no content of its own overrides nothing.

The view is instantiated through the container rather than through `Addressables.InstantiateAsync`,
so it and everything under it are injected the way every other object in the game is. `End` disposes
the controller, destroys the view, and releases the handles `BeginAsync` took. It releases the
*assets* rather than the instance, which is what lets it stay synchronous; it is idempotent and safe
on a minigame that was never begun, so callers can tear down unconditionally.

The set of minigames sits behind `IMinigameCatalog`, so `MinigameManager` depends on a catalog
rather than on a ScriptableObject at a particular key.

### Popup system

`PopupBase<TPopup, TData>` and `PopupManager` provide a typed popup framework — popups receive
strongly-typed data on initialization. `PopupManager` takes an `IPopupCatalog` and an
`IPopupParentProvider` rather than loading anything itself, which leaves it doing only
what it is about: picking a prefab, picking a parent, handing over the data.

`PopupParentProvider` creates the shared `DontDestroyOnLoad` canvas lazily, on first
use, from a prefab that was handed to it already loaded. Resolving `IPopupManager` from the container therefore has no side effects, which matters
because a `DontDestroyOnLoad` object built during resolution would leak into every consumer of
the container, tests included. There is a test pinning exactly that.

### Catalogs

The same three layers show up for both minigames and popups:

- `*ListSO` — pure authoring data, the list as the inspector holds it, holes and all.
- `*Catalog` — takes a plain `IReadOnlyList` and builds the lookup. Constructible in a test with no
  asset involved. `MinigameCatalog` builds two over the same entries: keyed by container type for
  callers that already have the type, and keyed by authored id for the shell, which must not.
- `Addressables*Source` — the class that knows the addressable key and nothing else. It fetches the
  authoring asset through `IAssetProvider` and hands the entries on.

`CatalogBuilder` holds the shared policy: an empty slot is skipped with a warning, because
the rest of the game is still playable; a duplicate key throws `InvalidCatalogException`,
because there is no right answer for which entry wins. The `TEntry : UnityEngine.Object`
constraint is deliberate — it makes the null check use Unity's overloaded equality, which also
catches destroyed objects.

`BuildById` adds the one rule a generic key cannot express: an id that was never authored is blank,
and blank is not a key, so that entry is skipped from the id lookup with a warning. It follows the
empty-slot reasoning — the entry is still reachable by type and the game still runs — and it stops
two unauthored entries from colliding as a duplicate nobody wrote.

### Exception hierarchy

`ChestGameException` is the base, with `MissingAssetException` (nothing ships under that key),
`AssetLoadException` (the key resolved and the load itself failed), `InvalidCatalogException` (the
asset is there and its contents are wrong), `GameConfigException`, `MinigameNotFoundException`, and
`PopupNotFoundException` under it. No bare `throw new Exception`
remains in game code. The point is stated in a comment on the base class: a test asserting
"this throws" shouldn't be satisfied by an unrelated `NullReferenceException` from somewhere
inside the call.

### Currency & rewards
- `CurrencyManager` wraps Resource Bank, providing events (`OnCurrencyChanged`,
  `OnCurrencyCollected`, `OnCurrencySpent`) and persistence. It takes an
  `IResourceBankSaveHandler<CurrencyType>` as its only constructor argument, registered in the
  scope, so a test can hand it an in-memory save instead of PlayerPrefs.
- `CurrencyWatcher.cs` subscribes to these events and updates TextMeshPro labels in the UI.
- `RewardsManager` picks a random currency reward from config values and shows a
  `RewardReceivedPopup`.

## 2. Testing

197 tests, split across two suites:

| Suite    | Tests | Wall time |
|----------|-------|-----------|
| EditMode | 165   | ~0.4 s    |
| PlayMode | 32    | ~20 s     |

The EditMode runner reports 166: the Addressables package ships one editor test of its own
(`Unity.Addressables.DocExampleCode.Editor.Tests`) and Unity picks it up. It is not ours and it is
not counted above.

EditMode covers the logic exhaustively against fakes. PlayMode is a thin layer of integration
smoke tests for the things only a real player loop, a real prefab or a real content catalog can
prove — that `UnityGameClock` drives the same chest flow the fake clock does, that a view really
unsubscribes when destroyed, that a popup really lands under its parent, and that the keys and
authored references the game ships really do resolve through Addressables.

`FakeAssetProvider` is what keeps the fast suite off Addressables entirely, the same way
`FakeGameClock` keeps it off the player loop: the four content sources are asked what key they want
and what they do with the answer, with no catalog and no bundle behind them.

The time difference is the point. The edit-mode suite runs the entire chest-opening flow,
including cancellation, without waiting for anything, because `FakeGameClock` decides when a
frame happens. The 20 seconds in play mode are almost entirely one class waiting on real
timers, which is why so little lives there.

Play-mode tests assert settled states rather than mid-flight ones, so a slow frame on a cold CI
runner can't cause a spurious failure.

Fakes live in `Tests/Common/` and are shared by both suites. There is deliberately no fake
catalog: `PopupCatalog` and `MinigameCatalog` take plain lists, so the tests use the real ones.

`GameLifetimeScopeTests` runs against `GameLifetimeScope.RegisterCoreServices` and
`RegisterLoadedServices` themselves rather than a copy of them, so dropping a registration from the
composition root fails there.

### Running the tests

In the editor: **Window → General → Test Runner**, then run the EditMode or PlayMode tab.

From the command line, with the editor closed:

```bash
ci/run-tests.sh             # both suites
ci/run-tests.sh EditMode    # one of them
```

It finds the editor from the version in `ProjectSettings/ProjectVersion.txt`, or uses `$UNITY`
if you point it somewhere else. Results land in `ci-results/` as NUnit XML plus the editor log.
Both suites run even if the first one fails, and the exit code is nonzero if either did.

`ci/build-addressables.sh` has the same shape and builds the addressable content, which the tests
deliberately do not need — both suites run against the asset database.

### CI

There is no pipeline yet. `ci/run-tests.sh` is the part that isn't provider-specific, so
whatever runs it later only has to check out the repo, supply a licensed Unity, and call one
command.

Two things any Unity pipeline needs regardless of provider. A licence has to be supplied at
runtime, which for a Personal licence means the account credentials reaching the runner as
secrets. And `Library/` has to be cached, keyed on `Assets/`, `Packages/` and `ProjectSettings/`
— without it every run re-imports the project from scratch, which costs several minutes against
the 15 seconds the tests actually take.

## 3. Key Design Decisions

1. **Library adoption** — UniTask eases async handling, VContainer structures dependency management, and Resource Bank speeds up currency system development. These libraries were chosen to increase reliability and reduce boilerplate.

2. **Parallel async tasks for chest opening** — In `ChestsMinigameController`, the opening state is intentionally split into two concurrent UniTasks: one updating the progress slider every frame, the other awaiting the delay timer. This demonstrates how to handle multiple async operations running in parallel while also providing visual feedback of the chest state.

3. **Engine seams over the clock and the random source** — Everything asynchronous in the game reaches the player loop through `IGameClock`, and everything random through `IRandomProvider`. This is what makes the chest flow testable at all: a suite that had to wait on real timers would be slow, and one racing a real stopwatch would be flaky. Both are registered in the scope alongside everything else, so the game gets the real ones and only tests substitute.

4. **Fetching split from parsing in the config** — `IGameConfigSource` fetches the document, `LocalJsonGameConfig` parses and validates it. Swapping the local JSON for a real remote config touches one registration and no parsing rules, and it makes the failure cases — missing document, malformed payload, out-of-range values — reachable from a unit test.

   The document is split by owner as well: values only one minigame cares about live in that minigame's own document, parsed by that minigame. A shared config that every feature bolts its fields onto grows into a god-object, and it is the thing that stops a feature from ever being self-contained.

5. **Generic minigame framework** — `MinigameBase<TController, TView, TMinigame>` uses generics and ScriptableObject definitions to make adding a new minigame a matter of creating an SO asset and a controller/view pair, without modifying the framework. Teardown is part of it: `MinigameContainer.End()` disposes the controller, destroys the view, releases what `BeginAsync` loaded, and is safe to call unconditionally.

   A definition names its content instead of holding it. `AssetReference` serializes as a GUID rather than as a dependency, so the descriptor a catalog holds for every minigame costs nothing to load, and a minigame's bundle is pulled in only when that minigame actually starts. It also forces the loading to move: nothing about a reference can be resolved synchronously, so `Begin` became `BeginAsync` and the configure-before-inject contract moved into it intact.

6. **Typed popup system** — `PopupBase<TPopup, TData>` ensures each popup receives strongly-typed data at initialization, avoiding stringly-typed or untyped dictionaries.

7. **Authoring data separated from lookups** — The `*ListSO` assets hold what the inspector holds; the catalogs turn that into a usable lookup and decide what counts as unusable; the `Addressables*Source` classes know the keys. Beyond keeping each piece small, it means a catalog can be built in a test from a plain list, with no asset and no content catalog.

8. **An exception type per failure** — Every failure the game reports deliberately derives from `ChestGameException`. A caller can tell a missing asset from a malformed one, and a test asserting a specific failure can't be accidentally satisfied by a `NullReferenceException`.

9. **Prize location calculated per run** — The prize chest is determined at runtime on each attempt rather than stored in memory, to avoid being discoverable through memory inspection. Unrealistic for a production game, but it works as a demonstration. The odds model exactly one prize among the chests: with N chests and k already opened empty, the one being opened now holds it with probability 1/(N − k). Since `Attempts` is already incremented by that point, the divisor is `Chests.Count - Attempts + 1` — dropping the `+ 1` makes the odds reach certainty one chest early, so the last chest could never hold the prize.

10. **One assembly calls the loading technology** — Every asset the game loads goes through `IAssetProvider`, and `Company.ChestGame.Assets` is the only assembly that calls Addressables. The package's own exception types stop at that boundary and are translated into `MissingAssetException` and `AssetLoadException`. The point is the same one the source classes make about paths, one level up: the day this game loads content some other way, exactly one assembly changes.

    This is deliberately weaker than it was. It used to be that no other assembly *referenced* the package either, and authored `AssetReference` fields ended that: a serialized reference needs the type at compile time, and so does any call site that has to resolve an overload naming it. The reference is the mechanism that keeps a minigame's content out of the descriptor, so the narrower invariant was the thing that had to give.

11. **A group per minigame, and delivery authored on the descriptor** — `Minigame.Chests` is a
    group of its own rather than a folder inside `Core`, which is what lets that content be built,
    labelled, bundled and shipped as one unit. The descriptor carries the label and a
    `MinigameLoadPolicy`, so *how* a minigame's content arrives is a property of that minigame
    rather than a branch in whatever code fetches it. Adding a second minigame that preloads while
    the chests one stays on demand is two authored fields and no code.

    The group is also the only honest unit of measurement: a download size and a progress figure are
    both per label, and a label is what a group's entries share.

12. **Remote content, local core** — `Minigame.Chests` loads over HTTP and `Core` does not. The
    asymmetry is deliberate and is the argument for where the line sits: the config has to parse
    before anything renders, and a failed download is reported by a popup, which is itself content.
    Everything needed to say "this did not work" ships inside the player; everything that is only
    needed once the player asks for it does not.

    `BuildRemoteCatalog` follows from the same decision. Content that can change without an app
    update is only useful if the catalog describing it can change too.

More specific reasoning is written above key parts inside the scripts.

For the reasoning behind how the code got this shape, including approaches that were tried and
replaced, the verification workflow, Unity behaviour worth knowing before changing anything
structural, and the current known gaps, see the context files under [docs/context/](docs/context/).
Each covers the work of one development pass, and each is kept current:

- [assemblies-and-tests.md](docs/context/assemblies-and-tests.md) — how the codebase became
  testable: the assembly definitions, the test suite, and the seams that testing revealed were
  missing.
- [self-contained-minigames.md](docs/context/self-contained-minigames.md) — how a minigame became a
  unit of content delivery: the config split, its own assembly, the boot scene, and Addressables.

## 4. Instructions to Build and Run

* Open the project in **Unity 6000.3.11f1** (or compatible)
* To play in the Editor, open `Assets/_Project/Scenes/Boot.unity` and press Play. Starting from
  `Game.unity` will not work — the boot scene is what loads the content and builds the scope it needs
* To run the tests, see [Running the tests](#running-the-tests) above
* To build for Android, switch the target platform to Android and **build the content first**:

  ```bash
  ci/build-addressables.sh
  ```

  or **Window → Asset Management → Addressables → Groups → Build → New Build → Default Build
  Script**, or **Build → Addressables Content** from the menu bar. Then follow the standard player
  build procedure. This is no longer optional the way it was while both groups were local:
  `Minigame.Chests` is remote, so its bundle and the remote catalog are written to
  `ServerData/[BuildTarget]/` and are not part of the player at all
* To serve that content locally, from the repo root:

  ```bash
  python3 -m http.server 8080 --directory ServerData
  ```

  which is exactly what the profile's `RemoteLoadPath` (`http://localhost:8080/[BuildTarget]`)
  points at. A device on the same network needs that host swapped for the machine's LAN address,
  and a real release needs it swapped for wherever the content is actually hosted. With nothing
  serving it, the game still boots — `Core` is local — and starting the chests minigame fails into
  a `ContentUnavailablePopup`, which is the behaviour that path exists to give

The project was validated on Editor and Android build, targeting 1080x1920 Portrait, with Landscape validated to be working. No further specific instructions are needed.
