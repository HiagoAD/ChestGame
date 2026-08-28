# Context: assembly definitions and the test suite

Working context from the session that took this repo from zero tests and zero `.asmdef` files to a
suite that covers the game, and reshaped the production code where testing it revealed a missing
seam. That is the scope of this file: **how the codebase became testable, and what was learned doing
it.** The delivery work that came after — the config split, the per-minigame assembly, the boot
scene and Addressables — has its own file,
[self-contained-minigames.md](self-contained-minigames.md).

**Kept current.** This is not a frozen record. Where later work changed something described here,
the description was updated rather than left to rot, so everything below is true of the project as
it stands. What has *not* been done is widening the scope: traps, decisions and gaps belonging to
the delivery work live in the other file, not this one.

The reference files under [docs/](../README.md) describe what the architecture is. This file covers
why it landed that way, what was tried and rejected, how to verify changes, and which traps cost time
the first time round. Read [architecture.md](../architecture.md) first for the map; read both context
files before changing anything structural.

---

## 1. How the current shape came about

The repo had `com.unity.test-framework` installed, zero tests, and zero `.asmdef` files. Everything
compiled into `Assembly-CSharp`. That blocks testing outright, because an assembly definition cannot
reference a predefined assembly, so no test assembly could see `ChestsMinigameController` or
`CurrencyManager`.

The work started as "add asmdefs plus tests" and went through several rounds of review. Most of the
current design exists because a first attempt was wrong in a way worth recording:

| First attempt | Why it was replaced |
|---|---|
| `internal OpenChest` + `InternalsVisibleTo` so edit-mode tests could skip the async path | It tested around the project's headline feature. The real seam was time, not visibility. `IGameClock` replaced it and `AssemblyInfo.cs` was deleted. |
| `ContainerWiringTests` that rebuilt the registration list by hand | It duplicated `Configure` and never loaded `GameLifetimeScope`, so deleting a registration passed. `RegisterCoreServices` and `RegisterLoadedServices` were extracted and the tests now run against them. |
| Two `CurrencyManager` constructors, `[Inject]` on the parameterless one | It created an extension point and sealed it in the same commit. The save handler is now registered in the scope and there is one constructor. |
| `PopupManager` loading Resources in its constructor | Left it play-mode only and leaking canvases between tests. `IPopupCatalog` plus a lazy `IPopupParentProvider` moved most popup tests to edit mode. |
| Catalogs building their dictionary with `ToDictionary` | Threw `NullReferenceException` on an empty inspector slot and `ArgumentException` on a duplicate. `CatalogBuilder` now skips nulls with a warning and throws `InvalidCatalogException` on duplicates. |

The through-line: when a test needs production code bent to accommodate it, the bend is usually
pointing at a missing seam. Prefer adding the seam.

---

## 2. Working agreements

These were established during review and are worth keeping.

**Seams go in a leaf assembly.** `Common` references only UniTask. `Core` already depends on
`Rewards`, and `Rewards` needs the seams, so seams in `Core` would close a cycle.

**One public type per file, except families.** A reader who knows `PopupCatalog` exists should be
able to find it by filename. Exceptions are genuine families where the filename leads you to the
group: `PopupBase.cs` (base plus generic variant), `RewardReceivedPopup.cs` (popup plus its data),
`ChestsMinigameSO.cs`, `MinigameBase.cs`.

**Typed exceptions, always.** Everything derives from `ChestGameException`. The reason is testing: a
test asserting `Assert.Throws<Exception>` also passes on an unrelated `NullReferenceException` from
inside the call, so it proves nothing.

**Verify claims by mutation.** Before saying a test protects something, break the thing and confirm
the test fails. This caught two toothless tests that would otherwise have shipped (see section 6).

**EditMode first.** PlayMode is for what only a real player loop, a real `Object.Destroy`, or a real
prefab can prove. Everything else belongs in the fast suite. PlayMode tests assert settled states,
never mid-flight ones, so a slow frame on CI cannot cause a false failure.

**No fake where the real thing is constructible.** `PopupCatalog` and `MinigameCatalog` take plain
lists, so tests use the real ones. There were fakes for both; deleting them raised fidelity.

---

## 3. The verification loop

Unity 6000.3.11f1 lives at
`/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity`. Note the path goes
through `Unity.app/Contents/MacOS/`; there is no bare `Unity` binary in the version folder.

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity"

"$UNITY" -batchmode -nographics -projectPath . \
  -runTests -testPlatform EditMode -testResults editmode.xml -logFile editmode.log

"$UNITY" -batchmode -nographics -projectPath . \
  -runTests -testPlatform PlayMode -testResults playmode.xml -logFile playmode.log
