# Architecture

The map of the codebase: what the assemblies are, how the game boots, and where each piece of the
shared machinery lives. Read this first. The deeper notes on asset loading, delivery, the minigame
framework and the test suite are in the sibling files listed in [README.md](README.md).

## Assembly layout

Every script sits inside an assembly definition, so nothing compiles into `Assembly-CSharp`. The
split is what keeps the dependency directions honest: a reference that would create a cycle fails to
compile instead of quietly working.

```
Company.ChestGame.Common      _Project/Scripts/Common/     leaf: engine seams, exceptions, catalog policy
Company.ChestGame.Pooling     _Project/Scripts/Pooling/    leaf: the prefab pool seam and its four strategies
Company.ChestGame.Assets      _Project/Scripts/Assets/     the only assembly that calls Addressables
Company.ChestGame.Config      _Project/Scripts/Config/
Company.ChestGame.Currency    _Project/Scripts/Currency/
Company.ChestGame.Popups      _Project/Scripts/Popups/
Company.ChestGame.Minigame    _Project/Scripts/Minigames/  the framework, no minigame in it
Company.ChestGame.Rewards     _Project/Scripts/Rewards/
Company.ChestGame.Gameplay    _Project/Scripts/Gameplay/   the shell: GameManager and nothing else
Company.ChestGame.UI          _Project/Scripts/UI/
Company.ChestGame.Core        _Project/Scripts/Core/       composition root, both LifetimeScopes
Company.ChestGame.Editor      _Project/Scripts/Editor/     content build entry point

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
nothing about chests, minigames or UI: it is a seam over where an instance comes from, with a
hand-rolled pool, a reparenting one, a wrapper over the engine's `ObjectPool` and an
`Instantiate`/`Destroy` baseline behind it. It is deliberately synchronous and frame-agnostic -
spreading a large fill over frames is the caller's job, through `FrameBudgetedLoop`. The two meet at
the call site rather than in each other, which is why `Common` has no pooling reference and `Pooling`
has no UniTask one.

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

## Boot

The game starts in `Scenes/Boot.unity`, not in the game scene. Opening `Game.unity` directly will not
work: its scope expects a parent that only the boot scene builds.

`GameLifetimeScope` lives in the boot scene, registers everything that needs no asset, and survives
the scene load through `DontDestroyOnLoad`. `GameBootstrapper` then does four things, in the order
the whole design rests on:

1. `GameContentLoader` reads every source.
2. `RegisterLoadedServices` builds a child scope from what came back.
3. `MinigameContentPreloader` fetches whatever asked to arrive up front.
4. `Game.unity` opens, and its own `GameSceneLifetimeScope` is parented to that scope through
   `LifetimeScope.EnqueueParent`.

`GameSceneLifetimeScope` is the game scene's scope and it registers nothing: its `Configure` is
empty. It exists to be the scene's injection root, so `GameManager` and `CurrencyWatcher` are
auto-injected from a scope that can see both halves of the registration split. The root scope cannot
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
first.

### Registration, in two halves

`GameLifetimeScope` keeps its registration lists apart from `Configure` so tests can assert against
the real composition root instead of a hand-copied duplicate. `GameLifetimeScopeTests` runs against
`RegisterCoreServices` and `RegisterLoadedServices` themselves, so dropping a registration from the
composition root fails there.

`RegisterCoreServices` holds everything that can be built the moment the container is: the two engine
seams, `IAssetProvider`, the four content sources, the save handler, `CurrencyManager`,
`GameContentLoader` and the bootstrapper. That is what lets the boot scene resolve the loader and the
bootstrapper before a single file has been read.

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

### Telling the player what boot is doing

Each step reports through `IBootStatus`. An interface rather than a label, because the bootstrapper is
a plain class and reaching a TextMeshPro component from it would put a scene object in the one part
of booting that has none. `BootStatusLabel` is the boot scene's implementation and holds the label
that scene already had. `SilentBootStatus` is what gets registered when there is no label: a container
built by a test, or a boot scene whose slot was never wired. Registering a silent one rather than
nothing keeps the bootstrapper free of a null check at every call site.

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
than left to the garbage collector with live subscriptions.

There is no persistence in the minigame itself. Attempts reset on every new game. Currencies persist,
including between sessions.

## Config pipeline

Config is two documents, not one, because the values had two different owners.

`Content/GameConfig.json` holds what the whole game shares, currently the two reward amounts, and
reaches the game through three steps with one job each:

- `IGameConfigSource` fetches the raw document asynchronously. `AddressablesGameConfigSource` is the
  only class that knows the key, and it goes through `IAssetProvider` to turn that key into bytes.
- `LocalJsonGameConfig` parses and validates it. It loads nothing itself, and takes the document
  rather than the source, so parse-and-validate stays a synchronous constructor.
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

## Popups

`PopupBase<TPopup, TData>` and `PopupManager` are a typed popup framework: popups receive
strongly-typed data on initialization rather than a stringly-typed dictionary. `PopupManager` takes an
`IPopupCatalog` and an `IPopupParentProvider` rather than loading anything itself, which leaves it
doing only what it is about: picking a prefab, picking a parent, handing over the data.

`PopupParentProvider` creates the shared `DontDestroyOnLoad` canvas lazily, on first use, from a
prefab that was handed to it already loaded. Resolving `IPopupManager` therefore has no side effects,
which matters because a `DontDestroyOnLoad` object built during resolution would leak into every
consumer of the container, tests included. There is a test pinning exactly that.

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
wrong), `GameConfigException`, `MinigameNotFoundException`, `MinigameAlreadyRunningException` and
`PopupNotFoundException`.

No bare `throw new Exception` remains in game code. A test asserting "this throws" should not be
satisfied by an unrelated `NullReferenceException` from somewhere inside the call, and a caller
should be able to tell a missing asset from a malformed one.

Two typed failures sit deliberately **outside** that base: `PoolException` and `FrameBudgetException`,
both under `InvalidOperationException`. Being under `ChestGameException` is not a label in this
project, it is behaviour — `GameManager` catches exactly that base, turns whatever it caught into a
content-unavailable popup and treats it as handled, on the understanding that anything outside it is
a bug and is left to blow up where it can be seen. Everything those two types report is a wiring
mistake: an unassigned prefab slot, a holder that was never built, a view that was never injected.
Reporting one of those as a delivery failure would tell a player their connection is bad and swallow
the bug that caused it. `PrefabPoolTests` and `FrameBudgetedLoopTests` each pin that with an
`IsNotInstanceOf<ChestGameException>`, so a later tidy-up of the hierarchy cannot quietly undo it.

`InvalidCatalogException` carries its offending key as `object`, because the catalogs index by
different things: a container type for the type-keyed lookups, an authored string id for the
id-keyed one. Type keys keep their original wording; anything else is quoted in the message, because
a blank-looking id is otherwise invisible.

## Currency and rewards

`CurrencyManager` wraps [Resource Bank](https://gitlab.com/tn-asset-library/resource-bank), providing
events (`OnCurrencyChanged`, `OnCurrencyCollected`, `OnCurrencySpent`) and persistence. Add
currencies by extending the `CurrencyType` enum. It takes an
`IResourceBankSaveHandler<CurrencyType>` as its only constructor argument, registered in the scope,
so a test can hand it an in-memory save instead of PlayerPrefs.

The class also marks the places a production game would hook up analytics and a currency purchase
flow, both left as commented examples.

One simplification against the library's own example: `ResourceBank.ResourceIdMap`, which maps enum
values to strings, was dropped. See
`Assets/AssetLibrary/ResourceBank/Examples/CurrencyManager/CurrencyManagerExample.cs` for the full
version.

`CurrencyWatcher` subscribes to the events and updates TextMeshPro labels in the UI. `RewardsManager`
picks a random currency reward from the config values and shows a `RewardReceivedPopup`.
