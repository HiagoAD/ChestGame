# Engineering notes

Working context for anyone (human or AI session) picking this repo up mid-stream.

The README describes what the architecture is. This file covers why it landed that way, what was
tried and rejected, how to verify changes, and which traps cost time the first time round. Read the
README first for the map; read this before changing anything structural.

Last updated after `Minigame.Chests` started loading from a remote path, `IAssetProvider` grew the
two routes that make that survivable — download size and download — and the two load policies the
descriptor had been carrying since the phase before started being read: `MinigameContentPreloader`
at boot for `Preload`, `MinigameContainer.BeginAsync` for `OnDemand`. At that point the suites were
165 EditMode tests (~0.4 s) and 32 PlayMode tests (~20 s). The EditMode runner reports 166: the
Addressables package ships one editor test of its own and Unity picks it up.

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
| `ContainerWiringTests` that rebuilt the registration list by hand | It duplicated `Configure` and never loaded `GameLifetimeScope`, so deleting a registration passed. `RegisterServices` was extracted and the tests now run against it. |
| Two `CurrencyManager` constructors, `[Inject]` on the parameterless one | It created an extension point and sealed it in the same commit. The save handler is now registered in the scope and there is one constructor. |
| `PopupManager` loading Resources in its constructor | Left it play-mode only and leaking canvases between tests. `IPopupCatalog` plus a lazy `IPopupParentProvider` moved most popup tests to edit mode. |
| Catalogs building their dictionary with `ToDictionary` | Threw `NullReferenceException` on an empty inspector slot and `ArgumentException` on a duplicate. `CatalogBuilder` now skips nulls with a warning and throws `InvalidCatalogException` on duplicates. |
| One `Data.json` and one `IGameConfig` carrying every feature's fields | A god-object: three of its five fields meant nothing outside the chests minigame, and it made that minigame permanently dependent on a shared document. Split into `GameConfig.json` (rewards) and a chests-owned `ChestsMinigameConfig.json`, with a `ConfigureController` hook as the only framework surface it needed. |
| The chests minigame living in the shell's assembly, started by `StartMinigame<ChestsMinigame>()` | "Self-contained" was held up by convention, and the shell named the minigame at compile time, so nothing could answer "which assets does this minigame own". The minigame moved to `Company.ChestGame.Minigame.Chests` with its assets beside it, and the shell now asks for an authored id. |
| Four `Resources*Source` classes, each calling `Resources.Load` | Every one of them knew both a path *and* a loading technology, so swapping the technology meant touching all four and their tests. `IAssetProvider` took the technology, the sources kept the key, and `Company.ChestGame.Assets` became the only assembly that references Addressables. |
| `AddressablesAssetProvider` keying its handle dictionary on the `AssetReference` instance | `AssetReference` overrides neither `Equals` nor `GetHashCode`, so the lookup had reference identity. It worked only because every production caller hands back the very same serialized field it loaded with. The bookkeeping moved to `AssetHandleRegistry`, keyed on the runtime key, which is also what made it testable without a content catalog behind it. |
| `ChestsMinigame.asset` holding its view prefab and config document as direct references | The `Minigame.Chests` group bought nothing: loading the descriptor, which the catalog does at boot for every minigame, dragged the whole chests bundle in as a hard dependency. The fields became `AssetReference`s, which serialize as GUID strings rather than as dependencies. That forced the loading out of the synchronous `GetMinigameContainer` and into `MinigameContainer.BeginAsync`, taking the configure-before-inject contract with it. |

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

**One assembly calls the loading technology.** Only `Company.ChestGame.Assets` calls Addressables.
Every other assembly reaches assets through `IAssetProvider`, and the package's exception types are
translated at that boundary. A `using UnityEngine.AddressableAssets` outside that assembly, for
anything other than the serializable reference types, is a regression.

This is deliberately weaker than the invariant it replaced, which was that no other assembly
*referenced* the package at all. Two things ended that, and neither is avoidable:

