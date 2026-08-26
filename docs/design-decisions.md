# Key design decisions

Why the project landed the way it did. The mechanics of each are in the other files under
[docs/](README.md); this is the argument.

## 1. Library adoption

UniTask eases async handling, VContainer structures dependency management, and Resource Bank speeds up
currency system development. Chosen to increase reliability and cut boilerplate.

## 2. Parallel async tasks for chest opening

In `ChestsMinigameController`, the opening state is split into two concurrent UniTasks: one updating
the progress slider every frame, the other awaiting the delay timer. This shows how to handle multiple
async operations running in parallel while also giving visual feedback of the chest state.

## 3. Engine seams over the clock and the random source

Everything asynchronous reaches the player loop through `IGameClock`, and everything random through
`IRandomProvider`. That is what makes the chest flow testable at all: a suite that had to wait on real
timers would be slow, and one racing a real stopwatch would be flaky. Both are registered in the scope
alongside everything else, so the game gets the real ones and only tests substitute.

## 4. Fetching split from parsing in the config

`IGameConfigSource` fetches the document, `LocalJsonGameConfig` parses and validates it. Swapping the
local JSON for a real remote config touches one registration and no parsing rules, and it makes the
failure cases (missing document, malformed payload, out-of-range values) reachable from a unit test.

The document is split by owner as well. Values only one minigame cares about live in that minigame's
own document, parsed by that minigame. A shared config that every feature bolts its fields onto grows
into a god-object, and that is the thing that stops a feature from ever being self-contained.

## 5. Generic minigame framework

`MinigameBase<TController, TView, TMinigame>` uses generics and ScriptableObject definitions so that
adding a minigame modifies no framework code and no existing file except the authoring list. That is
the property being bought, and it is not the same as the job being small: a new minigame is its own
assembly, four new types, a group with two schemas, and a label that has to match the descriptor by
hand. The full list is in [minigames.md](minigames.md). Teardown is part of it: `MinigameContainer.End()` disposes the controller, destroys the
view, releases what `BeginAsync` loaded, and is safe to call unconditionally.

A definition names its content instead of holding it. `AssetReference` serializes as a GUID rather
than as a dependency, so the descriptor a catalog holds for every minigame costs nothing to load, and
a minigame's bundle is pulled in only when that minigame actually starts. It also forces the loading
to move: nothing about a reference resolves synchronously, so `Begin` became `BeginAsync` and the
configure-before-inject contract moved into it intact.

## 6. Typed popup system

`PopupBase<TPopup, TData>` ensures each popup receives strongly-typed data at initialization, avoiding
stringly-typed or untyped dictionaries.

## 7. Authoring data separated from lookups

The `*ListSO` assets hold what the inspector holds. The catalogs turn that into a usable lookup and
decide what counts as unusable. The `Addressables*Source` classes know the keys. Beyond keeping each
piece small, it means a catalog can be built in a test from a plain list, with no asset and no content
catalog.

## 8. An exception type per failure

Every failure the game reports derives from `ChestGameException`. A caller can tell a missing asset
from a malformed one, and a test asserting a specific failure cannot be accidentally satisfied by a
`NullReferenceException`.

## 9. Prize location calculated per run

The prize chest is determined at runtime on each attempt rather than stored in memory, to avoid being
discoverable through memory inspection. Unrealistic for a production game, but it works as a
demonstration. The odds and the off-by-one that hides in them are in
[minigames.md](minigames.md).

## 10. One assembly calls the loading technology

Every asset goes through `IAssetProvider`, and `Company.ChestGame.Assets` is the only assembly that
calls Addressables. The package's own exception types stop at that boundary and become
`MissingAssetException` and `AssetLoadException`. Same point the source classes make about paths, one
level up: the day this game loads content some other way, exactly one assembly changes.

This is weaker than it was. It used to be that no other assembly referenced the package either, and
authored `AssetReference` fields ended that. A serialized reference needs the type at compile time,
and so does any call site that has to resolve an overload naming it. The reference is the mechanism
that keeps a minigame's content out of the descriptor, so the narrower invariant was the thing that
had to give.

## 11. A group per minigame, and delivery authored on the descriptor

`Minigame.Chests` is a group of its own rather than a folder inside `Core`, which is what lets that
content be built, labelled, bundled and shipped as one unit. The descriptor carries the label and a
`MinigameLoadPolicy`, so how a minigame's content arrives is a property of that minigame rather than a
branch in whatever code fetches it. Adding a second minigame that preloads while the chests one stays
on demand is two authored fields and no code.

The group is also the only honest unit of measurement: a download size and a progress figure are both
per label, and a label is what a group's entries share.

## 12. Remote content, local core

`Minigame.Chests` loads over HTTP and `Core` does not. The asymmetry is deliberate and is the argument
for where the line sits. The config has to parse before anything renders, and a failed download is
reported by a popup, which is itself content. Everything needed to say "this did not work" ships inside
the player; everything that is only needed once the player asks for it does not.

`BuildRemoteCatalog` follows from the same decision. Content that can change without an app update is
only useful if the catalog describing it can change too.

## 13. Deadlines on every fetch that can stall

A download that stalls never fails, so nothing throws and nothing returns. Both delivery paths bound
the wait and turn only a deadline that fired on its own into a typed failure the player is told about.
Caller cancellation stays cancellation. The reasoning, including why the preloader bounds each label
rather than the whole walk, is in [content-delivery.md](content-delivery.md).
