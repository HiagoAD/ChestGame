# Content delivery

How the game's content is grouped, where each group is built and loaded from, when it arrives, and
what happens when it does not. The loading seam itself is in [asset-loading.md](asset-loading.md).

## Two groups, delivered differently

| Group | Build path | Load path | Why |
|---|---|---|---|
| `Core` | `Local.BuildPath` | `Local.LoadPath` | Ships inside the player |
| `Minigame.Chests` | `Remote.BuildPath` | `Remote.LoadPath` | Fetched over the wire |

Those four are profile variables, not paths. `Remote.BuildPath` resolves to
`ServerData/[BuildTarget]` and `Remote.LoadPath` to `http://localhost:8080/[BuildTarget]`.

`Core` is local deliberately. The game config has to parse before anything can render, and the thing
that reports a failed download is a popup, whose prefab and parent are themselves content. A remote
`Core` would mean a game that cannot tell you why it could not start.

`BuildRemoteCatalog` is on, so the catalog is fetched from the same server as the bundles. That is
what makes a content update a content update: change the chests config or its art, run the content
build, upload `ServerData/`, and the shipped app picks it up with no store release.

## Where the content actually sits

Shared content sits in `_Project/Content`, addressable under the single local `Core` group. There is
no `Resources` folder of ours any more.

Two popup prefabs are worth knowing about before you go looking for them, because neither is where
the folder layout suggests. `RewardReceivedPopup.prefab` is a `Core` entry addressed
`Popups/RewardReceivedPopup`, and it lives at `Assets/_Project/Prefabs/UI/Rewards/` with the rest of
the UI prefabs rather than under `_Project/Content`. `ContentUnavailablePopup.prefab` is one level up
at `Assets/_Project/Prefabs/UI/`, and it is not a group entry at all; it reaches the bundle as a
dependency of `PopupList.asset`, which is. Group membership decides what ships where. The folder is
not the authority on it, and only the group is.

A minigame's own content sits with the minigame and has a group of its own. `Minigame.Chests` has
four entries and all of them carry the label `minigame.chests`.

### Every address the game ships

The source classes are each "the only place that knows a key", so here are the keys they know. The
five `Core` addresses:

| Address | Asset | Who asks for it |
|---|---|---|
| `GameConfig` | `_Project/Content/GameConfig.json` | `AddressablesGameConfigSource` |
| `Minigames/MinigameList` | `_Project/Content/Minigames/MinigameList.asset` | `AddressablesMinigameListSource` |
| `Popups/PopupList` | `_Project/Content/Popups/PopupList.asset` | `AddressablesPopupListSource` |
| `Popups/PopupParent` | `_Project/Content/Popups/PopupParent.prefab` | `AddressablesPopupParentSource` |
| `Popups/RewardReceivedPopup` | `_Project/Prefabs/UI/Rewards/RewardReceivedPopup.prefab` | nothing by key; `PopupCatalog` holds it |

The four `Minigame.Chests` addresses, all labelled `minigame.chests`:

| Address | Asset |
|---|---|
| `Minigames/Chests/ChestElement` | `ChestPrefab.prefab` |
| `Minigames/Chests/View` | `ChestsMinigame.prefab` |
| `Minigames/Chests/Sprite` | `chests.png` |
| `Minigames/Chests/Config` | `ChestsMinigameConfig.json` |

Only the first four `Core` addresses are named in code. Everything else is reached through an
`AssetReference` that carries a GUID, which is why the chests addresses appear nowhere in C#.

### The play mode script is not committed

Both test suites run against the asset database with no content build. That is a machine-local
setting rather than a committed one: the play mode script lives in `Library/AddressablesConfig.dat`,
and `Library/` is gitignored, so a fresh clone gets whatever default the package writes on import,
and every machine sets it independently. If a suite starts failing to resolve keys, check
Window, Asset Management, Addressables, Groups, Play Mode Script and set it to Use Asset Database
(fastest).

Do not read this off `AddressableAssetSettings.asset`. The `m_ActivePlayerDataBuilderIndex: 2` in
that file selects the **player build** script (packed mode) and is a different setting entirely.
Mapping it to the play mode script leads to the wrong conclusion that playing or testing requires a
prior content build.

## When content arrives

How a minigame's content arrives is authored on its own descriptor, as a `MinigameLoadPolicy` next to
the label naming that content. The rule belongs to the minigame rather than to whatever code later
acts on it.

`Preload` content is fetched during boot, before the player can press anything.
`MinigameContentPreloader` walks the catalog, sums the download sizes of every preloaded label, and
downloads them reporting one aggregate progress figure to the boot scene's status label.

