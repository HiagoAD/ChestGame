# Testing

Two suites, split by what only a real engine can prove.

| Suite | Ours | Wall time |
|---|---|---|
| EditMode | 708 | ~2 s |
| PlayMode | 74 | ~25 s |

Reproduce them with `ci/run-tests.sh`; the wall times move a little run to run. The numbers are
written here rather than linked because `ci-results/` is gitignored, so a fresh clone has none until
it runs the suites itself. The EditMode runner reports 709: the
Addressables package ships one editor test of its own
(`AddressableAssets.DocExampleCode.TestStub.RequiredTest`) and Unity picks it up. It is not ours and
is not counted above.

## What lives where

EditMode covers the logic exhaustively against fakes. PlayMode is a thin layer of integration smoke
tests for what only a real player loop, a real prefab or a real content catalog can prove: that
`UnityGameClock` drives the same chest flow the fake clock does, that a view really unsubscribes when
destroyed, that a popup really lands under its parent, and that the keys and authored references the
game ships really do resolve through Addressables.

The time difference is the point. The edit-mode suite runs the entire chest-opening flow, including
cancellation, without waiting for anything, because `FakeGameClock` decides when a frame happens. Two
classes account for almost all of play mode's time — `ChestsMinigameIntegrationTests` at ~14 s waiting
on real timers and `GameBootstrapperTests` at ~8 s loading real scenes — which is why so little else
lives there. Everything the save system added to play mode costs about 2 s in total, because the parts
that needed a real engine are the thread hop, a real `UIDocument` and a benchmark, not waiting.

`ChestsMinigameControllerTests` picks its timings so a chest takes exactly two frames to open: a
100ms configured open time against `FakeGameClock`'s 50ms delta per frame. That keeps every
assertion about a chest still mid-flight or freshly settled anchored to a frame count rather than
a duration.

`ChestBoardPoolingTests` runs in play mode because `Object.Destroy` is deferred to the end of the
frame, so "how many of these exist" has a wrong answer until it has landed; every count in the
fixture is read only after a fill and its destroys have settled. It also needs a real `ISaveService`
to inject into `ChestsMinigameController`, and its own PlayMode assembly cannot reference the
EditMode-only `FakeSaveStore`, so it wires a real `SaveService` over `InMemoryStore` instead.

`ChestsMinigameIntegrationTests.SetUp` wires a real `SaveService` over `InMemoryStore` into
`ChestsMinigameController` too, for the same reason as `ChestBoardPoolingTests`: this fixture's own
PlayMode assembly cannot reach the EditMode-only `FakeSaveStore`. `SettleTime` waits ten times the
configured open duration rather than the duration itself, enough slack to absorb a domain reload or a
cold CI runner while staying bounded.

`FakeAssetProvider` keeps the fast suite off Addressables entirely, the same way `FakeGameClock` keeps
it off the player loop. The four content sources are asked what key they want and what they do with
the answer, with no catalog and no bundle behind them.

Play-mode tests assert settled states rather than mid-flight ones, so a slow frame on a cold CI runner
cannot cause a spurious failure.

`PopupManagerIntegrationTests` uses `[UnityTest]` throughout because loading through Addressables
really does wait, unlike the edit-mode suite's fake clock. Its `[TearDown]` destroys every
`PopupParent` it finds because `PopupParentProvider` parents popups under a `DontDestroyOnLoad`
canvas it builds on first use, which would otherwise survive from one test into the next.

Two fixtures test authored assets rather than code: the pooling demo's panel and the save inspector's.
A third pair, `DemoOverlayTests` and `DemoOverlaysPlayModeTests`, covers the two demos sharing the Game
scene, since placing a prefab in a scene and choosing its sort order are asset edits no compiler sees.
Both UIs are a prefab, a `.uxml` and a `.uss`, and every way that breaks compiles perfectly: a renamed element, a
class the stylesheet no longer defines, a serialized field left empty. So those tests instantiate the
real prefab and ask the panel questions no compiler can - does a tap actually land on this button
(`panel.Pick`, not a display flag), is this control inside the box that paints the backdrop, do the
prefab's own `CanvasScaler` and `PanelSettings` still agree about what a pixel is. The prefab is
loaded through `AssetDatabase`, which is honest about these running only in an editor; the
alternatives all mean shipping the demo somewhere the game itself does not need it.

That fixture exists because a stylesheet failure is silent. A selector USS cannot parse - `:nth-child`
is one - discards the whole file with nothing in the console, and the panel renders with stock theme
controls that are the right shape to pass any test asking only whether an element was found.

