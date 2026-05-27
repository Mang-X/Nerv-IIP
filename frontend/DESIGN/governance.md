# Governance — Nerv-IIP Console Design System

## Ownership

The `@nerv-iip/ui` package (`frontend/packages/ui`) is the single source of truth for all UI primitives. Application code in `frontend/apps/console` never owns primitive component logic.

## Adding a New shadcn-vue Component

1. From the `frontend/` workspace root:
   ```bash
   pnpm dlx shadcn-vue@latest add <component-name>
   ```
   This installs source to `packages/ui/src/components/ui/<component-name>/`.

2. Export all public parts from `packages/ui/src/index.ts`.

3. Add a component spec under `DESIGN/components/<component-name>.md` documenting variants, usage, and Do NOTs.

4. Use the component in application code via `@nerv-iip/ui`.

## Adding a New Design Token

1. Add the CSS custom property to `main.css` `:root {}`.
2. Add the dark override to `.dark {}`.
3. Add the Tailwind mapping in `@theme inline {}`.
4. Update `DESIGN/tokens.md`.
5. If the token is a brand constraint (like `--primary`), add a `expect(value).toBe(...)` assertion to `packages/ui/src/design-system.contract.test.ts`.

## Component Contract Test

`packages/ui/src/design-system.contract.test.ts` guards the critical token values:

- `--primary: oklch(0.49 0.17 255)` — Calm Control Plane blue
- `--ring: oklch(0.62 0.15 255)` — Focus ring
- `--accent: oklch(0.96 0.03 255)` — Subtle blue surface
- `--sidebar-primary: var(--primary)` — Sidebar active

This test must pass before any CSS change is merged. If you need to update a guarded value, update the test intentionally and record the decision here.

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
