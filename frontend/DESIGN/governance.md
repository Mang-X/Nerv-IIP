# Governance — Nerv-IIP Console Design System

## Ownership

The `@nerv-iip/ui` package (`frontend/packages/ui`) is the single source of truth
for all UI primitives **and** for design tokens (`packages/ui/src/styles/theme.css`).
Application code in `frontend/apps/*` never owns primitive component logic and never
defines token values.

## Never modify原版 (hard rule)

Base shadcn-vue primitives are pulled **verbatim** from the official `reka-nova`
registry and must stay byte-for-zero-change so they can be re-pulled / overwritten
at any time. **Do not edit a primitive to customize it.** Any customization is a
*copy-rebuilt* component with a distinct name (FE-2 "block component library"), built
on top of the unchanged primitive + tokens.

### Fresh-pull baseline (FE-1 #276)

The Design System v2 base set was re-pulled from the official registry and normalized
to repo conventions (relative `../../../lib/utils` / `../<comp>` imports;
`@lucide/vue` → `lucide-vue-next`).

- **Registry:** `https://shadcn-vue.com/r/styles/reka-nova/<component>.json`
- **CLI equivalent:** `pnpm dlx shadcn-vue@2.7.3 add <component>` (style `reka-nova`,
  `components.json`; the direct-JSON pull is used because the CLI mutates `main.css`
  and runs a dependency install that is unreliable in CI/offline).
- **Pinned versions:** `shadcn-vue@2.7.3`, `reka-ui@^2.9.7`, `tailwindcss@^4.3.0`,
  `lucide-vue-next@1.0.0`. Table data-table helpers add `@tanstack/vue-table@^8.21.3`.
- **Re-pulled (pure原版):** button, card, table, input, select, dropdown-menu, dialog,
  sheet, tabs, breadcrumb, sidebar, pagination, tooltip, popover, skeleton, empty.
- **Intentionally NOT re-pulled:** `badge` carries a project customization (`success` /
  `warning` variants, consumed widely incl. `BusinessStatusBadge`). It is **pending a
  copy-rebuilt StatusBadge in FE-2**; success/warning already exist as `--success` /
  `--warning` tokens. Other extended components (avatar, chart, file-upload, date-picker,
  field, …) are likewise customizations, not part of the original re-pull set.

## Adding a New shadcn-vue Component

1. From the `frontend/` workspace root:
   ```bash
   pnpm dlx shadcn-vue@2.7.3 add <component-name>
   ```
   This installs source to `packages/ui/src/components/ui/<component-name>/`.

2. Export all public parts from `packages/ui/src/index.ts`.

3. Add a component spec under `DESIGN/components/<component-name>.md` documenting variants, usage, and Do NOTs.

4. Use the component in application code via `@nerv-iip/ui`.

## Adding a New Design Token

Tokens live ONLY in `packages/ui/src/styles/theme.css` (the single source of truth
imported by both apps). Never add token values to an app `main.css`.

1. Add the CSS custom property to `theme.css` `:root {}`.
2. Add the dark override to `.dark {}`.
3. Add the Tailwind mapping in `@theme inline {}`.
4. Update `DESIGN/tokens.md` and `DESIGN/foundation.md`.
5. If the token is a brand constraint (`--primary`, `--brand`, `--success`/`--warning`,
   elevation), add an assertion to `packages/ui/src/design-system.contract.test.ts`.

## Fonts

Both UI fonts are **self-hosted** (bundled by Vite — never `fonts.googleapis.com` or a
runtime CDN) and imported once at the top of `packages/ui/src/styles/theme.css`:

| Role | Family | Package | License |
|---|---|---|---|
| Latin / digits | `Inter Variable` | `@fontsource-variable/inter` | OFL |
| Chinese (SC) | `MiSans` | `misans` (Xiaomi) | Apache-2.0, free commercial |