`DemoOverlayTests` and `DemoOverlaysPlayModeTests` assert two inequalities about the pooling demo's and
save inspector's floating-toggle sort orders. The save inspector's toggle sorts below the pooling demo,
so an open pooling demo covers it rather than floating over the pooling demo's own panel; the save
inspector's chrome sorts above the pooling demo, so an open save inspector covers the pooling demo's
toggle instead of the reverse. Either inequality failing leaves one overlay's toggle floating on top of
the other's open panel.

`GameShellTeardownTests` boots the real game, starts the chests minigame through the real start button
and then unloads the game scene, because only a real scene unload proves that leaving the game scene
with the chests minigame running disposes the shell, which ends the minigame and unregisters its
`SaveScheduler` from the root `ISaveFlushRegistry`.

### The save suites never touch a real save

Every fixture that exercises a file-backed store writes into a per-fixture temp directory named with a
GUID and deletes it in teardown, and every fixture that touches `PlayerPrefs` uses a GUID-suffixed key
and calls `PlayerPrefs.Save()` after deleting it — a `DeleteKey` without that does not persist in batch
mode, which is how a leaked key was first noticed. `Application.persistentDataPath` and the real
`ResourceBankSaveData_CurrencyType` entry are off limits to both suites.

`PlayerPrefsStoreTests` records each `(prefix, key)` pair it intends to touch before making the
call that might throw, rather than only after a successful write, so `TearDown` still cleans up
when the test itself fails partway through. `PlayerPrefs.DeleteKey` on a key that was never
actually set is a no-op, so recording intent this way costs nothing on the keys a failed write
never reached.

This is a rule with a scar behind it. `GameBootstrapperTests` and `GameShellTeardownTests` boot the
real `Boot` scene, which runs the real `GameLifetimeScope.Configure()` — and `Configure` is Unity's
own callback, so no test can pass it an argument. Before the static overrides existed, that boot
resolved the currency manager against a developer's actual save and deleted it. The seam that closed
it is `GameLifetimeScope.CurrencySaveInputsOverride` and `LegacyCurrencyPlayerPrefsKeyOverride`,
assigned on the line before `LoadSceneAsync` so the ordering rests on statement order rather than on
how the framework sequences its setup attributes.

`RealGameBootFixture.RestoreTheCurrencySaveOverrides` clears both overrides unconditionally, in
`[TearDown]` rather than only on success, because a failing test that left them set would otherwise
leak its override into whichever fixture boots a scene next. The statement-order guarantee itself
replaced an earlier attempt that relied on the framework's setup-attribute ordering instead; that
assumption turned out not to hold, which is why the overrides are assigned on the line immediately
before `LoadSceneAsync` rather than from an earlier setup method, and the save root is deleted last,
at the end of `[UnityTearDown]`, rather than from `[TearDown]`. The directory that leaked before that
delete moved to the end of `[UnityTearDown]` held a `meta.sav` rather than a `currency.sav`, because
`GameMetaSaveDocument` is written on every boot while the currency document only writes when a
balance actually changes.

Both real-game fixtures derive from `RealGameBootFixture`, which owns the whole setup and teardown
so the statement order lives in one place.

Two consequences worth keeping. A teardown that deletes a save directory has to run *after* the
container is disposed, because disposing a `SaveScheduler<T>` triggers its best-effort flush and
recreates the directory — that leaked one directory per test until the delete moved to the end of
`[UnityTearDown]`. And an aborted batch-mode run leaves the previous results XML in place, where it
reads as a pass; delete it before every run and check the timestamp on the file you report from.

Fakes live in `Tests/Common/` and are shared by both suites. There is deliberately no fake catalog:
`PopupCatalog` and `MinigameCatalog` take plain lists, so the tests use the real ones.

`GameLifetimeScopeTests` runs against `GameLifetimeScope.RegisterCoreServices` and
`RegisterLoadedServices` themselves rather than a copy of them, so dropping a registration from the
composition root fails there.

### The save fixtures

The two context files each map the fixtures their own work added, so neither covers the save suite:
[context/assemblies-and-tests.md](context/assemblies-and-tests.md) section 8 has the original
testability work, [context/self-contained-minigames.md](context/self-contained-minigames.md) section 8
the content-delivery work, and the save fixtures are mapped here.

Thirty-five EditMode fixtures and four PlayMode ones, by what they protect:

| Area | Fixtures |
|---|---|
| Stores | `FileStoreTests`, `AtomicFileStoreTests`, `PlayerPrefsStoreTests`, `InMemoryStoreTests`, `SaveStoreContractTests` (covers the two file-backed stores), `ThreadHoppingStoreTests`, `SaveStoreCompletesOnCallingThreadTests` |
| Codecs | `GzipJsonCodecTests`, `PrettyJsonCodecTests`, `SaveCodecToJsonTests`, `SaveCodecEnvelopeValueExactnessTests` |
| Protectors | `AesProtectorTests`, `HmacSignedProtectorTests`, `XorObfuscatorTests`, `Base64ObfuscatorTests` |
| Envelope and corpus | `SaveEnvelopeTests`, `SaveGoldenCorpusTests` (bytes an older build really wrote) |
| The service | `SaveServiceTests`, `SaveServiceMigrationTests`, `SaveServiceLegacyImportTests`, `SaveServiceTamperDetectionTests`, `SaveServiceCompletesOnCallingThreadTests`, `SaveMigratorTests` |
| Factory and profile | `SaveServiceFactoryTests`, `SaveServiceFactoryCrossProductTests` (every storage/codec/protector triple), `SaveProfileSOTests`, `SaveProfileValidatorTests` |
| Scheduler and flush | `SaveSchedulerTests`, `SaveFlushRegistryTests`, `GameLifetimeScopePauseQuitFlushTests` |
| Currency | `CurrencyResourceBankSaveHandleTests`, `CurrencyLegacyImportIntegrationTests` |
| Chests | `ChestsMinigameSaveTests` (including the two that pin decision #17) |
| The inspector | `SavePipelineProbeTests`, `SaveTamperTests` |

`FakeSaveStore` and `FakeSaveCodec` let a test point every field of the pipeline somewhere it
controls, so `SaveService`'s own logic - the first-run/corrupt distinction, the version and
component checks, and exactly when `SaveAsync` touches the codec - can be proven without a real
file system or `JsonCodec`'s real serialization standing in the way. `FileStoreTests` covers what
only a real file system can prove.

`PrettyJsonCodecTests` and `SaveCodecEnvelopeValueExactnessTests` split
[saving.md](saving.md)'s "Value-exactness, and where the formatting stops" between them:
`SaveCodecEnvelopeValueExactnessTests` covers that every value in a text-safe body survives even
though `PrettyJsonCodec`'s own whitespace normalises away on a `Parse`; `PrettyJsonCodecTests` covers
the other half, that the file on disk, before anything ever re-`Parse`s it, still carries the codec's
own indentation verbatim. `SaveCodecEnvelopeValueExactnessTests` enumerates `SaveCodec` with
`Enum.GetValues` rather than listing its three members by hand, so a fourth codec is picked up here
automatically the moment it is added; `CodecFor`'s default arm throws rather than silently skipping
the new member, the same reasoning `SaveServiceFactory`'s own switches use for why a missing arm has
to be visible rather than quietly wrong.

`AesProtectorTests` and `HmacSignedProtectorTests` check a thrown `PayloadTamperedException` by
name, through `error.GetType().Name`, rather than by catching the type directly: it is internal to
`Company.ChestGame.Saving`, and no assembly in this project has `InternalsVisibleTo` into it
(confirmed absent project-wide), so `Exception.GetType()` is the only way a test outside that
assembly can still pin the exact type. `SaveServiceTamperDetectionTests` proves the same class of
failure through the public `SaveException.PayloadTampered` instead, which is what an actual caller
ever sees.

`SaveServiceTests` isolates `SaveService`'s own logic from a real file system and from `JsonCodec`'s
real serialization with `FakeSaveCodec`, `FakePayloadProtector` and `FakeSaveStore`; `FileStoreTests`
covers what only a real file system can prove, and `SaveEnvelopeTests` covers the byte-exact round
trip that this fixture's fakes do not exercise. Both real components this fixture composes by
default, `JsonCodec` and `NoProtection`, are text-safe, so nothing real ever drives `SaveService`
into computing `IsTextSafe` false;
`SaveAsync_WhenTheProtectorIsNotTextSafe_StoresABase64BodyThatLoadAsyncCanStillRead` sets
`_protector.IsTextSafe` on the fake to prove the composition -
`_codec.IsTextSafe && _protector.IsTextSafe` - actually reaches `SaveEnvelope.Wrap` and round-trips
through `LoadAsync`, not only that `SaveEnvelope` can do it in isolation.

`SaveServiceCompletesOnCallingThreadTests` proves both answers of `ISaveService.CompletesOnCallingThread`
without a real thread hop: `ThreadHoppingStore`'s own `CompletesOnCallingThread` (proven separately in
`ThreadHoppingStoreTests`) already answers both ways depending on what it wraps, which is enough to
drive `SaveService`'s pass-through through both branches from the outside.

