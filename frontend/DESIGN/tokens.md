---
# Design Tokens — Nerv-IIP Console
# Three layers: Primitive → Semantic → Component
---

## Layer 1: Primitives

Raw OKLCH values defined ONCE in the shared `@nerv-iip/ui` theme file —
`packages/ui/src/styles/theme.css` (imported by both apps' `main.css`).
**Never referenced directly in component templates.**

```css
/* Surfaces — background ≠ card gives the inset floating-panel elevation */
--background: oklch(0.985 0 0);
--foreground: oklch(0.145 0 0);
--card: oklch(1 0 0);
--popover: oklch(1 0 0);
--muted: oklch(0.97 0 0);
--muted-foreground: oklch(0.556 0 0);
--border: oklch(0.922 0 0);
--input: oklch(0.922 0 0);

/* Near-black primary (Design System v2) */
--primary: oklch(0.205 0 0);
--primary-foreground: oklch(0.985 0 0);
--secondary: oklch(0.97 0 0);
--accent: oklch(0.97 0 0);          /* neutral hover surface */
--ring: oklch(0.708 0 0);

/* Dynamic brand accent — blue by default, overridable at runtime via --brand */
--brand: oklch(0.55 0.18 255);
--brand-foreground: oklch(0.985 0 0);

/* Semantic status */
--destructive: oklch(0.577 0.245 27.325);
--success: oklch(0.62 0.17 149);
--warning: oklch(0.75 0.15 75);

/* Elevation scale */
--shadow-xs: 0 1px 2px 0 oklch(0 0 0 / 0.04);
--shadow-sm: 0 1px 3px 0 oklch(0 0 0 / 0.08), 0 1px 2px -1px oklch(0 0 0 / 0.06);
--shadow-md: 0 4px 8px -2px oklch(0 0 0 / 0.08), 0 2px 4px -2px oklch(0 0 0 / 0.05);
--shadow-lg: 0 12px 24px -6px oklch(0 0 0 / 0.1), 0 4px 8px -4px oklch(0 0 0 / 0.05);

/* Charts — chart-1 tracks the dynamic brand */
--chart-1: var(--brand);
--chart-2: oklch(0.62 0.13 160);   /* teal */
--chart-3: oklch(0.72 0.16 80);    /* yellow */
--chart-4: oklch(0.64 0.18 35);    /* orange */
--chart-5: oklch(0.55 0.12 300);   /* purple */
```

A full `.dark { … }` override follows in the same file (dashboard-01 dark baseline:
`--background: oklch(0.145 0 0)`, `--card: oklch(0.205 0 0)`, `--primary: oklch(0.922 0 0)`, …).

The contract test `packages/ui/src/design-system.contract.test.ts` guards the
brand-critical values: near-black `--primary`, the dynamic `--brand` (+ `--color-brand`,
`--chart-1`), `--success`/`--warning`, the `--background`≠`--card` elevation + `--shadow-*`,
and presence of the `.dark` override. Do not change those without updating the test.

---

## Layer 2: Semantic Utilities (Tailwind v4 via `@theme inline`)

These are the values you use in templates. The `@theme inline` block in `main.css` maps each CSS custom property to a Tailwind color utility.

| Tailwind utility | CSS var | When to use |
|---|---|---|
| `bg-background` | `--background` | Page body |
| `bg-card` | `--card` | Card, panel surfaces |
| `bg-muted` | `--muted` | Hover rows, chip backgrounds |
| `bg-primary` | `--primary` | CTA buttons, active nav |
| `bg-accent` | `--accent` | Selected row, tag chip bg |
| `bg-destructive` | `--destructive` | Danger zones (DO NOT use for success/warning) |
| `text-foreground` | `--foreground` | Primary body text |
| `text-muted-foreground` | `--muted-foreground` | Secondary text, captions |
| `text-primary` | `--primary` | Link-style emphasis |
| `text-destructive` | `--destructive` | Inline error messages |
| `border-border` | `--border` | All dividers and input borders |
| `ring-ring` | `--ring` | Focus rings (handled by shadcn) |

---

## Layer 3: Component Tokens

These are handled internally by shadcn-vue components via CVA. Do not override these with arbitrary classes unless documented here.

### Badge variants (extended)

| Variant | When to use |
|---|---|
| `default` | Primary label, system info |
| `secondary` | Disabled / inactive state |
| `outline` | Neutral categories, tags |
| `destructive` | Error state, deletion-related |
| `success` | Active / enabled / healthy state |
| `warning` | Degraded / at-risk state |
| `ghost` | De-emphasized label |

### Button variants

| Variant | When to use |
|---|---|
| `default` | Primary call-to-action (one per toolbar/form) |
| `outline` | Secondary actions |
| `ghost` | Icon-only controls, table row actions |
| `destructive` | Irreversible destructive action (preceded by confirm dialog) |
| `link` | Inline navigation |

---

## Legacy Tokens (DO NOT USE IN NEW CODE)

`--legacy-color-*` tokens exist for backward compatibility with two pre-Phase-8 components:

- `frontend/apps/console/src/components/console/InstanceTable.vue`
- `frontend/apps/console/src/components/console/InstanceDetailPanel.vue`

These are migration targets. When those components are rewritten, remove both the `<style scoped>` block and all `--legacy-color-*` definitions from `main.css`.

---

## Adding New Tokens

All token edits happen in the shared `packages/ui/src/styles/theme.css` — never in
an app `main.css` (those only `@import` the shared file).

1. Define the CSS custom property in the `:root` block of `theme.css`.
2. Add the dark-mode override in the `.dark {}` block.
3. Add a Tailwind mapping in `@theme inline {}` (e.g. `--color-foo: var(--foo)`).
4. Update this file.
5. If the token is design-critical (primary, brand, success/warning, elevation),
   add a contract assertion in `packages/ui/src/design-system.contract.test.ts`.
