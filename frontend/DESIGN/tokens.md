---
# Design Tokens — Nerv-IIP Console
# Three layers: Primitive → Semantic → Component
---

## Layer 1: Primitives

Raw OKLCH values defined in `main.css`. **Never referenced directly in component templates.**

```css
/* Neutrals */
--background: oklch(1 0 0);
--foreground: oklch(0.145 0 0);
--muted: oklch(0.97 0 0);
--muted-foreground: oklch(0.556 0 0);
--border: oklch(0.922 0 0);

/* Blue primary (Calm Control Plane) */
--primary: oklch(0.49 0.17 255);
--primary-foreground: oklch(0.985 0 0);
--ring: oklch(0.62 0.15 255);
--accent: oklch(0.96 0.03 255);
--accent-foreground: oklch(0.28 0.11 255);

/* Semantic status */
--destructive: oklch(0.577 0.245 27.325);

/* Surfaces */
--card: oklch(1 0 0);
--popover: oklch(1 0 0);
--secondary: oklch(0.97 0 0);
--input: oklch(0.922 0 0);

/* Sidebar */
--sidebar: oklch(0.985 0 0);
--sidebar-primary: var(--primary);
--sidebar-ring: var(--ring);
--sidebar-accent: oklch(0.96 0.03 255);

/* Charts */
--chart-1: oklch(0.58 0.16 255);   /* blue */
--chart-2: oklch(0.62 0.13 160);   /* teal */
--chart-3: oklch(0.72 0.16 80);    /* yellow */
--chart-4: oklch(0.64 0.18 35);    /* orange */
--chart-5: oklch(0.55 0.12 300);   /* purple */
```

Contract test guards `--primary`, `--ring`, `--accent`, `--sidebar-primary`, `--chart-1`. Do not change those values without updating the test.

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

1. Define the CSS custom property in `main.css` `:root` block.
2. Add the dark-mode override in `.dark {}` block.
3. Add a Tailwind mapping in `@theme inline {}`.
4. Update this file.
5. If the token is design-critical (like primary), add a contract assertion in `design-system.contract.test.ts`.