The real `JsonCodec` and `NoProtection` run underneath `SaveServiceLegacyImportTests`, rather than
the isolated fakes `SaveServiceTests` uses, because the idempotency scenario it covers needs an
honest round trip: whether a value saved after the import is really what a second load reads back,
not what a fixed fake decode result says it is.
`LoadAsync_WhenClearThrows_DoesNotFailTheLoad_AndDoesNotLoseTheImportedData` has to declare the
error log `LogAssert.Expect` produces before triggering it, because Unity's test runner treats an
unexpected error-level log message as a test failure on its own. The two things the test's own name
claims - the load does not fail, the imported data is not lost - still have to hold regardless of
what gets logged along the way.

Every `(SaveStorage, SaveCodec, SaveProtection)` triple `SaveServiceFactory.CreateFrom` can build is
walked in `SaveServiceFactoryCrossProductTests` with `Enum.GetValues` rather than listed by hand, so
a fifth storage, a fourth codec or a sixth protector joins this test's coverage the moment it is
appended to its enum, with nothing in the fixture needing to change. The fixture's own `Inventory`
model deliberately carries more than a bare `int` — a string, a number and a list — so a codec that
only happened to round-trip a single scalar field would not quietly pass this test. Its `SetUp` gives
each test case its own GUID-suffixed key rather than a shared constant, because `SaveServiceFactory`
hands every `SaveStorage.InMemory` case the same process-lifetime `InMemoryStore` (so that two
independently constructed `InMemory`-backed services actually see each other's writes; see
[saving.md](saving.md), "SaveComponentFactory, SaveFactoryInputs and SaveServiceFactory" for
`SharedInMemoryStore` itself). That store outlives any single test case, so two cases sharing one key
would leak into each other and the failure would depend on run order. File/AtomicFile already get a
fresh temp root per test and PlayerPrefs a fresh key prefix, so the per-case key is this fixture's
own isolation for the one backend neither of those covers.

`File` and `AtomicFile` both leave `CreateFrom_EveryStorageMember_RoundTripsThroughItsBackend`
passing in `SaveServiceFactoryTests` even if the factory wired the wrong one of the two in, since
both are files under the same root.
`CreateFrom_File_IsBackedByFileStore_WhichNeverKeepsABackup` and
`CreateFrom_AtomicFile_IsBackedByAtomicFileStore_WhichKeepsABackupAfterASecondSave` prove which
backend actually landed by checking for the `.bak` generation only `AtomicFileStore` ever writes.

`SaveSchedulerTests` proves everything provable without a real thread hop or a real clock: the
constructor's own guards, `CanFlushBlocking` reading straight from the `ISaveService` it was given,
and `MarkDirty`/`FlushAsync`/`FlushBlocking` all throwing `SchedulerDisposed` once `Dispose` has run.
`FakeGameClock` stands in for `IGameClock` here because none of these cases call `MarkDirty` and wait
for the coalescing window to actually elapse. Coalescing, one write in flight, `FlushBlocking`'s throw
over a genuinely hopping composition, and `Dispose`'s own best-effort flush and logged loss all need a
player loop or a real hop, so those live in `SaveSchedulerPlayModeTests` instead.

Every `ISaveService` in `ChestsMinigameSaveTests` is a real `SaveService` over a `FakeSaveStore`,
the same shape `GameLifetimeScopePauseQuitFlushTests` already uses for the currency scheduler, so
what these tests pin is the actual bytes `ChestsMinigameController` persists rather than a mock's
recorded call.

| PlayMode fixture | What only play mode can prove |
|---|---|
| `SaveSchedulerPlayModeTests` | The coalescing window against a real player loop, and the disposal flush with its logged loss |
| `ThreadHoppingStorePlayModeTests` | That the hop really leaves and returns to the main thread |
| `SaveInspectorPanelPlayModeTests` | The authored inspector UI: real prefab, `.uxml` and `.uss` still bind |
| `SaveBenchmark` | Logs the size numbers `saving.md` quotes; asserts round-trips and byte counts, never a duration |

