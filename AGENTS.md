# ChestGame

## Documentation policy

This applies to the main session and to every agent working in this repo, and it takes precedence
over any global or project agent definition. Where an agent's instructions say
to match the comment density of neighbouring code, to leave a warning comment in code, or anything
else that conflicts with the rules below, follow the rules below. The existing code predates this
policy and is heavily commented, so it is not the style to match.

- **`docs/` holds all context and reasoning.** Why something is shaped the way it is, what was tried
  and replaced, the traps, the trade-offs. Put it in the `docs/` file that covers the area (see
  [docs/README.md](docs/README.md) for the map). If no file covers it, add one named for the area and
  list it in the index.
- **Code comments are API documentation only.** XML doc comments (`///`) on types and members,
  stating the contract a caller needs: what it does, parameters, return value, exceptions thrown,
  ownership, lifetime, threading, and constraints on use. A `<remarks>` line may name the `docs/` file
  that holds the reasoning. No inline `//` comments carrying rationale, history or narration.
- **Future work goes in [docs/WIP.md](docs/WIP.md), nowhere else.** Planned features, phases,
  pending changes, known gaps, TODOs. Never in a code comment, and not in the reference docs, which
  describe what the code does now.

Scope: new and changed code follows this. Existing comments that break it are not grounds for
rewriting code a change does not otherwise touch; note them for a separate pass instead.
