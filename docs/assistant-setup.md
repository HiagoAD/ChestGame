# Assistant setup

`AGENTS.md` is the shared project instruction file. `CLAUDE.md` is a relative
symlink to it, so both assistants read the same documentation policy. Edit
`AGENTS.md` when changing that policy.

On Hiago's machine, `~/.agents/` holds the shared global instructions, agent role
definitions and review workflows. Claude's existing instruction, agent and command
paths link there. Codex's global instructions link there too; its agent adapters
and workflow skills read those same definitions. See `~/.agents/README.md` for the
layout and maintenance details. These home-directory settings are machine-local
and are not installed by cloning this repository.

Each tool keeps its native model and permission settings in its own configuration
file. The shared files describe work; the native configuration selects the runtime.
Start a new session after changing settings or adding skills.