`SaveSchedulerPlayModeTests` drives every wait through `UniTask.WaitUntil` against a counter or flag
the fixture controls (`RecordingSaveStore.WriteCount`, `SaveScheduler<T>.IsFlushing`) rather than a
fixed sleep, bounded by a generous cancellation timeout only so a genuine hang fails loudly instead
of stalling the suite; nothing in the fixture asserts on how long anything took. Its `TearDown`
releases every `RecordingSaveStore` left blocking a write and disposes every scheduler the test
created, swallowing the exception some tests already provoke deliberately by disposing the scheduler
themselves first, so a worker thread is never left parked on a gate nobody will release, and no
scheduler's loop keeps running into the next test. `WindowMilliseconds` is kept short (40ms) so the
suite stays fast: `SaveScheduler<T>.DefaultCoalesceWindowMilliseconds` (1000ms) is a production
default, not something a test waiting on a real clock should wait out.
`MarkDirty_SeveralCallsInsideOneWindow_ProduceExactlyOneWrite_CarryingTheLastState` yields two frames
after the expected write count is reached, giving a bug that double-writes one more frame to show
itself before the test asserts. `Dispose_WithAWriteMidHopAndANewerOneQueued_LogsNamingTheKey_AndDoesNotThrow`
and `Dispose_WithAPendingWriteThatNeverStartedFlushing_OverAHoppingComposition_LogsTheLossAndDoesNotThrow`
both release the write they armed and then wait for it to land before finishing, so the abandoned
background write settles instead of leaking a live continuation into whatever test runs next.

Both checks in `ThreadHoppingStorePlayModeTests` are deterministic identity comparisons - which
thread a write ran on - never timing; nothing in this fixture asserts on how long anything took.

`SaveInspectorPanelPlayModeTests.BuildPanel` yields two frames after instantiating the prefab:
`SaveInspectorPanel` binds its controls in `Start`, not on the frame `Object.Instantiate` runs, so
nothing under `Chrome()` or `ToggleRoot()` can be read before that.
`ExpandedControls_AreInsideTheChrome_AndResolveToATouchFriendlyHeight` asserts both a resolved
height and containment inside the chrome: a control carrying a `min-height` resolves to it whether
or not the box around it is actually visible, so height alone would still pass a control clipped
outside the chrome - the containment check against `Chrome().worldBound` is what catches that case.
`ControlRows_KeepEveryControlOnScreen_AtTheNarrowestWidthAPhoneGives` walks `chrome[1]` through
`chrome[4]`: `SaveInspector.uxml` puts the title bar at index 0, the three axis rows (storage, codec,
protection) at 1-3, and the run row at 4.

### RecordingSaveStore, and why the gate is two fields, not one

`RecordingSaveStore` lets a PlayMode test park a write mid-flight and release it on demand,
deterministically, without blocking a real thread: `ArmBlockingWrite()` arms a
`UniTaskCompletionSource` that the next `WriteAsync` call awaits, and `ReleaseWrite()` completes it.
That completion source travels through two fields rather than one because the two methods read it at
different times relative to `WriteAsync` consuming it. `_armedGate` is set by `ArmBlockingWrite` and
consumed - nulled out - the moment a write claims it, which is what makes a follow-up write in the
same test complete immediately rather than blocking on a stale arm. But `ReleaseWrite()` can run after
that point, while the first write is still parked awaiting the gate it claimed; if it read `_armedGate`
at that point it would find `null` and release nothing. `_activeGate` holds the same completion source
from the moment a write claims it until that write finishes, so `ReleaseWrite()` always has something
live to signal regardless of when it is called relative to `WriteAsync` taking over.

### Simulating a click in a PlayMode UI test

A `PointerDown`/`PointerUp` pair does not reliably click a `Button` in these fixtures: `Clickable`
only fires on the up event if the panel's picking still reports the element as the one under the
pointer, and forcing `target` on a synthetic event skips the picking that state depends on.
`NavigationSubmitEvent` is the real alternative rather than a workaround - `Button`'s constructor
registers `OnNavigationSubmit`, which calls `clickable.SimulateSingleClick` directly (confirmed by
decompiling `UnityEngine.UIElementsModule.dll`, not assumed from docs). Both
`SaveInspectorPanelPlayModeTests.Click` and `PoolingDemoPanelPlayModeTests.Click` use this pattern.

### SaveProfileSOTests, and why it drives fields through SerializedObject

`SaveProfileSOTests` drives `SaveProfileSO`'s private serialized fields through `SerializedObject`
rather than through reflection, so that what the fixture proves is that `SaveServiceFactory` actually
reads what the inspector's dropdowns write - a reflection-driven fixture would only prove the fields
exist, not that the inspector-authored path reaches them.

### SavePipelineProbeTests, and why isolation is per-key not per-store

