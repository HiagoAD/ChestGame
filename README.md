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
- [Unity Test Framework](https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/manual/index.html) — NUnit test runner for both suites

Game code lives under `Assets/_Project`, tests under `Assets/Tests`, and the vendored copy of
Resource Bank under `Assets/AssetLibrary`. Dependencies are registered as singletons in
`Assets/_Project/Scripts/Core/GameLifetimeScope.cs`.

### Assembly layout

Every script is inside an assembly definition, so nothing compiles into `Assembly-CSharp`
anymore. The split is what keeps the dependency directions honest — a reference that would
create a cycle fails to compile instead of quietly working.

```
Company.ChestGame.Common      _Project/Scripts/Common/     (leaf: engine seams, exceptions, catalog policy)
Company.ChestGame.Config      _Project/Scripts/Config/
Company.ChestGame.Currency    _Project/Scripts/Currency/
Company.ChestGame.Popups      _Project/Scripts/Popups/
Company.ChestGame.Minigame    _Project/Scripts/Minigames/
Company.ChestGame.Rewards     _Project/Scripts/Rewards/
Company.ChestGame.Gameplay    _Project/Scripts/Gameplay/
Company.ChestGame.UI          _Project/Scripts/UI/
Company.ChestGame.Core        _Project/Scripts/Core/       (composition root)
TapNation.Modules             AssetLibrary/                (vendored Resource Bank)

Company.ChestGame.Tests.Common    Tests/Common/            (fakes, shared by both suites)
Company.ChestGame.Tests.EditMode  Tests/EditMode/
Company.ChestGame.Tests.PlayMode  Tests/PlayMode/
```

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

### Entry point & game flow
- `GameManager.cs` is the main entry point. On button press it asks `IMinigameManager` for a
  minigame container and starts a new game. `StartMinigame<TMinigame>()` is generic: asking for
  the minigame already running just restarts it, asking for a different one tears the current
  one down first. `OnDestroy` ends whatever is running, so the controller is disposed and the
  view destroyed rather than left to the garbage collector with live subscriptions.
- `ChestsMinigameController.cs` holds the core logic: chest states, remaining attempts, async
  opening (two parallel UniTasks), and reward distribution. It touches neither `UnityEngine.Random`
  nor `Time` directly — both arrive through the seams above.
- `ChestsMinigameChestModel.cs` is the per-chest state, owned by the controller.
- `ChestsMinigameView.cs` and `ChestsMinigameChestElementView.cs` handle the UI, instantiating
  chest prefabs and reacting to model state changes. Views unsubscribe in `OnDestroy`, because
  the models belong to the controller and outlive the views showing them.

### Config pipeline

Three steps, each with one job:

- `IGameConfigSource` fetches the raw document. `ResourcesGameConfigSource` is the only class
  that knows the asset path.
- `LocalJsonGameConfig` parses and validates it. It loads nothing itself.
- `IGameConfig` is what the rest of the game consumes.

Pointing the game at a real remote config means registering a different source and changing
nothing else. Validation rejects `ChestCount <= 0`, `AttempsCount <= 0`, and negative times or
rewards, throwing `GameConfigException` at the boundary — a document can parse cleanly and still
describe a round that can never be played or never end.

### Generic minigame framework

`MinigameBase`, `MinigameContainer`, and `MinigameManager` register and instantiate minigames
through ScriptableObject definitions, with VContainer resolving dependencies. The set of
minigames sits behind `IMinigameCatalog`, so `MinigameManager` depends on a catalog rather than
on a ScriptableObject at a particular Resources path.

`MinigameContainer.Begin` instantiates the view through the container; `End` disposes the
controller and destroys the view. `End` is idempotent and safe on a minigame that was never
begun, so callers can tear down unconditionally.

### Popup system

`PopupBase<TPopup, TData>` and `PopupManager` provide a typed popup framework — popups receive
strongly-typed data on initialization. `PopupManager` takes an `IPopupCatalog` and an
`IPopupParentProvider` rather than loading from Resources itself, which leaves it doing only
what it is about: picking a prefab, picking a parent, handing over the data.

`ResourcesPopupParentProvider` creates the shared `DontDestroyOnLoad` canvas lazily, on first
use. Resolving `IPopupManager` from the container therefore has no side effects, which matters
because a `DontDestroyOnLoad` object built during resolution would leak into every consumer of
the container, tests included. There is a test pinning exactly that.

### Catalogs

The same three layers show up for both minigames and popups:

- `*ListSO` — pure authoring data, the list as the inspector holds it, holes and all.
- `*Catalog` — takes a plain `IReadOnlyList` and builds the type-keyed lookup. Constructible in
  a test with no asset involved.
- `Resources*Catalog` — a subclass that knows the Resources path and nothing else.

`CatalogBuilder.Build` holds the shared policy: an empty slot is skipped with a warning, because
the rest of the game is still playable; a duplicate type throws `InvalidCatalogException`,
because there is no right answer for which entry wins. The `TEntry : UnityEngine.Object`
constraint is deliberate — it makes the null check use Unity's overloaded equality, which also
catches destroyed objects.

### Exception hierarchy

`ChestGameException` is the base, with `MissingAssetException` (the asset isn't there),
`InvalidCatalogException` (the asset is there and its contents are wrong), `GameConfigException`,
`MinigameNotFoundException`, and `PopupNotFoundException` under it. No bare `throw new Exception`
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

109 tests, split across two suites:

| Suite    | Tests | Wall time |
|----------|-------|-----------|
| EditMode | 95    | ~0.5 s    |
| PlayMode | 14    | ~14 s     |

EditMode covers the logic exhaustively against fakes. PlayMode is a thin layer of integration
smoke tests for the things only a real player loop or a real prefab can prove — that
`UnityGameClock` drives the same chest flow the fake clock does, that a view really unsubscribes
when destroyed, that a popup really lands under its parent.

The time difference is the point. The edit-mode suite runs the entire chest-opening flow,
including cancellation, without waiting for anything, because `FakeGameClock` decides when a
frame happens. The 14 seconds in play mode are almost entirely one class waiting on real
timers, which is why so little lives there.

Play-mode tests assert settled states rather than mid-flight ones, so a slow frame on a cold CI
runner can't cause a spurious failure.

Fakes live in `Tests/Common/` and are shared by both suites. There is deliberately no fake
catalog: `PopupCatalog` and `MinigameCatalog` take plain lists, so the tests use the real ones.

`GameLifetimeScopeTests` runs against `GameLifetimeScope.RegisterServices` itself rather than a
copy of it, so dropping a registration from the composition root fails there.

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

5. **Generic minigame framework** — `MinigameBase<TController, TView, TMinigame>` uses generics and ScriptableObject definitions to make adding a new minigame a matter of creating an SO asset and a controller/view pair, without modifying the framework. Teardown is part of it: `MinigameContainer.End()` disposes the controller and destroys the view, and is safe to call unconditionally.

6. **Typed popup system** — `PopupBase<TPopup, TData>` ensures each popup receives strongly-typed data at initialization, avoiding stringly-typed or untyped dictionaries.

7. **Authoring data separated from lookups** — The `*ListSO` assets hold what the inspector holds; the catalogs turn that into a usable lookup and decide what counts as unusable; the `Resources*Catalog` subclasses know the paths. Beyond keeping each piece small, it means a catalog can be built in a test from a plain list, with no asset and no Resources folder.

8. **An exception type per failure** — Every failure the game reports deliberately derives from `ChestGameException`. A caller can tell a missing asset from a malformed one, and a test asserting a specific failure can't be accidentally satisfied by a `NullReferenceException`.

9. **Prize location calculated per run** — The prize chest is determined at runtime on each attempt rather than stored in memory, to avoid being discoverable through memory inspection. Unrealistic for a production game, but it works as a demonstration. The odds model exactly one prize among the chests: with N chests and k already opened empty, the one being opened now holds it with probability 1/(N − k). Since `Attempts` is already incremented by that point, the divisor is `Chests.Count - Attempts + 1` — dropping the `+ 1` makes the odds reach certainty one chest early, so the last chest could never hold the prize.

More specific reasoning is written above key parts inside the scripts.

For the reasoning behind how the code got this shape, including approaches that were tried and
replaced, the verification workflow, Unity behaviour worth knowing before changing anything
structural, and the current known gaps, see [docs/ENGINEERING_NOTES.md](docs/ENGINEERING_NOTES.md).

## 4. Instructions to Build and Run

* Open the project in **Unity 6000.3.11f1** (or compatible)
* To play in the Editor, open the main scene and press Play
* To run the tests, see [Running the tests](#running-the-tests) above
* To build for Android, switch the target platform to Android and follow the standard build procedure

The project was validated on Editor and Android build, targeting 1080x1920 Portrait, with Landscape validated to be working. No further specific instructions are needed.