- A serialized `AssetReference` field is only authorable in an assembly that can see the type, so
  `Company.ChestGame.Minigame` and `Company.ChestGame.Minigame.Chests` reference the package for it.
- Once `IAssetProvider` carries an overload naming `AssetReference`, *every* call site has to be
  able to resolve that overload, whether or not it uses it. `Company.ChestGame.Config` and
  `Company.ChestGame.Popups` call only the key route and still fail to compile without the
  reference (`CS0012: the type 'AssetReference' is defined in an assembly that is not referenced`).

The reference is the mechanism — it is what stops a descriptor from depending on its own bundle — so
the narrower invariant was the thing that had to give. Splitting the seam into two interfaces would
have bought the second bullet back, at the cost of a caller having to know which one it wants.

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

The content build has its own script, `ci/build-addressables.sh`, which runs the permanent
`Company.ChestGame.Editor.AddressablesContentBuild.BuildFromCommandLine` over
`AddressableAssetSettings.BuildPlayerContent()`. It is not part of the test loop on purpose: both
suites run against the asset database, so nothing here needs a bundle. Run it when the delivery
paths themselves are what changed, and read what it wrote under `ServerData/[BuildTarget]/` — that
is the only check that a group actually bundles as one unit rather than merely being configured to.

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

**UniTask's `AsyncOperation` awaiter is compiled out on Unity 2023.1+.** `UnityAsyncExtensions`
guards `GetAwaiter(this AsyncOperation)` with `#if !UNITY_2023_1_OR_NEWER`, so on this editor
`await SceneManager.LoadSceneAsync(...)` binds to the generic `IEnumerator` overload instead and
fails with CS0311. Use `.ToUniTask()`, which is not version-gated. The same reflex applies to
`AsyncOperationHandle`: `AddressablesAssetProvider` calls `handle.ToUniTask(...)` explicitly.

**UniTask's Addressables support is off until the package is installed.**
`UniTask.Addressables.asmdef` carries a version define that only defines
`UNITASK_ADDRESSABLE_SUPPORT` when `com.unity.addressables` is present, and the whole of
`AddressablesAsyncExtensions.cs` is inside that `#if`. Before the package is in `manifest.json`,
`ToUniTask` on a handle does not exist and the error says nothing about why.

**Addressables loads never complete synchronously.** `Resources.Load` returned before the call did,
which is why `SynchronousUniTask` worked in play mode. Addressables does not: the first call runs
`InitializeAsync` and every load takes at least a frame. That is what turned
`PopupManagerIntegrationTests` into `UnityTest`s. `SynchronousUniTask` is now an edit-mode-only
helper, and it still refuses a pending task loudly rather than handing back a default.

**An asset in a `Resources` folder *and* an addressable group ships twice.** The move to
`_Project/Content` is not cosmetic. `Assets/TextMesh Pro/Resources` and
`Assets/AssetLibrary/**/Resources` are not ours and are left alone.

**Addressables settings cannot be authored by editing a file.** They need editor APIs
(`AddressableAssetSettingsDefaultObject.GetSettings(true)`, `CreateGroup`, `CreateOrMoveEntry`,
`entry.address`). The way that was done here is a throwaway script under `Assets/<Phase>Setup/Editor/`
with its own asmdef, run with `-batchmode -executeMethod` (no `-quit`), exiting 0 or 1 itself, then
deleted. `ci/run-tests.sh` runs both suites with no content build, so the committed settings must
leave the play mode script on **Use Asset Database (fastest)**.

**Addressables logs an error before it throws on an unknown key.** `LoadAssetAsync` with a key that
is not in the catalog calls `Debug.LogError` ("No Location found for Key=...") on its way to throwing
`InvalidKeyException`, and an unexpected error log fails a test by itself. A test covering the
missing-key path needs `LogAssert.Expect(LogType.Error, new Regex("No Location found for Key=..."))`
or it fails for the logging rather than for the behaviour under test.