`SaveComponentFactory` hands every `SaveStorage.InMemory` case in this fixture the same
process-lifetime store (see `SaveServiceFactoryCrossProductTests`, above), so tests in
`SavePipelineProbeTests` cannot isolate by using a fresh store per case the way a file-backed fixture
would. Isolation instead comes from a unique save key per test; a new test added to this fixture must
not share a key with an existing one, or the two will read and write the same in-memory state.

### FakeLegacyImport, and how OnClear proves write-before-clear directly

`FakeLegacyImport.OnClear` runs from inside `Clear()` itself, so a test can inspect or mutate the
world at exactly the point `SaveService` considers the import finished. That lets a test prove the
write-before-clear ordering (see [saving.md](saving.md), "The legacy import: `CurrencyLegacyImport`")
directly, by observing state from inside the hook, rather than merely inferring the ordering from
call counts.

### FakeGameClock, and what its knobs are for

`DeltaTime` defaults to 0.05 (50ms): a round number that keeps the frame counts a test asserts against
small and exact, not an approximation of a real device's frame rate.

`FrameWaitersResumeFirst` exists because the order in which a frame tick and a due delay resume, when
both come due on the same `AdvanceFrame` call, is not a documented guarantee of the real player loop,
only its observed behaviour today. Tests flip the knob and check that the flow under test does not
depend on which one goes first, rather than baking in an assumption Unity does not promise to keep.

`Release` snapshots the waiters that are due into a separate list before resuming any of them, because
resuming a waiter runs its continuation synchronously, and that continuation usually parks a fresh
waiter of its own for the next frame. Resuming directly off the live list while a continuation mutates
it would skip or double-release a waiter.

### SynchronousUniTask, and the signal a pending task sends

`Result` and `Complete` assert the task they are handed is not still pending before reading its
result. Tripping that assertion is not a bug in the helper: it means the code under test has started
depending on something that genuinely suspends - a real clock, a real thread hop - and the fix is to
move that test to the play-mode suite, not to find a way to keep waiting for it here.

### Reflection helpers in the EditMode and PlayMode fixtures

`ListAssetWith` (in `AddressablesContentSourceTests`) fills a `*ListSO` authoring asset's entries by
reflecting into its private serialized field, the same reflect-the-field-in pattern
`MinigameDefinitionAuthoring` uses, rather than adding a test-only public setter to production code.
That is what lets the assertion that follows be identity ("this exact entry came back") rather than
merely "something came back".

### FrameBudgetedLoopTests, and what the tests are shaped around

`FrameBudgetedLoopTests` is written to prove the split rather than the work: every assertion is about
which frame a unit lands in, not about the unit itself. `FakeGameClock` decides when a frame happens,
and `Spend` is what lets a unit cost time, which is the only way a time budget can run out with
nothing driving a real player loop. `CostlyStep` calls `Spend`; `FreeStep` does not, and stands in
wherever a test needs units to run without exercising the split, such as checking that an empty or
already-cancelled fill does nothing.

The default constants are chosen so the arithmetic is exact: a ten-millisecond budget and three
four-millisecond units means the third unit is the one that pushes the running total past the budget,
so a test built on them can assert exact frame counts rather than approximate ones.

Because `RunAsync` runs synchronously up to its first `await`, whatever ran before the method returns
is exactly the first frame's worth of work; `RunAsync_SplitsTheWorkAcrossFrames_RatherThanDoingItAllInOne`
reads `_ran.Count` immediately after calling it for that reason, before ever advancing the clock.

`RunAsync_PlacesMoreOfACheaperUnitInTheSameFrame` runs two loops side by side, each against its own
`FakeGameClock`, because `Spend` moves the clock the other loop is reading; sharing one clock between
them would let one loop's cost bleed into the other's frame count.

### RecordingBootStatus, and what it makes assertable

`GameBootstrapperFailureTests` records what `IBootStatus` was told through a private
`RecordingBootStatus` that just remembers the last message reported, rather than reading a live UI
label. That is what lets the suite assert on boot's narration directly - that a failure replaces the
loading message, that cancellation leaves it untouched - with no `TextMeshPro` component and no scene.

### RewardsManagerTests, and pinning the random draw

`RewardsManager.GiveRandomCurrencyReward` draws over the whole `CurrencyType` enum, so a real random
provider would make any one test's assertions non-deterministic. `RewardsManagerTests` uses
`FakeRandomProvider` to pin the draw's result before each call, so a single test can force a
particular branch (Coins, Gems) and assert the exact grant, popup and event that branch produces,
rather than sampling many runs statistically. `RangeSequence` lets a test queue more than one result
in advance, for a test that calls `GiveRandomCurrencyReward` more than once and needs each call to
land on a different branch.

