---
description: Run the frontend quality gate (typecheck + test + build)
---

Run the Nerv-IIP frontend quality gate from the repo root and report pass/fail for each step with any failure output:

1. `pnpm -C frontend typecheck`
2. `pnpm -C frontend test`
3. `pnpm -C frontend build`

Note: `pnpm -C frontend check` / `lint` / `fmt` may fail on pre-existing out-of-scope issues — see AGENTS.md "Known Baseline Caveats". Only report regressions introduced by the current change.
