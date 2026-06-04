# `.claude/` — Claude Code project configuration

Claude Code equivalent of `.codex/environments/environment.toml`. Committed so a
freshly created git worktree picks it up automatically.

## Environment setup (codex `[setup]` parity)

`SessionStart` hook in [`settings.json`](settings.json) runs
[`scripts/setup-worktree.ps1`](scripts/setup-worktree.ps1) every time a session
starts. The script is **idempotent** — heavy steps are guarded by their output
artifacts, so it only does real work on a fresh worktree:

- **Frontend deps** — `pnpm -C frontend install --frozen-lockfile` when
  `frontend/node_modules` is missing. (Always on; needed for typecheck/test/build/preview.)
- **Backend .NET restore** — **opt-in** (slow; not needed for frontend work).
  Enable full parity with `$env:NERV_SETUP_BACKEND = '1'`, or run `/setup-env` on demand.

## Slash commands (codex `[[actions]]` parity)

| Command | Action |
|---|---|
| `/setup-env` | Full env setup (frontend deps + backend `dotnet restore`). |
| `/frontend-gate` | Frontend quality gate: `typecheck` + `test` + `build`. |

Add more under [`commands/`](commands/) — each `*.md` is a prompt run when invoked.

## Mapping to codex

| codex `environment.toml` | Claude Code |
|---|---|
| `[setup].script` | `settings.json` → `hooks.SessionStart` → `scripts/setup-worktree.ps1` |
| `[[actions]]` named commands | `commands/*.md` slash commands |
| (launching apps for preview) | `.claude/launch.json` is read by the preview tool, not the core CLI; create on demand. |

## Notes

- `settings.local.json` is per-developer local state (permissions etc.) and is **not**
  committed — do not put shared config there.
- `setup-worktree.ps1` is harness config outside the governed `scripts/` tree, so it may
  call `pnpm` / `dotnet` directly (the `scripts/` governance rules do not apply here).