### AssetHandleRegistryTests, and why its handles are real operations

`AssetHandleRegistryTests` builds its `AsyncOperationHandle` values from a real `ResourceManager`
(`HandleNamed`, via `CreateCompletedOperation`) rather than default handles. Every
`default(AsyncOperationHandle)` compares equal to every other, so a fixture using them could not
tell "handed back both handles" from "handed back the same one twice" - the exact distinction
several of its assertions exist to prove.

### Expecting the error Addressables logs before it throws

`AKeyThatIsNotInTheCatalog_SurfacesAsAMissingAsset` and `ALabelThatShipsWithNothing_SurfacesAsAMissingAsset`
both call `LogAssert.Expect` before asserting on the thrown exception. Addressables logs an error on its
way to throwing over a key or label nobody authored - a label nobody authored is the same authoring
mistake as a key nobody authored, and Addressables logs before it throws for both - and an unexpected
error log fails a test on its own regardless of what it asserts afterward. Expecting the log first
keeps the test about the translation `AddressablesAssetProvider` performs, rather than about
Addressables' own logging.

### AReferenceLoadCancelledBeforeItArrives_LeavesNothingLoaded, and how the probe works

`AReferenceLoadCancelledBeforeItArrives_LeavesNothingLoaded` proves that `AddressablesAssetProvider.LoadAsync`
unwinds a cancellation without leaking the ref-count it already took. Addressables takes the ref-count
on the call rather than on the await, so a token that fires while the bytes are still coming used to
throw straight past the provider's own bookkeeping; `GameShellView` passes `GetCancellationTokenOnDestroy`,
so leaving the scene mid-load is exactly this case, and the test runs in play mode because the leak it
is proving the absence of is a real `ResourceManager`'s ref-count.

The test warms up the reference and releases it once before the cancelled load, because an
uninitialised Addressables answers every load with a chained operation that never finishes on the
frame it was asked for - without the warm-up, the probe below would read "not held" no matter what
actually happened. After the cancelled load unwinds, the test waits three frames for the operation
Addressables actually started to keep running, then asks whether a fresh `AsyncOperationHandle` for the
same reference comes back already done. Addressables hands back an operation it already holds
finished, while one it does not hold has to be started and never finishes on its own frame - so
"was it done immediately" is reading whether the cancelled load's ref-count is still outstanding,
which has no other observer.

### MinigameDefinitionAuthoring, and why it reaches through reflection

`MinigameBaseSO`'s authored fields are serialized and private, so a definition built with
`CreateInstance` carries empty ones. `MinigameDefinitionAuthoring` writes them directly through
reflection instead, which means no production type has to open a setter it does not otherwise
need. `WithContent` sets the content label and the load policy together, because the two are one
decision: a label with no policy names content nothing will ever fetch. `WithViewReference` walks
up from the concrete definition's own type to find `_viewRef`, because that field lives on the
generic `MinigameBase<TController, TView, TMinigame>` base rather than on `MinigameBaseSO` itself.

Every field lookup is checked for null before use and throws `MissingFieldException` naming the
missing field, rather than let a renamed production field surface as a `NullReferenceException`
from a helper nobody would think to suspect.

### What the minigame fixtures choose not to fake

`FakeMinigameController.Inject` takes only an `IObjectResolver`, not a game service, so any test's
VContainer builder can satisfy it regardless of what else that builder registers.

`ChestsMinigameControllerTests.ConfigureAndInject` builds the controller against the real
`ChestsMinigameConfig` rather than a fake, because it is a plain validated value with no engine
dependency behind it. It also injects a real `ISaveService` over a `FakeSaveStore` rather than a
mock: `ChestsMinigameSaveTests` is where the save behaviour itself is pinned, so this fixture only
needs `Inject` to succeed the way it always has.

`MinigameManagerTests` registers a real `FakeAssetProvider` on the VContainer builder it hands
`MinigameManager`, rather than leaving `IAssetProvider` unregistered, because the container built for
`FakeMinigameSO` reaches its content through that provider: a resolver with nothing registered for it
could not inject the container at all.

### MinigameContainerContentTests, and its fixture choices

`MinigameContainerContentTests` runs against `FakeAssetProvider`, which hands back already-completed
tasks, so `BeginAsync` runs synchronously inside the calling `SynchronousUniTask.Complete` and every
effect can be asserted the moment it returns.

