# Testing

Two suites, split by what only a real engine can prove.

| Suite | Ours | Wall time |
|---|---|---|
| EditMode | 176 | ~0.6 s |
| PlayMode | 34 | ~22 s |

Reproduce them with `ci/run-tests.sh`; the wall times move a little run to run. The numbers are
written here rather than linked because `ci-results/` is gitignored, so a fresh clone has none until
it runs the suites itself. The EditMode runner reports 177: the
Addressables package ships one editor test of its own
(`Unity.Addressables.DocExampleCode.Editor.Tests`) and Unity picks it up. It is not ours and is not
counted above.

## What lives where

EditMode covers the logic exhaustively against fakes. PlayMode is a thin layer of integration smoke
tests for what only a real player loop, a real prefab or a real content catalog can prove: that
`UnityGameClock` drives the same chest flow the fake clock does, that a view really unsubscribes when
destroyed, that a popup really lands under its parent, and that the keys and authored references the
game ships really do resolve through Addressables.

The time difference is the point. The edit-mode suite runs the entire chest-opening flow, including
cancellation, without waiting for anything, because `FakeGameClock` decides when a frame happens. The
22 seconds in play mode are almost entirely one class waiting on real timers, which is why so little
lives there.

`FakeAssetProvider` keeps the fast suite off Addressables entirely, the same way `FakeGameClock` keeps
it off the player loop. The four content sources are asked what key they want and what they do with
the answer, with no catalog and no bundle behind them.

Play-mode tests assert settled states rather than mid-flight ones, so a slow frame on a cold CI runner
cannot cause a spurious failure.

Fakes live in `Tests/Common/` and are shared by both suites. There is deliberately no fake catalog:
`PopupCatalog` and `MinigameCatalog` take plain lists, so the tests use the real ones.

`GameLifetimeScopeTests` runs against `GameLifetimeScope.RegisterCoreServices` and
`RegisterLoadedServices` themselves rather than a copy of them, so dropping a registration from the
composition root fails there.

For the fixture-by-fixture map, see [context/assemblies-and-tests.md](context/assemblies-and-tests.md)
section 8 and [context/self-contained-minigames.md](context/self-contained-minigames.md) section 8.

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
