# Context: self-contained minigames and content delivery

Working context from the session that made a minigame a **unit of content delivery** — its own
assembly, its own config document, its own addressable group, fetched from a remote path either at
boot or on demand. That is the scope of this file.

The session before it built the assembly definitions and the test suite; its context is in
[assemblies-and-tests.md](assemblies-and-tests.md), and most of what it recorded still applies.
Read the README first for what the architecture *is*. These two files are why it got that way.

**Kept current.** Where later work changes something described here, update the description rather
than appending a correction. Everything below is true of the project as it stands.

---

## 1. What this set out to do, and why it started somewhere else

The request was "implement Addressables". Almost none of the work was about Addressables, because
two things blocked a minigame from being a delivery unit and neither was a bundle setting.

**The config was a god-object.** `Data.json` and `IGameConfig` mixed three chests-specific fields
(`ChestCount`, `AttempsCount`, `TimeToOpenChestMiliseconds`) with two global ones (`GemsReward`,
`CoinsReward`). A second minigame would have had to add its fields to the same interface, which every
consumer already sees.

**The minigame was not separable from the shell.** Its controller, view, model and definition all
lived in `Company.ChestGame.Gameplay` alongside `GameManager`, and `GameManager.NewChestsMinigame()`
called `StartMinigame<ChestsMinigame>()` — a compile-time reference from the shell to one specific
minigame. No amount of bundle configuration makes content self-contained while the code that owns it
ships in the shell's assembly.

So the architecture moved first and Addressables became a source swap at the end. That sequencing is
the single most important thing to understand about these commits: it is why the interesting diffs
are in phases 1–3 and the Addressables phase is deliberately boring.

**One constraint bounds the whole design: C# assemblies cannot ship in an AssetBundle.** A minigame's
controller and view classes are in the player from first install. What a remote group delivers is
everything *authored* — prefabs, sprites, documents — plus whether a minigame is available and when
its content arrives. That is why the descriptor asset naming a minigame stays local, paired 1:1 with
code that is local anyway, and it is what lets the type-keyed catalog survive.

### Two decisions taken beyond the config split

**The chests minigame gets its own assembly, and the shell stops naming it.** This is what makes
"self-contained" checkable rather than aspirational: a reference from the shell back to a specific
minigame stops compiling, the same way the existing assembly split keeps dependency directions
honest. It also settles a question the grouping work could not answer otherwise — a per-minigame
group is defined by "the assets this minigame owns", and with the code sitting in the shell's
assembly the only available answer is "whatever the view prefab happens to reference".

**The async boundary moved before Addressables arrived, not with it.** The sources went
`UniTask`-returning while their implementations still called `Resources.Load` and handed back an
already-completed task. Nothing downloaded in that phase. This isolated the signature churn — which
reaches the composition root, `LocalJsonGameConfig` and several fixtures — from the
loading-technology change, leaving the Addressables phase as a swap of four classes and nothing
else.

Both were load-bearing rather than opportunistic: without the first there is no authority on a
group's contents, and without the second the Addressables phase carries two unrelated kinds of risk
at once.

---

## 2. How this work was run, and how to continue it

Hiago set a specific working protocol. **It is still in force** — anyone continuing this should
follow it rather than inventing their own.

1. **Subagents write the code**, one per phase, briefed with that phase's plan section plus the
   working agreements it has to honour. The lead session does not write production code itself.
2. **The agent's report is not evidence.** Review adversarially: read every changed file, verify
   every new test by mutation, check no coverage was silently dropped or assertions weakened, and
   re-run the suite yourself from a clean `ci-results/`.
3. **Sanity-check the architecture separately from the behaviour.** Green tests prove the code
   works, not that it belongs here. Judge it against SOLID and the project's own agreements — SRP
   (has a class taken a second job?), OCP (can the next minigame be added without editing the
   framework?), LSP, ISP (the config split exists because `IGameConfig` failed this), DIP (does
   anything depend on a concrete loader or path where a seam already exists?). Report every
   deviation with its severity, including ones inherited rather than introduced.
4. **Agents never commit.** Work stays in the tree for review.
5. **Stop at the end of every phase** until Hiago explicitly says to proceed. Silence is not
   approval; a follow-up question is not approval.