**An `AssetReference` serializes as a nested string, not as an object reference.** In YAML it is
`m_AssetGUID` plus three empty strings and a bool, not `{fileID, guid, type}`. That difference is the
entire mechanism: a GUID string is not a dependency, so the descriptor holding one does not pull the
asset's bundle in. The fields were rewired by hand and then re-saved through the editor to confirm
Unity writes the same thing; it did, byte for byte.

**`AssetReference.LoadAssetAsync` stores its handle on the reference itself**, which is a field on a
shared definition asset. A second load logs an error and loses the first handle, and `ReleaseAsset`
warns when there is nothing to release. `AddressablesAssetProvider` therefore passes the reference to
`Addressables.LoadAssetAsync` as a key and keeps the handles itself, which is also what lets `Release`
be safe on a reference that was never loaded.

**`Object.Destroy` is a logged error in edit mode**, so an edit-mode test that reaches
`MinigameContainer.End` after a view was instantiated fails on the log rather than on the behaviour.
`MinigameContainerContentTests` destroys the instance itself first; End against a live view belongs
to play mode.

**`AssetReference` has no value semantics.** It overrides neither `Equals` nor `GetHashCode`
(verified in the package source), so a `Dictionary<AssetReference, _>` keys on the instance. Two
references carrying the same GUID are two different keys. `AssetHandleRegistry` keys on
`RuntimeKey.ToString()` instead, which is the GUID plus `[SubObjectName]` when there is one — so a
sprite out of an atlas is still distinct from the atlas. `AssetHandleRegistryTests` asserts the
package's behaviour as a premise, so the day the type gains an `Equals` the indirection is flagged
rather than left behind.

**`GetDownloadSizeAsync` and `DownloadDependenciesAsync` fail through the handle, not the call.**
Both return `ResourceManager.CreateCompletedOperationWithException<T>(..., new InvalidKeyException(...))`
when the key resolves to no locations, so nothing throws until the handle is awaited — which means
the `try` has to wrap the `await`, not just the call, exactly as the load routes already did.

**A failed Addressables operation logs an error before anyone sees it.**
`AddressablesImpl.LogException` is installed as `ResourceManager.ExceptionHandler` during
initialization and does `Debug.LogError(ex.ToString())` for any operation that ends `Failed`. That
is the same trap as the "No Location found for Key=" one, generalised: *every* negative test against
the real provider needs `LogAssert.Expect`, whichever route it exercises.

**A label with nothing to fetch is not an error, but an unknown label is.** With the play mode
script on Use Asset Database, `AddressableAssetSettingsLocator` indexes labels as keys, so
`minigame.chests` resolves — to four locations with no dependencies, which is a download size of 0
and a download that completes immediately. That is what lets the on-demand path run unchanged in
both test suites and in a build that shipped its content local. A label nobody authored still
arrives as `MissingAssetException`, the same as an unknown key.

**A group's remote path is not what the editor loads from.** `Minigame.Chests` points at
`http://localhost:8080/[BuildTarget]`, and both suites still pass with nothing serving it, because
Fast Mode never consults the group's load path at all. The remote paths are only exercised by a
packed build — which is why `ci/build-addressables.sh` exists and why running it once is the only
structural check that the group actually bundles as a unit.

**The play mode script lives in `Library`, not in the settings asset.**
`AddressableAssetSettings.ActivePlayModeDataBuilderIndex` delegates to `ProjectConfigData`, which
`BinaryFormatter`s itself into `Library/AddressablesConfig.dat`. It is therefore *not* committed:
a fresh checkout gets the default, which is index 0, which is `BuildScriptFastMode` — the one the
test suites need. `m_ActivePlayerDataBuilderIndex: 2` in the settings asset is the *player* build
script (packed), and is a different setting entirely.

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
   minigame in `OnDestroy` and when switching to a different minigame type.