`--font-sans` is `'Inter Variable', 'MiSans', …` so Latin renders in Inter and Chinese
falls through to MiSans. Do **not** add `misans-webfont` (marked 学习交流 only / non-commercial).

### Regenerating `styles/misans.css`

The `misans` package ships MiSans at non-standard optical weights (Regular=330,
Medium=380, Semibold=520, Bold=630). `styles/misans.css` is **generated** to remap those
to standard CSS weights so Tailwind `font-normal/medium/semibold/bold` map correctly; the
`unicode-range`-subsetted woff2 chunks stay in `node_modules` (referenced relatively, not
committed). After bumping `misans`, regenerate from `frontend/`:

```bash
python - <<'PY'
import os
src="packages/ui/node_modules/misans/lib/Normal"
weights=[("MiSans-Regular",330,400),("MiSans-Medium",380,500),("MiSans-Semibold",520,600),("MiSans-Bold",630,700)]
prefix="../../node_modules/misans/lib/Normal/"
out=["/* generated from `misans` (Apache-2.0); weights remapped to standard CSS values */",""]
for name,old,new in weights:
    css=open(os.path.join(src,name+".min.css"),encoding="utf-8").read()
    css=css.replace(f"font-weight:{old}",f"font-weight:{new}").replace("url('","url('"+prefix)
    out += [f"/* {name} -> {new} */", css, ""]
open("packages/ui/src/styles/misans.css","w",encoding="utf-8",newline="\n").write("\n".join(out))
PY
```

## Component Contract Test

`packages/ui/src/design-system.contract.test.ts` reads
`packages/ui/src/styles/theme.css` and guards the Design System v2 critical tokens:

- `--primary: oklch(0.205 0 0)` — near-black primary (the retired blue must be absent)
- `--brand: oklch(0.55 0.18 255)` + `--color-brand` + `--chart-1: var(--brand)` — dynamic accent
- `--success` / `--warning` (+ `--color-success` / `--color-warning`) — status semantics
- `--background: oklch(0.985 0 0)` ≠ `--card: oklch(1 0 0)` + `--shadow-{sm,md,lg}` — elevation
- `.dark { … }` override with `--primary: oklch(0.922 0 0)` and `color-scheme: dark`

Run it with `pnpm -C frontend --filter @nerv-iip/ui test`. This test must pass before
any token change is merged. If you need to update a guarded value, update the test
intentionally and record the decision here.

## Migration Backlog

Two legacy components still use `--legacy-color-*` tokens and `<style scoped>`:

| File | What to do |
|---|---|
| `apps/console/src/components/console/InstanceTable.vue` | Rewrite using `Table` + shadcn-vue primitives |
| `apps/console/src/components/console/InstanceDetailPanel.vue` | Rewrite using `Card` + shadcn-vue primitives |
| `apps/console/src/pages/index.vue` | Remove `<style scoped>`, convert to Tailwind utilities |

Once migrated, remove all `--legacy-color-*` definitions from `main.css`.

## Versioning

This system is internal (no semver). Breaking changes to `@nerv-iip/ui` exports must update all consuming import sites in `apps/console` in the same commit.

## Review Checklist (for UI PRs)

- [ ] Visible page copy is written for business users, not developers or reviewers
- [ ] No visible demo/test/scaffolding terms (`样例`, `内置`, `用于验证`, `联动测试`, `demo`, `mock`, `seed`)
- [ ] No visible platform metadata or gateway/API wording (`组织`, `环境`, `上下文`, `业务网关契约`, `operationId`)
- [ ] No raw palette classes (`bg-blue-*`, `text-gray-*`, etc.)
- [ ] No raw hex values in `.vue` files
- [ ] No `--legacy-color-*` in new components
- [ ] Status indicators use named `Badge` variants
- [ ] Destructive actions use `AlertDialog`
- [ ] New shadcn components exported from `@nerv-iip/ui`
- [ ] DESIGN/ docs updated if a new pattern or component was introduced
- [ ] `design-system.contract.test.ts` passes