This is not ceremony. It caught defects that green suites did not: two silent-leak bugs, an entirely
untested exception-translation path, and a false claim that had been written into both a code comment
and the README.

The verification loop itself is in the other file's section 3, and it is worth reading before you
trust a green run: this project can report a pass on a suite that never executed. The two traps that
have actually caught people here are the editor holding `Temp/UnityLockfile` so batch mode aborts,
and the previous `ci-results/*.xml` surviving that abort and still reading as a pass. Run
`rm -rf ci-results && ci/run-tests.sh` with the editor closed.

The content build is a separate script, `ci/build-addressables.sh`, over the permanent
`Company.ChestGame.Editor.AddressablesContentBuild.BuildFromCommandLine`. It is not part of the test
loop on purpose: both suites run against the asset database, so nothing there needs a bundle. Run it
when the delivery paths themselves are what changed, and read what it wrote under
`ServerData/[BuildTarget]/` — that is the only check that a group actually bundles as one unit rather
than merely being configured to.

---

## 3. What landed

Four commits on top of `main` (`26ae095`), on `feature/addressables`:

| Commit | What landed |
|---|---|
| `511fbd7` | The config split: the chests minigame stops sharing a document with the game |
| `2db6ecd` | The chests minigame gets its own assembly; the shell stops naming it |
| `8704733` | Boot scene, the scope split, and the async boundary — still on `Resources` |
| `6635a9a` | Addressables replaces `Resources`; the chests group goes remote |

**The config split.** Two documents, not one document with sections. `GameConfig.json` keeps what the
game owns; `ChestsMinigameConfig.json` holds the three fields only the chests minigame cares about,
parsed by the chests assembly, and **nothing outside that assembly can name a field called
`ChestCount`**. The framework got no config surface at all — no provider interface, no section
lookup, no id threading through `MinigameManager`. The descriptor holds a reference to its own
document and parses it itself. `GameConfigException` and a shared `ConfigValidation.Require` moved
into `Common` so the chests assembly needs no reference to `Config` whatsoever.

**The minigame's own assembly.** `Company.ChestGame.Minigame.Chests`, with its assets beside it under
`_Project/Minigames/Chests/`, so the group definition is literally "this folder".
`Company.ChestGame.Gameplay` shrank to the shell. `GameManager` carries an authored `_minigameId`,
and `CatalogBuilder.BuildById` runs over the same entries to give the catalog an id-keyed lookup
beside the type-keyed one.

**Boot scene and the scope split.** `Boot.unity` at build index 0 holds the root scope and
`GameBootstrapper`; `SampleScene` became `Game.unity`. `RegisterServices` split into
`RegisterCoreServices` (resolvable immediately, nothing in it needs an asset) and
`RegisterLoadedServices` (things built from content that has arrived, registered as already-built
instances), the latter on a child scope the bootstrapper creates before
`using (LifetimeScope.EnqueueParent(_gameScope)) await SceneManager.LoadSceneAsync("Game")`. **No
service ever exists with its data not yet arrived, so there is no `if (!_loaded)` guard anywhere.**

**Addressables.** New `Company.ChestGame.Assets` assembly behind `IAssetProvider`, with two load
routes, a release, and the two delivery routes (`GetDownloadSizeAsync`, `DownloadAsync`). The four
`Resources*Source` classes became `Addressables*Source`, `Assets/_Project/Resources/` was emptied
into `_Project/Content`, and package exceptions are translated at the boundary into
`MissingAssetException` (key not in the catalog) and `AssetLoadException` (key resolved, bytes did
not arrive).

**Delivery.** Two groups: `Core` (local — five entries: `GameConfig.json`, both lists, the popup
parent and the reward popup) and `Minigame.Chests` (**remote**, label `minigame.chests`,
`Remote.LoadPath = http://localhost:8080/[BuildTarget]`), with `BuildRemoteCatalog` on. Config and
popups stay local deliberately: the config has to parse before a download screen can render, and a
popup is what reports a download failure.

`ChestsMinigame.asset` is in no group at all. It ships inside the `Core` bundle as a hard dependency
of `MinigameList.asset`, which is a group entry and does hold it directly — and that is the intended
shape: the descriptor has to be there before anything can ask what minigames exist, while the content
it *names* stays out of `Core` because it is named through `AssetReference`s rather than held. Note
that `ChestsMinigame.asset` (the descriptor) and `ChestsMinigame.prefab` (the view, which *is* a
`Minigame.Chests` entry) differ only by extension, which makes bundle listings easy to misread.

