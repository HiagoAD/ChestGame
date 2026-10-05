# Testing

Two suites, split by what only a real engine can prove.

| Suite | Ours | Wall time |
|---|---|---|
| EditMode | 687 | ~1 s |
| PlayMode | 74 | ~26 s |

Reproduce them with `ci/run-tests.sh`; the wall times move a little run to run. The numbers are
written here rather than linked because `ci-results/` is gitignored, so a fresh clone has none until
it runs the suites itself. The EditMode runner reports 688: the
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

`FakeAssetProvider` keeps the fast suite off Addressables entirely, the same way `FakeGameClock` keeps
it off the player loop. The four content sources are asked what key they want and what they do with
the answer, with no catalog and no bundle behind them.

Play-mode tests assert settled states rather than mid-flight ones, so a slow frame on a cold CI runner
cannot cause a spurious failure.

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

### The save suites never touch a real save

Every fixture that exercises a file-backed store writes into a per-fixture temp directory named with a
GUID and deletes it in teardown, and every fixture that touches `PlayerPrefs` uses a GUID-suffixed key
and calls `PlayerPrefs.Save()` after deleting it — a `DeleteKey` without that does not persist in batch
mode, which is how a leaked key was first noticed. `Application.persistentDataPath` and the real
`ResourceBankSaveData_CurrencyType` entry are off limits to both suites.

This is a rule with a scar behind it. `GameBootstrapperTests` boots the real `Boot` scene, which runs
the real `GameLifetimeScope.Configure()` — and `Configure` is Unity's own callback, so no test can pass
it an argument. Before the static overrides existed, that fixture resolved the currency manager against
a developer's actual save and deleted it. The seam that closed it is
`GameLifetimeScope.CurrencySaveInputsOverride` and `LegacyCurrencyPlayerPrefsKeyOverride`, assigned on
the line before `LoadSceneAsync` so the ordering rests on statement order rather than on how the
framework sequences its setup attributes.

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
| Stores | `FileStoreTests`, `AtomicFileStoreTests`, `PlayerPrefsStoreTests`, `InMemoryStoreTests`, `SaveStoreContractTests` (one shared contract across all four), `ThreadHoppingStoreTests`, `SaveStoreCompletesOnCallingThreadTests` |
| Codecs | `GzipJsonCodecTests`, `PrettyJsonCodecTests`, `SaveCodecToJsonTests`, `SaveCodecEnvelopeValueExactnessTests` |
| Protectors | `AesProtectorTests`, `HmacSignedProtectorTests`, `XorObfuscatorTests`, `Base64ObfuscatorTests` |
| Envelope and corpus | `SaveEnvelopeTests`, `SaveGoldenCorpusTests` (bytes an older build really wrote) |
| The service | `SaveServiceTests`, `SaveServiceMigrationTests`, `SaveServiceLegacyImportTests`, `SaveServiceTamperDetectionTests`, `SaveServiceCompletesOnCallingThreadTests`, `SaveMigratorTests` |
| Factory and profile | `SaveServiceFactoryTests`, `SaveServiceFactoryCrossProductTests` (every storage/codec/protector triple), `SaveProfileSOTests`, `SaveProfileValidatorTests` |
| Scheduler and flush | `SaveSchedulerTests`, `SaveFlushRegistryTests`, `GameLifetimeScopePauseQuitFlushTests` |
| Currency | `CurrencyResourceBankSaveHandleTests`, `CurrencyLegacyImportIntegrationTests` |
| Chests | `ChestsMinigameSaveTests` (including the two that pin decision #17) |
| The inspector | `SavePipelineProbeTests`, `SaveTamperTests` |

| PlayMode fixture | What only play mode can prove |
|---|---|
| `SaveSchedulerPlayModeTests` | The coalescing window against a real player loop, and the disposal flush with its logged loss |
| `ThreadHoppingStorePlayModeTests` | That the hop really leaves and returns to the main thread |
| `SaveInspectorPanelPlayModeTests` | The authored inspector UI: real prefab, `.uxml` and `.uss` still bind |
| `SaveBenchmark` | Logs the size numbers `saving.md` quotes; asserts round-trips and byte counts, never a duration |

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
