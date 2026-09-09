# ChestGame

Project made in Unity 6000.3.11f1

## 0. What is this?

This is not a game that you would love to play, this is a project you would love to work on.

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

## 1. Overview

Game code lives under `Assets/_Project`, tests under `Assets/Tests`, and the vendored copy of
Resource Bank under `Assets/AssetLibrary`. Most of the content the game loads by key lives under
`_Project/Content`. Dependencies are registered as singletons in
`Assets/_Project/Scripts/Core/GameLifetimeScope.cs`.

Libraries:

- [UniTask](https://github.com/Cysharp/UniTask): async/await for Unity with no allocations
- [VContainer](https://github.com/hadashiA/VContainer): dependency injection
- [Resource Bank](https://gitlab.com/tn-asset-library/resource-bank): persistent currency management
- [Newtonsoft.Json](https://www.newtonsoft.com/json): JSON deserialization for config loading
- [Addressables](https://docs.unity3d.com/Packages/com.unity.addressables@2.9/manual/index.html):
  content loading by key or by authored reference, kept behind a seam of the project's own
- [Unity Test Framework](https://docs.unity3d.com/Packages/com.unity.test-framework@1.6/manual/index.html):
  NUnit test runner for both suites

Every script sits in an assembly definition, so nothing compiles into `Assembly-CSharp` and a
reference that would create a cycle fails to compile. The game boots from `Scenes/Boot.unity`, which
loads all content before building the scope the game scene runs under. Assets are reached through one
seam, `IAssetProvider`, and only `Company.ChestGame.Assets` calls Addressables. The chests minigame is
an assembly and an addressable group of its own, and the shell starts it by authored id without
referencing it.

## 2. Documentation

The reasoning lives under [docs/](docs/README.md), split by area. Code comments are kept to what would
otherwise get "fixed" into a bug.

| File | Covers |
|---|---|
| [architecture.md](docs/architecture.md) | Assemblies, boot order, game flow, engine seams, config, catalogs, popups, exceptions, currency |
| [asset-loading.md](docs/asset-loading.md) | `IAssetProvider`, the two load routes and their lifetime rules, handle tracking, failure translation |
| [content-delivery.md](docs/content-delivery.md) | Addressable groups, local vs remote, load policies, timeouts, building and serving content |
| [minigames.md](docs/minigames.md) | The minigame framework contract, container lifecycle, and the chests implementation |
| [saving.md](docs/saving.md) | The `ISaveService` seam, the envelope and its byte-exact round trip, versioning, the stores, the selection enums, and the factory |
| [testing.md](docs/testing.md) | The two suites, what belongs in each, running them, CI |
| [design-decisions.md](docs/design-decisions.md) | Why the project landed this way |

Working notes from the development passes, including approaches that were tried and replaced and the
known gaps, are in [docs/context/](docs/context/). Read those before changing anything structural.

## 3. Build and run

Open the project in Unity 6000.3.11f1 or compatible.

To play in the editor, open `Assets/_Project/Scenes/Boot.unity` and press Play. Starting from
`Game.unity` will not work, because the boot scene is what loads the content and builds the scope it
needs.

To run the tests, with the editor closed:

```bash
ci/run-tests.sh             # both suites
ci/run-tests.sh EditMode    # one of them
```

See [testing.md](docs/testing.md) for what each suite covers.

To build for Android, switch the target platform and build the content first:

```bash
ci/build-addressables.sh
```

The editor menu equivalents are Window, Asset Management, Addressables, Groups, Build, New Build,
Default Build Script, or Build, Addressables Content from the menu bar. Then follow the standard
player build procedure. This is no longer optional the way it was while both groups were local:
`Minigame.Chests` is remote, so its bundle and the remote catalog are written to
`ServerData/[BuildTarget]/` and are not part of the player at all.

To serve that content locally, from the repo root:

```bash
python3 -m http.server 8080 --directory ServerData
```

That is what the profile's `Remote.LoadPath` (`http://localhost:8080/[BuildTarget]`) points at. A
device on the same network needs that host swapped for the machine's LAN address, and a real release
needs it swapped for wherever the content is actually hosted. With nothing serving it the game still
boots, because `Core` is local, and starting the chests minigame fails into a
`ContentUnavailablePopup`.

The project was validated on Editor and Android build, targeting 1080x1920 Portrait, with Landscape
validated to be working. No further specific instructions are needed.
