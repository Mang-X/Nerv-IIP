---
description: Run the frontend quality gate (check + typecheck + test + build)
---

Run the Nerv-IIP frontend quality gate from the repo root and report pass/fail for each step with any failure output:

1. `pnpm -C frontend check`
2. `pnpm -C frontend typecheck`
3. `pnpm -C frontend test`
4. `pnpm -C frontend build`