4. **`ChestsMinigameChestElementView` never unsubscribed from its model.** Latent only because of
   bug 3: the models belong to the controller and outlive the views, so the first time `End()` was
   wired up, the next `NewGame()` would have driven destroyed MonoBehaviours. Fixed together with
   bug 3, which is the only sensible way to fix either.
5. **Catalog `ToDictionary` crashed on nulls and duplicates.** Made easier to reach by converting
   `OnValidate` from throw to log, which left the null behind instead of aborting the edit.
6. **`SetOpening` had no guard against firing after a chest opened**, unlike `SetOpen`. Belt and
   braces; see the honest caveat in section 7.
7. **An unwired config document threw an engine exception, not a typed one.** `ChestsMinigameSO`
   read `_configDocument.text` before `ChestsMinigameConfig.Parse` could reject it, so the most
   common authoring mistake surfaced as `UnassignedReferenceException` in the editor and a plain
   `NullReferenceException` in a build. It now throws `GameConfigException` naming the asset.
8. **A failed or cancelled `BeginAsync` leaked whatever it had already loaded.** `End` returns early
   on `!_running`, and `_running` is set on the last line of `BeginAsync`, so a container whose view
   loaded and whose config load then threw held a handle nothing could ever release. `BeginAsync`
   now releases what it took and rethrows. `End` was deliberately *not* made unconditional, because
   "releases nothing when it never began" is a contract the teardown paths depend on.
9. **The provider's handle dictionary keyed on reference identity.** Described in section 1. Not
   reachable from the shipped game, which is the reason it survived review once: every caller
   happens to pass the same serialized instance to load and to release. Any caller that built a
   reference from a GUID would have got a silent no-op release and a permanent leak.
10. **Two stray editor-only usings in runtime files** (`UnityEditor.U2D.Aseprite` in `PopupManager`,
    `Unity.Android.Gradle.Manifest` in `PopupBase`). They compiled only because `Assembly-CSharp`
    gets editor references in the editor. They would have broken any player build.

---

## 7. Known gaps, and one honest caveat

**`GameManager` is untested.** It is MonoBehaviour glue needing a wired Button and a scene. `End()`
is reachable and `MinigameContainerLifecycleTests` covers the container lifecycle directly, but
nothing asserts that `GameManager` calls it. The pattern in `ChestElementViewLifetimeTests`
(deactivate, add component, reflect fields in, reactivate) would close this.

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

**The configure-before-inject assertion is no longer a canary.** It used to be unfalsifiable: the
hook ran inside `GetMinigameContainer` and `MinigameManager.Get` injected afterwards, so "configured
after injection" was not reachable without moving the hook. The move happened — both now sit either
side of one `await` in `MinigameContainer.BeginAsync` — and the assertion, which travelled to
`MinigameContainerContentTests.BeginAsync_ConfiguresTheControllerBeforeInjectingIt`, fails under
mutation. It was kept for exactly the refactor that arrived.

**A view prefab with no `MinigameViewBase` on it is an untyped failure.** `BeginAsync` does
`prefab.GetComponent<MinigameViewBase>()` and hands the result straight to the resolver, so an
`AssetReference` pointing at the wrong prefab surfaces as a `NullReferenceException` rather than as
something under `ChestGameException`. A wrong or absent GUID is covered — that is a
`MissingAssetException` — but a right GUID on the wrong asset is not.

**A start that is cancelled mid-load no longer leaks**, but the fix lives in the wrong-looking
place. `GameManager` still only records the container once `BeginAsync` returns; what closed the gap
is `BeginAsync` releasing its own partial work on the way out. That is the only place that can, and
it is worth knowing before anyone tries to "simplify" the `catch` away.

**The value-semantics half of the handle fix is unprovable through the seam.**
`AssetHandleRegistryTests` covers it directly, and a play-mode test that loads through one
`AssetReference` and releases through another built from the same GUID was written and then deleted:
under both the fixed and the broken code the observable behaviour through `IAssetProvider` is
identical — a released handle and a leaked one look the same from outside, and `ResourceManager`'s
only counter (`OperationCacheCount`) is `internal`. Extracting the registry was what made the fix
assertable at all.