The two timeout tests cannot use `SynchronousUniTask` the way the rest of the fixture does: a real
deadline is a real timer running on a background thread, so `BeginAsync` is genuinely still pending
when the call returns. `WaitFor` blocks the calling thread on that task instead. Nothing that
completes the task needs the main thread, so blocking here cannot deadlock, and reading the result
through `Task.GetAwaiter().GetResult()` rethrows the original exception rather than wrapping it in an
`AggregateException`. Three constants tune that wait: `SHORT_DEADLINE` (50ms) is short enough that a
timeout test costs milliseconds rather than the 90 seconds the game ships with, and long enough that
it cannot fire before the start it bounds has actually begun; `UNREACHABLE_DEADLINE` (5 minutes) is
longer than any test run, so a test about caller cancellation can be sure the deadline itself never
fires and cancellation is the only thing that could have ended the wait; `WAIT_LIMIT` (10s), `WaitFor`'s
own ceiling, sits above `SHORT_DEADLINE` so a slow machine is not a spurious failure and far below the
shipped budget so a deadline that never fires makes the test fail rather than hang.

`End_ReleasesTheViewAndTheMinigamesOwnContent` destroys the view instance before calling `End`,
because `Object.Destroy` is a logged error in edit mode. Releasing the content handles does not
depend on the view instance surviving; asserting `End` against a still-live view is the play-mode
fixture's job.

`ConfigurableMinigameSO`, the fixture's test definition, derives from the generic
`MinigameBase<TController, TView, TMinigame>` rather than the non-generic `MinigameBaseSO`, so the
`ConfigureControllerAsync` hook and its ordering run through the real `GetMinigameContainer` and the
real `BeginAsync` rather than a stand-in for either.

### ChestElementViewLifetimeTests, and its fixture choices

`BuildView` uses three distinct generated sprites rather than the real prefab's art, because all that
matters is that the fixture can tell them apart: with every slot left null, every chest state would
paint the same nothing and a view showing the wrong sprite would still look right. Its image, slider
and button are built as real children of the view's `GameObject`, as the real prefab wires them,
because they have to die with the view: a leaked subscription that instead kept working against
still-live objects would go unnoticed.

## Two settings the suites depend on

Both look unrelated to gameplay and both are load-bearing, so they are written down here rather
than left to be tidied away by someone reading the diff.

`runInBackground: 1` in `ProjectSettings.asset`. The pooling work added a lot of frame-dependent
play-mode coverage — budgeted board fills, the race fixtures, a panel fixture that yields frames
to settle layout. With it off, the player loop can stall whenever the editor is not focused,
which is the normal condition for a headless run.

`com.unity.pipeline` in `Packages/manifest.json`. Editor and pipeline tooling, used to drive a
live editor from the command line; nothing under `Assets/` references it, and nothing should.

## Running them

In the editor: Window, General, Test Runner, then run the EditMode or PlayMode tab.

From the command line, with the editor closed:

```bash
ci/run-tests.sh             # both suites
ci/run-tests.sh EditMode    # one of them
```

Batch mode fails outright if the editor is holding the project lock. The exit code is right when that
happens, but the printed summary is not: `run_suite` never clears the old XML before running, so it
reads the counts off the previous run and prints them under a run that never happened. Check the
timestamps in `ci-results/`, or delete them first, if a result looks too good.

`summarize` pulls its counts off the NUnit `<test-run>` root's attributes rather than parsing the
log, and comes back empty when the run failed before producing any results at all; `run_suite`
reports that case as "no results written; see <log>" instead of printing blank counts.

The script finds the editor from the version in `ProjectSettings/ProjectVersion.txt`, or uses `$UNITY`
if you point it somewhere else. Results land in `ci-results/` as NUnit XML plus the editor log. Both
suites run even if the first one fails, and the exit code is nonzero if either did.

`ci/build-addressables.sh` has the same shape and builds the addressable content, which the tests
deliberately do not need: both suites run against the asset database. That depends on a machine-local
setting, not a committed one, so if keys start failing to resolve see
[the play mode script](content-delivery.md#the-play-mode-script-is-not-committed).

## CI

There is no pipeline yet. `ci/run-tests.sh` is the part that is not provider-specific, so whatever
runs it later only has to check out the repo, supply a licensed Unity, and call one command.

Two things any Unity pipeline needs regardless of provider. A licence has to be supplied at runtime,
which for a Personal licence means the account credentials reaching the runner as secrets. And
`Library/` has to be cached, keyed on `Assets/`, `Packages/` and `ProjectSettings/`. Without it every
run re-imports the project from scratch, which costs several minutes against the 22 seconds the tests
actually take.
</content>