The descriptor swapped its direct references for
`AssetReference`s and gained a `_contentLabel` and a `MinigameLoadPolicy`.
`MinigameContentPreloader` fetches `Preload` entries at boot with aggregate progress; `OnDemand`
entries download on first `BeginAsync`.

### The ordering contract that had to move

The config document is remote now, so it cannot be parsed inside the synchronous
`GetMinigameContainer`. Everything content-shaped arrives together at `BeginAsync` instead:
`MinigameManager.Get(id)` builds the container and injects the *container*, loading nothing;
`BeginAsync` ensures content is downloaded, loads the view prefab, calls
`ConfigureControllerAsync`, injects the controller, and instantiates the view.

**Configure must land before inject**, because `ChestsMinigameController.Inject` builds its chest
list from the configured count. The contract did not change — only its location. It is pinned by
`MinigameContainerContentTests.BeginAsync_ConfiguresTheControllerBeforeInjectingIt`, which fails
under mutation.

Instantiation stays `_resolver.Instantiate` rather than `Addressables.InstantiateAsync`, so
VContainer's injection semantics are untouched, and the *asset* handle is released rather than the
instance — which is what keeps `End` synchronous and idempotent.

One more trap worth knowing: **the auto-inject list must live on `GameSceneLifetimeScope`, not on
the root.** Moving it to the root breaks once `IMinigameManager` is registered on the child. No
unit test catches this, because nothing else loads the scene; `GameBootstrapperTests` is the guard.

---

## 4. First attempts that were replaced

Same spirit as the other file's table — the current design mostly exists because something simpler
was tried and was wrong in a way worth recording.

| First attempt | Why it was replaced |
|---|---|
| One `Data.json` and one `IGameConfig` carrying every feature's fields | A god-object: three of its five fields meant nothing outside the chests minigame, and it made that minigame permanently dependent on a shared document. Split into `GameConfig.json` (rewards) and a chests-owned `ChestsMinigameConfig.json`, with a `ConfigureControllerAsync` hook as the only framework surface it needed. |
| The chests minigame living in the shell's assembly, started by `StartMinigame<ChestsMinigame>()` | "Self-contained" was held up by convention, and the shell named the minigame at compile time, so nothing could answer "which assets does this minigame own". The minigame moved to `Company.ChestGame.Minigame.Chests` with its assets beside it, and the shell now asks for an authored id. |
| Four `Resources*Source` classes, each calling `Resources.Load` | Every one of them knew both a path *and* a loading technology, so swapping the technology meant touching all four and their tests. `IAssetProvider` took the technology, the sources kept the key, and `Company.ChestGame.Assets` became the only assembly that references Addressables. |
| `AddressablesAssetProvider` keying its handle dictionary on the `AssetReference` instance | `AssetReference` overrides neither `Equals` nor `GetHashCode`, so the lookup had reference identity. It worked only because every production caller hands back the very same serialized field it loaded with. The bookkeeping moved to `AssetHandleRegistry`, keyed on the runtime key, which is also what made it testable without a content catalog behind it. |
| `ChestsMinigame.asset` holding its view prefab and config document as direct references | The `Minigame.Chests` group bought nothing: loading the descriptor, which the catalog does at boot for every minigame, dragged the whole chests bundle in as a hard dependency. The fields became `AssetReference`s, which serialize as GUID strings rather than as dependencies. That forced the loading out of the synchronous `GetMinigameContainer` and into `MinigameContainer.BeginAsync`, taking the configure-before-inject contract with it. |

---

## 5. The agreement this work added

**One assembly calls the loading technology.** Only `Company.ChestGame.Assets` calls Addressables.
Every other assembly reaches assets through `IAssetProvider`, and the package's exception types are
translated at that boundary. A `using UnityEngine.AddressableAssets` outside that assembly, for
anything other than the serializable reference types, is a regression.

This is **deliberately weaker than the invariant it replaced**, which was that no other assembly
*referenced* the package at all. Two things ended that, and neither is avoidable:

- A serialized `AssetReference` field is only authorable in an assembly that can see the type, so
  `Company.ChestGame.Minigame` and `Company.ChestGame.Minigame.Chests` reference the package for it.
- Once `IAssetProvider` carries an overload naming `AssetReference`, *every* call site has to be able
  to resolve that overload, whether or not it uses it. `Company.ChestGame.Config` and
  `Company.ChestGame.Popups` call only the key route and still fail to compile without the reference
  (`CS0012: the type 'AssetReference' is defined in an assembly that is not referenced`).

Nine asmdefs reference the package now, up from one: the seam itself, `Company.ChestGame.Editor`
for the content-build script, the three test assemblies, and the four this argument is about —
`Minigame` and `Minigame.Chests` for their serialized fields, `Config` and `Popups` for nothing but
the overload. The reference is the mechanism — it is what
stops a descriptor from depending on its own bundle — so the narrower invariant was the thing that
had to give. Splitting the seam into two interfaces would buy the second bullet back, at the cost of
a caller having to know which one it wants. That decision is still open; see section 12.

All the agreements in the other file still hold, unchanged.

---

## 6. Traps — Addressables and Unity 6

**UniTask's `AsyncOperation` awaiter is compiled out on Unity 2023.1+.** `UnityAsyncExtensions`
guards `GetAwaiter(this AsyncOperation)` with `#if !UNITY_2023_1_OR_NEWER`, so on this editor
`await SceneManager.LoadSceneAsync(...)` binds to the generic `IEnumerator` overload instead and
fails with **CS0311**. Use `.ToUniTask()`, which is not version-gated. The same reflex applies to
`AsyncOperationHandle`: `AddressablesAssetProvider` calls `handle.ToUniTask(...)` explicitly.

**UniTask's Addressables support is off until the package is installed.**
`UniTask.Addressables.asmdef` carries a version define that only defines
`UNITASK_ADDRESSABLE_SUPPORT` when `com.unity.addressables` is present, and the whole of
`AddressablesAsyncExtensions.cs` is inside that `#if`. Before the package is in `manifest.json`,
`ToUniTask` on a handle does not exist and the error says nothing about why.

**A failed Addressables operation logs an error before anyone sees it.**
`AddressablesImpl.LogException` is installed as `ResourceManager.ExceptionHandler` during
initialization and does `Debug.LogError(ex.ToString())` for any operation that ends `Failed` — and an
unexpected error log fails a Unity test by itself. **Every negative test against the real provider
needs `LogAssert.Expect`**, whichever route it exercises. For an unknown key specifically,
`LoadAssetAsync` logs "No Location found for Key=..." on its way to throwing `InvalidKeyException`,
so that test needs `LogAssert.Expect(LogType.Error, new Regex("No Location found for Key=..."))` or
it fails for the logging rather than for the behaviour under test.

**`GetDownloadSizeAsync` and `DownloadDependenciesAsync` fail through the handle, not the call.**
Both return `ResourceManager.CreateCompletedOperationWithException<T>(..., new InvalidKeyException(...))`
when the key resolves to no locations, so nothing throws until the handle is awaited — which means
the `try` has to wrap the `await`, not just the call, exactly as the load routes already did.

**`AssetReference` has no value semantics.** It overrides neither `Equals` nor `GetHashCode`
(verified in the package source), so a `Dictionary<AssetReference, _>` keys on the instance. Two
references carrying the same GUID are two different keys. `AssetHandleRegistry` keys on
`RuntimeKey.ToString()` instead, which is the GUID plus `[SubObjectName]` when there is one — so a
sprite out of an atlas is still distinct from the atlas. `AssetHandleRegistryTests` asserts the
package's behaviour as a premise, so the day the type gains an `Equals` the indirection is flagged
rather than left behind.

**`AssetReference.LoadAssetAsync` stores its handle on the reference itself**, which is a field on a
shared definition asset. A second load logs an error and loses the first handle, and `ReleaseAsset`
warns when there is nothing to release. `AddressablesAssetProvider` therefore passes the reference to
`Addressables.LoadAssetAsync` as a key and keeps the handles itself, which is also what lets `Release`
be safe on a reference that was never loaded.