**`GameManager` is still untested**, and it grew two behaviours this phase: the start button going
non-interactable while a start is in flight, and a `ChestGameException` becoming a
`ContentUnavailablePopup`. Both are asserted nowhere. The pattern in `ChestElementViewLifetimeTests`
would close it, and `FakePopupManager` already records what would have been spawned.

**Nothing asserts the boot status label actually updates.** `IBootStatus` is registered and resolved
under test, and `BootStatusLabel` is three lines, but no test drives boot and reads the label back.

---

## 8. Test suite map

Fakes live in `Tests/Common/` and are shared by both suites.

| EditMode fixture | What it protects |
|---|---|
| `ChestsMinigameControllerTests` | The whole chest flow through `OnChestClicked`, including both UniTasks, cancellation, attempt accounting, prize odds, end-of-game |
| `ChestsMinigameChestModelTests` | Per-chest state machine and its guards |
| `CurrencyManagerTests` | Add/spend guards, the spent-vs-changed sign asymmetry, persistence through the save handler |
| `LocalJsonGameConfigTests` | Missing, empty, malformed, non-object, and out-of-range game config documents |
| `ChestsMinigameConfigTests` | The same failure surface for the chests minigame's own document, plus its three range rules and a definition with no document wired |
| `CatalogTests` | Empty slots and duplicate types in both catalogs, and for minigames the id lookup: indexing by authored id, a duplicate id throwing, a blank id skipped with a warning |
| `MinigameManagerTests` | Container construction by type and by id, fresh instance per request, all three throw paths, and that `Get` neither configures nor injects the controller — both of which belong to `BeginAsync` now |
| `MinigameContainerContentTests` | What `BeginAsync` loads and in what order, that the controller is configured before it is injected and injected once, that `End` releases both the view and the minigame's own content, that `End` on a container that never began releases nothing, that a failed load surfaces typed and releases what it had already taken, and the whole on-demand fetch: measured then downloaded for `OnDemand`, skipped for `Preload`, skipped for a blank label, skipped when the size is zero, typed and not running when the download fails |
| `MinigameContentPreloaderTests` | The boot-time half of the load policies: only `Preload` entries measured and fetched, every size gathered before the first byte, aggregate progress that never goes backwards, a blank label warned and skipped, nothing fetched when there is nothing to fetch, the typed failure propagating, and the token threaded through |
| `AssetHandleRegistryTests` | That the provider's bookkeeping has value semantics: a reference rebuilt from the same GUID takes what the authored one left, a different asset takes nothing, a sub-object is not its parent, every handle for one asset is kept, taking untracks, and taking something never loaded is safe. Also asserts, as a premise, that `AssetReference` still has no `Equals` of its own |
| `PopupManagerTests` | Catalog lookup, parent selection, data hand-off, unregistered popup |
| `RewardsManagerTests` | Currency draw, amount from config, popup and event agreement |
| `GameContentLoaderTests` | Every source read once, in order, cancellation threaded through, and a failure stopping the load rather than yielding half-built content |
| `AddressablesContentSourceTests` | The four content sources against `FakeAssetProvider`: each asks for its own key, carries the provider's answer through untouched, surfaces a missing asset as `MissingAssetException`, and threads its cancellation token |
| `GameLifetimeScopeTests` | The real `RegisterCoreServices` and `RegisterLoadedServices`: every service registered, the risky ones actually resolvable, the preloader resolvable across both halves of the split, and that boot always has an `IBootStatus` to report through — the scene's when one was handed in, a silent one when it was not |