```

Things that will waste your time otherwise:

- **The editor must be closed.** A running editor holds `Temp/UnityLockfile` and batchmode aborts
  with "another Unity instance is running". Check with
  `pgrep -f "Unity.app/Contents/MacOS/Unity -projectpath"`.
- **Do not pass `-quit` with `-runTests`.** It cuts the run short.
- **Exit codes:** 0 all passed, 2 tests ran and some failed, 1 aborted (usually compile errors).
  Exit 1 means you should grep the log, not the XML. **This applies to the Unity invocation above,
  not to `ci/run-tests.sh`** — the script does `run_suite ... || overall=1` and exits with that, so
  every failure mode collapses to 1. With the script, tell the two apart by whether the XML was
  written and carries counts: results present means tests ran and failed, results absent means the
  run aborted.
- **A stale results XML lies.** If a run aborts, the previous XML is still on disk and looks like a
  pass. Delete it first or check the exit code before reading it.
- **Filter to one fixture** with `-testFilter "SomeTests"` while iterating.
- Compile errors: `grep -oE "Assets/[^ ]+\([0-9,]+\): error CS[0-9]+: .*" editmode.log | sort -u`

`Assembly-CSharp.csproj` at the repo root is a stale artifact from before the split and is not
regenerated. Do not read it to check what compiles where. Look at `Library/ScriptAssemblies/`
instead, which no longer contains `Assembly-CSharp.dll` at all.

---

## 4. Unity behaviour that surprised us

**`autoReferenced: true` does not mean other asmdefs can see you.** It controls inclusion in the
*predefined* assemblies (`Assembly-CSharp`). Assembly definitions must always reference each other
explicitly. This is why `Unity.TextMeshPro` had to be added by hand to three asmdefs even though its
own asmdef sets `autoReferenced: true`.

**`ICurrencyManager` leaks a third-party type.** Its events are typed
`ResourceBankCallbacks<CurrencyType>.ResourceAmountChangedDelegate`, so every consumer needs a
reference to `TapNation.Modules`. `Company.ChestGame.UI` references a vendored currency library
purely to subscribe to an event. The monolith hid this. It is a real design smell and still open.

**A container-resolved `CurrencyManager` reads the real PlayerPrefs save.** This surfaced as a test
failing with "Expected: 0, But was: 470", where 470 was the developer's actual coin balance. Tests
that build a `CurrencyManager` directly must pass `InMemoryResourceBankSaveHandler`. The one test
that resolves it from the container deliberately asserts nothing about balances.

**`Debug.LogError` fails a test by default.** `CurrencyManager` logs an error on every rejected
operation, so negative-path tests need `LogAssert.Expect(LogType.Error, "exact message")`. Warnings
do not fail tests, which is why `CatalogBuilder` warns rather than errors on an empty slot.

**`Assert.Multiple` does not exist** in the bundled NUnit. Use sequential asserts.

**Components added to an active GameObject run `Awake` immediately.** To wire serialized fields
before `Awake`, deactivate the GameObject, `AddComponent`, set the fields, then reactivate. See
`ChestElementViewLifetimeTests.BuildView`.

**`Object.Destroy` is deferred**, so a play-mode test must `yield return null` before asserting the
object is gone. `DestroyImmediate` is the edit-mode equivalent.

**Throwing from `OnValidate` aborts the surrounding Unity operation**, including asset import. Both
list ScriptableObjects now log instead. That change had a consequence, covered in section 6.

---

## 5. Design decisions and the reasoning behind them

### Why `CatalogBuilder` is a static helper, not a generic base class

`MinigameCatalog` and `PopupCatalog` shared about fifteen lines. Fifteen lines is under most
duplication thresholds, and a `Catalog<TKey, TEntry>` base class would have forced both into a
shared property name, losing `Minigames` and `Popups`. A static builder taking a key selector
avoids that. Each catalog keeps its own interface and property name, and the key selector, the one
thing that actually differs, becomes the visible difference instead of being buried in a loop.

The `TEntry : UnityEngine.Object` constraint is deliberate. It makes `entry == null` use Unity's
overloaded equality, which also catches destroyed objects.

### Why an empty catalog slot warns but a duplicate throws, and a blank id warns too

An empty inspector slot is the most common authoring mistake, and `OnValidate` creates one every
time it clears a duplicate. The rest of the game still works, so making it fatal would mean one bad
inspector row bricks startup. A duplicate is different: there is no correct answer for which entry
wins, and silent last-wins is worse than the old `ArgumentException`.

A blank id sits with the empty slot rather than with the duplicate. An entry whose id was never
authored is still reachable through the type-keyed lookup and the rest of the game still runs, which
is the same argument. It also matters that two unauthored entries would otherwise collide as a
duplicate of `""` and throw over a key nobody wrote. Mutating the skip away shows the second half of
this: a `CreateInstance`d definition leaves `_id` null, so the entry does not merely land under a
useless key, it takes `Dictionary` down with an `ArgumentNullException`.

### Why the prize divisor has a `+ 1`

`TryGiveChestPrize` computes `1 / (float)(Chests.Count - Attempts + 1)`. `Attempts` is already
incremented for the chest being opened, so `k = Attempts - 1` chests are known empty and the divisor
is `N - k`. Dropping the `+ 1` makes the odds reach certainty one chest early: with the shipped
12/12 config the win was guaranteed by the 11th chest and the 12th could never hold the prize.
`WithTheUnluckiestDraws_ThePrizeWaitsInTheFinalChest` and `EveryChest_CanHoldThePrize` guard this.

### Why the config loss path is unreachable in production

`ChestsMinigameConfig.json` ships `ChestCount: 12` and `AttempsCount: 12`. With correct odds that guarantees a win
by the last chest, so "Game Over! Out of attempts!" never displays. This is a config choice, not a
bug, and it is fixable without touching code by setting `AttempsCount` below `ChestCount`. The
losing branch is still tested, at 10 chests and 2 attempts.

### How `FakeGameClock` works

Awaiters park in one of two lists and resume from `AdvanceFrame()`. Continuations run synchronously
inside that call, so once it returns every effect of that frame has already happened and can be
asserted. The list is snapshotted before releasing, because a resumed continuation usually parks a
fresh waiter belonging to the *next* frame.

Test timings are chosen so a chest opens in exactly two frames: a 100 ms open at 50 ms per frame.
Frame one is mid-flight, frame two completes it.

`FrameWaitersResumeFirst` controls which of the two resumes when a frame tick and a delay come due
together. It is a knob rather than a hardcoded assumption so tests can flip it and confirm behaviour
does not depend on the answer.

---

## 6. Bugs found and fixed during this work

Each of these was reachable in the shipped game unless noted.

1. **Prize off-by-one.** The last chest could never hold the prize. Described above.
2. **`ChestsMinigameController.Dispose` cleared one event of three.** `OnGameFinished` and
   `OnAttemptsChanged` stayed subscribed while the view subscribed to all three.
3. **The teardown path was dead code.** `MinigameContainer.End()` had no production caller, so
   `_running` never went false and the first button press built a container the game reused forever.
   `ControllerInstance.Dispose()` never ran in a real session. `GameManager` now ends the active
   minigame in `OnDestroy` and when switching to a different minigame.
4. **`ChestsMinigameChestElementView` never unsubscribed from its model.** Latent only because of
   bug 3: the models belong to the controller and outlive the views, so the first time `End()` was
   wired up, the next `NewGame()` would have driven destroyed MonoBehaviours. Fixed together with
   bug 3, which is the only sensible way to fix either.
5. **Catalog `ToDictionary` crashed on nulls and duplicates.** Made easier to reach by converting
   `OnValidate` from throw to log, which left the null behind instead of aborting the edit.
6. **`SetOpening` had no guard against firing after a chest opened**, unlike `SetOpen`. Belt and
   braces; see the honest caveat in section 7.
7. **Two stray editor-only usings in runtime files** (`UnityEditor.U2D.Aseprite` in `PopupManager`,
    `Unity.Android.Gradle.Manifest` in `PopupBase`). They compiled only because `Assembly-CSharp`
    gets editor references in the editor. They would have broken any player build.

---

## 7. Known gaps, and one honest caveat

**`GameManager` is untested.** It is MonoBehaviour glue needing a wired Button and a scene. `End()`
is reachable and `MinigameContainerLifecycleTests` covers the container lifecycle directly, but
nothing asserts that `GameManager` calls it. Two more of its behaviours are asserted nowhere: the
start button going non-interactable while a start is in flight, and a `ChestGameException` becoming
a `ContentUnavailablePopup`. The pattern in `ChestElementViewLifetimeTests` (deactivate, add
component, reflect fields in, reactivate) would close all of this, and `FakePopupManager` already
records what would have been spawned.

**`CurrencyWatcher` is untested.** Cosmetic UI binding, judged low value.

**The play-mode ordering test is dormant.** `OnTheRealPlayerLoop_NoProgressTickLandsAfterAChestOpens`
passes with or without the `SetOpening` guard, because the real player loop currently orders the two
tasks favourably. It is a canary for a future ordering change, not an active check.

**`FakeGameClock` cannot reproduce the ordering bug it was meant to guard.** In the fake, `passedTime`
and the delay share one clock, so `passedTime < totalTime` flips false on exactly the frame the delay
comes due, and the progress loop always exits before it could tick again. That holds under both
orderings. The real risk is drift between `UniTask.Yield` accumulation and `UniTask.Delay`, which are
separate accumulators in the engine but one in the fake. What actually protects the invariant is the
`SetOpening` guard, covered by `SetOpening_AfterTheChestIsOpen_IsIgnored` (verified by mutation).
`OpeningIsUnaffectedByScheduling` is kept because scheduling independence is worth asserting, but it
does not guard this.

**`ICurrencyManager` leaking `ResourceBankCallbacks`** is unresolved. Wrapping the delegate in a
project-owned type would let `UI` drop its `TapNation.Modules` reference.

---

## 8. Test suite map

Fakes live in `Tests/Common/` and are shared by both suites.

| EditMode fixture | What it protects |
|---|---|
| `ChestsMinigameControllerTests` | The whole chest flow through `OnChestClicked`, including both UniTasks, cancellation, attempt accounting, prize odds, end-of-game |
| `ChestsMinigameChestModelTests` | Per-chest state machine and its guards |
| `CurrencyManagerTests` | Add/spend guards, the spent-vs-changed sign asymmetry, persistence through the save handler |
| `LocalJsonGameConfigTests` | Missing, empty, malformed, non-object, and out-of-range game config documents |
| `CatalogTests` | Empty slots and duplicate types in both catalogs, and for minigames the id lookup: indexing by authored id, a duplicate id throwing, a blank id skipped with a warning |
| `MinigameManagerTests` | Container construction by type and by id, fresh instance per request, all three throw paths, and that `Get` neither configures nor injects the controller — both of which belong to `BeginAsync` now |
| `PopupManagerTests` | Catalog lookup, parent selection, data hand-off, unregistered popup |
| `RewardsManagerTests` | Currency draw, amount from config, popup and event agreement |
| `PrefabPoolTests` | All four pool strategies against one shared contract: reuse, bounds, disposal, and that only `DirectSpawner` instantiates on a second get |
| `FrameBudgetedLoopTests` | That work is spread by elapsed time rather than by item count, and that every frame places at least one unit |
| `PoolRaceTests` | The race orchestration against a fake clock: per-lane timing, solo vs all-four, the three fill modes |
| `GameLifetimeScopeTests` | The real `RegisterCoreServices` and `RegisterLoadedServices`: every service registered, the risky ones actually resolvable, the preloader resolvable across both halves of the split, and that boot always has an `IBootStatus` to report through — the scene's when one was handed in, a silent one when it was not |

| PlayMode fixture | What only play mode can prove |
|---|---|
| `ChestsMinigameIntegrationTests` | `UnityGameClock` drives the same flow the fake does |
| `MinigameContainerLifecycleTests` | `BeginAsync`/`End` against real `Object.Destroy` semantics, and that the container is what injects the controller |
| `ChestElementViewLifetimeTests` | A destroyed view really stops listening to its model |
| `PopupManagerIntegrationTests` | The shipped popup keys resolve through the real Addressables catalog and a real prefab instantiates |
| `ChestBoardPoolingTests` | That a rebuilt board really reuses instances, counted by real `Awake` calls rather than by pool bookkeeping |
| `PoolRacePlayModeTests` | The lanes as real Unity objects: real holders, real `GridLayoutGroup` fills, real `Object.Destroy` |
| `PoolingDemoPanelPlayModeTests` | The authored UI - that the real prefab, `.uxml` and `.uss` still bind to the panel, and that a tap lands on the control it looks like it lands on |
| `PoolBenchmark` | Logs the strategy timings the pooling numbers in `design-decisions.md` come from; asserts only counts, never a duration |

---

## 9. If you change something structural

- Adding a service: which half of `GameLifetimeScope` it goes in follows from whether it needs
  content that has been loaded. If it can be built the moment the container is, register it in
  `RegisterCoreServices` and add it to `RegisterCoreServices_RegistersEveryServiceTheGameResolves`;
  if it needs a loaded asset, directly or through something that does, it belongs in
  `RegisterLoadedServices` and in `RegisterLoadedServices_RegistersEveryContentBackedService`. Only
  the things built straight out of a `LoadedContent` field are `RegisterInstance` calls — anything
  that merely depends on one is an ordinary singleton registration in the same half. If its
  constructor reaches outside itself, add it to the matching
  `EveryCoreServiceTheGameResolves_HasASatisfiableObjectGraph` or
  `EveryLoadedServiceTheGameResolves_HasASatisfiableObjectGraph`.
- Adding an assembly: production asmdefs use `autoReferenced: false` and `overrideReferences: true`,
  so list every dependency explicitly, including precompiled ones such as `Newtonsoft.Json.dll`.
  Trim the assembly it came out of at the same time; a reference left behind still compiles and
  quietly keeps the old coupling.
- Adding a failure mode: give it a type under `ChestGameException` and assert the type in the test,
  not `Exception`.
- Moving a script that a scene or prefab references: move the `.meta` with it, or the GUID changes
  and the reference breaks. The catalog and exception splits were safe because those are plain C#
  classes that no asset references.