**An `AssetReference` serializes as a nested string, not as an object reference.** In YAML it is
`m_AssetGUID` plus three empty strings and a bool, not `{fileID, guid, type}`. That difference is the
entire mechanism: a GUID string is not a dependency, so the descriptor holding one does not pull the
asset's bundle in. The fields were rewired by hand and then re-saved through the editor to confirm
Unity writes the same thing; it did, byte for byte.

**An asset in a `Resources` folder *and* an addressable group ships twice.** The move to
`_Project/Content` is not cosmetic. `Assets/TextMesh Pro/Resources` and
`Assets/AssetLibrary/**/Resources` are not ours and are left alone.

**Addressables loads never complete synchronously.** `Resources.Load` returned before the call did,
which is why `SynchronousUniTask` worked in play mode. Addressables does not: the first call runs
`InitializeAsync` and every load takes at least a frame. That is what turned
`PopupManagerIntegrationTests` into `UnityTest`s. `SynchronousUniTask` is now an edit-mode-only
helper, and it still refuses a pending task loudly rather than handing back a default.

**Addressables settings cannot be authored by editing a file.** They need editor APIs
(`AddressableAssetSettingsDefaultObject.GetSettings(true)`, `CreateGroup`, `CreateOrMoveEntry`,
`entry.address`). The way that was done here is a throwaway script under `Assets/<Name>Setup/Editor/`
with its own asmdef, run with `-batchmode -executeMethod` (no `-quit`), exiting 0 or 1 itself, then
deleted. `ci/run-tests.sh` runs both suites with no content build, so the committed settings must
leave the play mode script on **Use Asset Database (fastest)**.

**The play mode script lives in `Library`, not in the settings asset.**
`AddressableAssetSettings.ActivePlayModeDataBuilderIndex` delegates to `ProjectConfigData`, which
`BinaryFormatter`s itself into `Library/AddressablesConfig.dat`. It is therefore *not* committed:
a fresh checkout gets the default, which is index 0, which is `BuildScriptFastMode` — the one the
test suites need. `m_ActivePlayerDataBuilderIndex: 2` in the settings asset is the *player* build
script (packed), and is a different setting entirely.

**A group's remote path is not what the editor loads from.** `Minigame.Chests` points at
`http://localhost:8080/[BuildTarget]`, and both suites still pass with nothing serving it, because
Fast Mode never consults the group's load path at all. The remote paths are only exercised by a
packed build — which is why `ci/build-addressables.sh` exists and why running it once is the only
structural check that the group actually bundles as a unit.

**A label with nothing to fetch is not an error, but an unknown label is.** With the play mode script
on Use Asset Database, `AddressableAssetSettingsLocator` indexes labels as keys, so `minigame.chests`
resolves — to four locations with no dependencies, which is a download size of 0 and a download that
completes immediately. That is what lets the on-demand path run unchanged in both test suites and in
a build that shipped its content local. A label nobody authored still arrives as
`MissingAssetException`, the same as an unknown key.

**`Object.Destroy` is a logged error in edit mode**, so an edit-mode test that reaches
`MinigameContainer.End` after a view was instantiated fails on the log rather than on the behaviour.
`MinigameContainerContentTests` destroys the instance itself first; `End` against a live view belongs
to play mode.

---

## 7. Bugs found and fixed

1. **An unwired config document threw an engine exception, not a typed one.** `ChestsMinigameSO`
   read `_configDocument.text` before `ChestsMinigameConfig.Parse` could reject it, so the most
   common authoring mistake surfaced as `UnassignedReferenceException` in the editor and a plain
   `NullReferenceException` in a build. It now throws `GameConfigException` naming the asset.
2. **A failed or cancelled `BeginAsync` leaked whatever it had already loaded.** `End` returns early
   on `!_running`, and `_running` is set on the last line of `BeginAsync`, so a container whose view
   loaded and whose config load then threw held a handle nothing could ever release. `BeginAsync`
   now releases what it took and rethrows. `End` was deliberately *not* made unconditional, because
   "releases nothing when it never began" is a contract the teardown paths depend on.
3. **The provider's handle dictionary keyed on reference identity.** Described in section 4. Not
   reachable from the shipped game, which is the reason it survived review once: every caller
   happens to pass the same serialized instance to load and to release. Any caller that built a
   reference from a GUID would have got a silent no-op release and a permanent leak.

---

