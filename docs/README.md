# Docs

Start with [architecture.md](architecture.md) for the map, then read whichever file covers the area
you are about to change. All context and reasoning lives here; code comments are API documentation
only. Future work goes in [WIP.md](WIP.md). The full policy is in [CLAUDE.md](../CLAUDE.md).

## Reference

| File | Covers |
|---|---|
| [architecture.md](architecture.md) | Assemblies, boot order, game flow, engine seams, config, catalogs, popups, exceptions, currency |
| [asset-loading.md](asset-loading.md) | `IAssetProvider`, the two load routes and their lifetime rules, handle tracking, failure translation |
| [content-delivery.md](content-delivery.md) | Addressable groups, local vs remote, load policies, timeouts, building and serving content |
| [minigames.md](minigames.md) | The minigame framework contract, container lifecycle, and the chests implementation |
| [pooling.md](pooling.md) | `IPrefabPool<T>` and its implementations, `PoolFactory`, the pool race demo, and what the pooling tests prove |
| [saving.md](saving.md) | The `ISaveService` seam, the envelope and its value-exact round trip, versioning and the legacy import, the stores, codecs and protectors, the selection enums, async writes and coalescing, the save model, and the inspector |
| [testing.md](testing.md) | The two suites, what belongs in each, running them, CI |
| [design-decisions.md](design-decisions.md) | Why the project landed this way |
| [assistant-setup.md](assistant-setup.md) | Shared Claude and Codex instructions and the machine-local configuration layout |
| [WIP.md](WIP.md) | Planned features, phases, pending changes, known gaps and TODOs |

## Session notes

Working context from the development passes that produced the current shape: approaches that were
tried and replaced, and Unity behaviour that cost time. Their known gaps and open decisions now live
in [WIP.md](WIP.md).

- [context/assemblies-and-tests.md](context/assemblies-and-tests.md): how the codebase became
  testable, covering the assembly definitions, the test suite, and the seams that testing revealed
  were missing.
- [context/self-contained-minigames.md](context/self-contained-minigames.md): how a minigame became a
  unit of content delivery, covering the config split, its own assembly, the boot scene, and
  Addressables.

Read the reference files for what the architecture is, and the session notes before changing anything
structural.