`OnDemand` content is fetched by `MinigameContainer.BeginAsync`, the one moment the game knows the
content is about to be needed. It asks for the size first and downloads only if there is something to
download, so the ordinary case, content already cached or a build that shipped it local, costs one
query and no wait. The chests minigame is `OnDemand`.

A minigame set to preload but naming no label is skipped with a warning, the same policy the catalogs
apply to a blank id. That rule is stated once, on `MinigameBaseSO.TryGetContentLabel`, because both
delivery paths need it and they used to answer it differently: the preloader warned while the
on-demand fetch skipped in silence, so whether an unauthored slot was visible depended on which
policy it happened to be paired with.

`GameManager` makes the start button non-interactable while a start is in flight and turns a failed
one into a `ContentUnavailablePopup` rather than leaving a button that silently does nothing. There
is no progress bar in the game scene; boot is the only place a download is narrated.

## Progress reporting

The preloader reports aggregate progress, not per label. A player watching a bar does not care that
the work is split by minigame, and a bar that restarts at zero for every label reads as a bug. Sizes
are gathered first for exactly that reason: the share each label is worth cannot be known until the
whole total is. `AggregateProgress` maps one label's own 0..1 onto its slice, and the preloader
reports again on completion of each label rather than trusting the inner reporter to have finished at
exactly its own 1.

The preloader reports a number because a number is all it knows. Wording is the shell's business, so
`GameBootstrapper.DownloadStatus` turns the fraction into the line the boot scene shows.

## Timeouts

A stalled download is not a failed one. Nothing throws, nothing returns, and the button the shell
disabled on the way in stays disabled for the rest of the session with nothing on screen to say why.
Both delivery paths put a deadline on the wait and both use 90 seconds.

Ninety is deliberately longer than anything Addressables bounds on its own. A bundle request gives up
after fifteen seconds in which not one byte arrived and is retried twice, so the worst bounded failure
is forty-five seconds and the package's own typed error wins that race, reaching the player with a
better reason than "it timed out". The deadline exists for the stalls the package does not bound at
all.

The budget is `protected virtual` on both classes rather than authored on a config document. A
minigame whose payload justifies a longer wait widens it on its own container subclass, which every
minigame already has, and a test shortens it to milliseconds. No tuning knob appears on a document
that ships to players, and the framework grows no config surface it does not otherwise need.

The preloader bounds each label separately rather than the whole walk. A wall-clock budget for the
entire preload would make boot fail for having more content rather than for being stuck, so every
minigame added would bring the game closer to a spurious timeout. Bounding each label means the budget
measures the thing that is actually wrong, one fetch that stopped answering, and a legitimately large
preload is never killed for its size. The worst case grows with the number of labels, which is the
honest trade: it is bounded, and every step is reported to the player.

### Which token fired

Both paths link the deadline to the caller's token, so a scene going away mid-fetch ends the wait
immediately instead of sitting out the rest of the budget. That makes the two ends indistinguishable
at the catch site, and the caller's token is the only one that can be asked about after the fact.

The caller cancelling means the scene is going away, or the application is quitting, and there is
nobody left to tell. It stays an `OperationCanceledException` and travels out untouched. Only the
deadline firing on its own becomes `ContentDownloadTimeoutException`, because only then is a player
still sitting in front of a button waiting for an answer that is never coming.

Both at once counts as the caller's. A teardown that happens to coincide with the deadline is still a
teardown, and popping a message onto a scene being unloaded would be worse than saying nothing.

## Code is always local

C# assemblies cannot ship inside an AssetBundle. Unity builds managed code into the player, not into
content, so `Company.ChestGame.Minigame.Chests` is in every build whether or not its content ever
arrives.

What the split buys is that the assets travel instead: two prefabs, a sprite and a config document,
nearly all of the megabyte, and they travel without an app update. A minigame that could be added
after ship would need scripting to be data too, which is a different project.

## Building and serving

`ci/build-addressables.sh` runs the content build. `AddressablesContentBuild` is its entry point, and
it lives in an editor-only assembly so nothing there can be referenced from game code by accident. It
exits the editor itself rather than letting batch mode decide, because `-executeMethod` otherwise
reports a thrown exception and a clean return with the same code.

To serve the built content locally, from the repo root:

```bash
python3 -m http.server 8080 --directory ServerData
```

That is exactly what the profile's `Remote.LoadPath` points at. A device on the same network needs that
host swapped for the machine's LAN address, and a real release needs it swapped for wherever the
content is actually hosted.

With nothing serving it, the game still boots, because `Core` is local, and starting the chests
minigame fails into a `ContentUnavailablePopup`. That is the behaviour the path exists to give.