## 8. Test map — the fixtures this work added

`FakeAssetProvider` lives in `Tests/Common/` and is what keeps the suite fast. `AssetReference` has a
public GUID-string constructor, so tests need no real assets.

| EditMode fixture | What it protects |
|---|---|
| `ChestsMinigameConfigTests` | The chests minigame's own document: missing, empty, malformed, its three range rules, and a definition with no document wired |
| `MinigameContainerContentTests` | What `BeginAsync` loads and in what order, that the controller is configured before it is injected and injected once, that `End` releases both the view and the minigame's own content, that `End` on a container that never began releases nothing, that a failed load surfaces typed and releases what it had already taken, and the whole on-demand fetch: measured then downloaded for `OnDemand`, skipped for `Preload`, skipped for a blank label, skipped when the size is zero, typed and not running when the download fails |
| `MinigameContentPreloaderTests` | The boot-time half of the load policies: only `Preload` entries measured and fetched, every size gathered before the first byte, aggregate progress that never goes backwards, a blank label warned and skipped, nothing fetched when there is nothing to fetch, the typed failure propagating, and the token threaded through |
| `AssetHandleRegistryTests` | That the provider's bookkeeping has value semantics: a reference rebuilt from the same GUID takes what the authored one left, a different asset takes nothing, a sub-object is not its parent, every handle for one asset is kept, taking untracks, and taking something never loaded is safe. Also asserts, as a premise, that `AssetReference` still has no `Equals` of its own |
| `AddressablesContentSourceTests` | The four content sources against `FakeAssetProvider`: each asks for its own key, carries the provider's answer through untouched, surfaces a missing asset as `MissingAssetException`, and threads its cancellation token |
| `GameContentLoaderTests` | Every source read once, in order, cancellation threaded through, and a failure stopping the load rather than yielding half-built content |

| PlayMode fixture | What only play mode can prove |
|---|---|
| `AddressablesAssetProviderTests` | That the **real** provider translates what the **real** library throws, on all four routes: an unknown key, an unresolvable reference or an unauthored label arrives as `MissingAssetException`, a shipped key and a shipped reference both still load, a label with nothing left to fetch reports 0 and downloads without failing, and releasing a reference that was never loaded is safe. The sources are covered in edit mode against a fake, which by construction cannot prove this |
| `GameBootstrapperTests` | The boot flow end to end: the game scene opens parented to the loaded scope, the root scope survives it, every shipped key resolves through Addressables, both halves of the split inject the scene's objects, and the chests minigame really begins from the two references its descriptor names — which is the integration proof for the indirection itself |

`GameLifetimeScopeTests`, `MinigameManagerTests`, `CatalogTests`, `MinigameContainerLifecycleTests`
and `PopupManagerIntegrationTests` predate this work and changed shape in it; they are described in
the other file.

---

## 9. If you change something structural

The general rules are in the other file's section 9. These are the ones this work added.

- Loading a new asset: give it an address in the `Core` group, put the key on exactly one source
  class, and reach it through `IAssetProvider`. Do not name an Addressables API in the assembly you
  are working in; that is what the seam is for. The one thing that legitimately crosses the boundary
  is a serialized `AssetReference` field, and only on an asset that authors content of its own.
- Loading content that belongs to one minigame: put it in that minigame's group, label it, and name
  it from the definition asset with an `AssetReference`. Load it in `ConfigureControllerAsync` and
  release it in `ReleaseContent`, which are the two halves the container calls from `BeginAsync` and
  `End`. A direct object reference on the definition undoes the grouping silently — nothing fails,
  the bundle just comes back as a hard dependency of the catalog.
- Adding a minigame: six things, and only the first is code.
  1. Its own assembly and its own folder under `_Project/Minigames/`, with no reference from
     `Company.ChestGame.Gameplay`. The shell reaching a minigame by type again is the thing this
     layout exists to prevent. The assembly is always in the player — managed code cannot ship in a
     bundle — so this is the part a content update can never change.
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

---

## 10. Known gaps

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

**A start that is cancelled mid-load no longer leaks**, but the fix lives in the wrong-looking place.
`GameManager` still only records the container once `BeginAsync` returns; what closed the gap is
`BeginAsync` releasing its own partial work on the way out. That is the only place that can, and it
is worth knowing before anyone tries to "simplify" the `catch` away.

