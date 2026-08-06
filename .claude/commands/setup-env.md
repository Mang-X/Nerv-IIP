---
description: Full worktree environment setup (frontend deps + backend .NET restore)
---

Run the full environment setup for this worktree (codex `[setup]` parity), from the repo root, and report the result of each step:

1. Agent skills + frontend deps: `pwsh -NoProfile -File scripts/setup-worktree.ps1` — the same script the SessionStart hook runs. It mirrors `.agents/skills` plus the `.claude/skills` link layer from the main worktree (installing via `npx skills experimental_install` only when the main worktree has none), then runs `pnpm -C frontend install --frozen-lockfile --config.confirmModulesPurge=false`.
2. Backend restore: `dotnet restore backend/Nerv.IIP.sln`
3. Connector Host restore: `dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln`

Skip any step whose artifacts already exist (non-empty `.agents/skills`, frontend `node_modules`, backend `obj/project.assets.json`). Report clearly whether each step ran or was skipped, and surface any failures with their output.