| PlayMode fixture | What only play mode can prove |
|---|---|
| `ChestsMinigameIntegrationTests` | `UnityGameClock` drives the same flow the fake does |
| `MinigameContainerLifecycleTests` | `BeginAsync`/`End` against real `Object.Destroy` semantics, and that the container is what injects the controller |
| `ChestElementViewLifetimeTests` | A destroyed view really stops listening to its model |
| `GameBootstrapperTests` | The boot flow end to end: the game scene opens parented to the loaded scope, the root scope survives it, every shipped key resolves through Addressables, both halves of the split inject the scene's objects, and the chests minigame really begins from the two references its descriptor names — which is the integration proof for the indirection itself |
| `AddressablesAssetProviderTests` | That the real provider translates what the real library throws, on all four routes: an unknown key, an unresolvable reference or an unauthored label arrives as `MissingAssetException`, a shipped key and a shipped reference both still load, a label with nothing left to fetch reports 0 and downloads without failing, and releasing a reference that was never loaded is safe. The sources are covered in edit mode against a fake, which by construction cannot prove this |
| `PopupManagerIntegrationTests` | The shipped popup keys resolve through the real Addressables catalog and a real prefab instantiates |

---

## 9. If you change something structural

- Adding a service: register it in `GameLifetimeScope.RegisterServices` and add it to
  `RegisterServices_RegistersEveryServiceTheGameResolves`. If its constructor reaches outside itself,
  add it to `EveryServiceTheGameResolves_HasASatisfiableObjectGraph` too.
- Loading a new asset: give it an address in the `Core` group, put the key on exactly one source
  class, and reach it through `IAssetProvider`. Do not name an Addressables API in the assembly you
  are working in; that is what the seam is for. The one thing that legitimately crosses the boundary
  is a serialized `AssetReference` field, and only on an asset that authors content of its own.
- Loading content that belongs to one minigame: put it in that minigame's group, label it, and name
  it from the definition asset with an `AssetReference`. Load it in `ConfigureControllerAsync` and
  release it in `ReleaseContent`, which are the two halves the container calls from `BeginAsync` and
  `End`. A direct object reference on the definition undoes the grouping silently — nothing fails,
  the bundle just comes back as a hard dependency of the catalog.
- Adding an assembly: production asmdefs use `autoReferenced: false` and `overrideReferences: true`,
  so list every dependency explicitly, including precompiled ones such as `Newtonsoft.Json.dll`.
  Trim the assembly it came out of at the same time; a reference left behind still compiles and
  quietly keeps the old coupling.
- Adding a minigame: six things, and only the first is code.
  1. Its own assembly and its own folder under `_Project/Minigames/`, with no reference from
     `Company.ChestGame.Gameplay`. The shell reaching a minigame by type again is the thing this
     layout exists to prevent. The assembly is always in the player — managed code cannot ship in a
     bundle — so this is the part that a content update can never change.
  2. Its own config document, if it has values of its own, parsed by itself. Do not add fields to
     `GameConfig.json`.
  3. A definition asset with an authored `_id`, and `AssetReference`s naming its view and its
     document rather than holding them.
  4. An addressable group of its own, `Minigame.<Name>`, with a build and load path pair chosen the
     way `Minigame.Chests` chose remote and `Core` chose local.
  5. A label, carried by every entry in that group, authored into `_contentLabel` on the descriptor.
     The label is the unit both the download size and the progress figure work in, so a group whose
     entries carry different labels cannot be measured.
  6. A `_loadPolicy`. `Preload` means the boot screen waits for it; `OnDemand` means the first press
     of its button does. Neither needs a line of code — `MinigameContentPreloader` and
     `MinigameContainer.BeginAsync` already read both.

  Entry 5 is the one with a silent failure mode: a blank label is warned about and skipped, so a
  minigame set to preload with no label simply never preloads and nothing goes red.
- Adding a failure mode: give it a type under `ChestGameException` and assert the type in the test,
  not `Exception`.
- Moving a script that a scene or prefab references: move the `.meta` with it, or the GUID changes
  and the reference breaks. The catalog and exception splits were safe because those are plain C#
  classes that no asset references.