**The value-semantics half of the handle fix is unprovable through the seam.**
`AssetHandleRegistryTests` covers it directly, and a play-mode test that loads through one
`AssetReference` and releases through another built from the same GUID was written and then deleted:
under both the fixed and the broken code the observable behaviour through `IAssetProvider` is
identical — a released handle and a leaked one look the same from outside, and `ResourceManager`'s
only counter (`OperationCacheCount`) is `internal`. Extracting the registry was what made the fix
assertable at all.

**Nothing asserts the boot status label actually updates.** `IBootStatus` is registered and resolved
under test, and `BootStatusLabel` is three lines, but no test drives boot and reads the label back.

The gaps that predate this work — `GameManager` untested, `CurrencyWatcher` untested, the dormant
play-mode ordering test, `FakeGameClock`'s caveat, and `ICurrencyManager` leaking
`ResourceBankCallbacks` — are all still open and are described in the other file.

---

## 11. What is verified, and what is not

Stated plainly, because the gap matters more than the green suite does.

**Verified.** Both suites from a clean `ci-results/`: **166 EditMode + 32 PlayMode, exit 0.** 165 of
the EditMode tests are ours; the Addressables package ships one editor test of its own
(`AddressableAssets.DocExampleCode.TestStub.RequiredTest`) and Unity picks it up. Every new test was
checked by mutation. The content build was verified at bundle level:
`ServerData/Android/minigame.chests_assets_all_….bundle` (1,041,132 bytes) contains `ChestPrefab` and
`ChestsMinigame` and **no** `Core` assets, while `core_assets_all_….bundle` stayed local under
`Library/`.

**Not verified.**

- **No player session has been run across the boot-scene, Addressables or delivery work.** Everything
  is verified by test suite and by inspecting built bundles. Nobody has pressed Play and watched the
  game work.
- **The remote load path has never been exercised.** Fast Mode ignores group load paths entirely, so
  the suites pass with nothing serving `ServerData`. The outstanding check: run
  `ci/build-addressables.sh`, start `python3 -m http.server 8080 --directory ServerData`, play from
  `Boot.unity`, then **kill the server mid-session** and confirm `AssetLoadException` reaches a popup
  rather than hanging. Clearing the bundle cache and relaunching exercises the cold path. An Android
  build should confirm the chests group is absent from the APK. Launching `Game.unity` directly is
  expected to fail, and is worth confirming gives a clear message rather than a null.
- **`GameManager`'s two new behaviours** — the start button going non-interactable while a start is
  in flight, and a `ChestGameException` becoming a `ContentUnavailablePopup` — are asserted nowhere.

Remote download stays a documented manual check rather than a CI test: a fixture that starts a web
server is exactly the flakiness the project's testing agreements argue against.

---

## 12. Open decisions, awaiting Hiago

Raised during review, none ruled on. **Do not act on these without asking.**

1. **Split `IAssetProvider` in two**, so assemblies that only ever load by key stop being forced to
   reference Addressables. Nine asmdefs reference it now; one did before, and two of the nine
   (`Config` and `Popups`) gain nothing from it. Trade-off in section 5.
2. **The unused `TView` type parameter** on `MinigameBase<TController, TView, TMinigame>`. It
   constrains nothing now that the view is an `AssetReferenceGameObject`.
3. **The untyped `NullReferenceException`** on a right-GUID-wrong-prefab, described in section 10.
4. **Filter the Addressables package's own test** out of `ci/run-tests.sh` via `-assemblyNames`, so
   the EditMode count is 165 rather than a 166 that needs explaining.

Also open, and deliberately kept out of scope so it would not muddy these diffs: **`ICurrencyManager`
leaks `ResourceBankCallbacks<CurrencyType>`**, forcing every consumer — including
`Company.ChestGame.UI` — to reference the vendored library. It is a real smell and deserves its own
pass.

---

## 13. Picking this up

1. **Do the manual verification in section 11.** It is the single largest gap: everything about
   remote delivery is currently believed rather than observed.
2. **Ask before starting anything in section 12.** The protocol in section 2 is still in force.
3. Read the other context file too. Most of what it records about seams, testing discipline and Unity
   behaviour is still exactly right, and this work leaned on all of it.
