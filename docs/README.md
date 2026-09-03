# Docs

Start with [architecture.md](architecture.md) for the map, then read whichever file covers the area
you are about to change. Code comments are kept to what would otherwise get "fixed" into a bug; the
reasoning lives here.

## Reference

| File | Covers |
|---|---|
| [architecture.md](architecture.md) | Assemblies, boot order, game flow, engine seams, config, catalogs, popups, exceptions, currency |
| [asset-loading.md](asset-loading.md) | `IAssetProvider`, the two load routes and their lifetime rules, handle tracking, failure translation |
| [content-delivery.md](content-delivery.md) | Addressable groups, local vs remote, load policies, timeouts, building and serving content |
| [minigames.md](minigames.md) | The minigame framework contract, container lifecycle, and the chests implementation |
| [saving.md](saving.md) | The `ISaveService` seam, the envelope and its byte-exact round trip, versioning, the stores, the selection enums, and the factory |
| [testing.md](testing.md) | The two suites, what belongs in each, running them, CI |
| [design-decisions.md](design-decisions.md) | Why the project landed this way |

## Session notes

Working context from the development passes that produced the current shape: approaches that were
tried and replaced, Unity behaviour that cost time, and the known gaps. Both are kept current rather
than frozen.

- [context/assemblies-and-tests.md](context/assemblies-and-tests.md): how the codebase became
  testable, covering the assembly definitions, the test suite, and the seams that testing revealed
  were missing.
- [context/self-contained-minigames.md](context/self-contained-minigames.md): how a minigame became a
  unit of content delivery, covering the config split, its own assembly, the boot scene, and
  Addressables.

Read the reference files for what the architecture is, and the session notes before changing anything
structural.
