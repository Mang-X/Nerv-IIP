---
# Design Tokens — Nerv-IIP Console
# Three layers: Primitive → Semantic → Component
---

## Scene Namespaces (ADR 0020)

Token 名称按场景命名空间隔离（[ADR 0020](../../docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md) §3，
落地批 MAN-433，`--sb-*` → `--nv-scr-*` 全表映射见 ADR 附录 B）：

| 命名空间 | 场景 | 说明 |
|---|---|---|
| 契约层（无前缀，冻结） | shadcn 原版依赖 | `--background` `--primary` `--border` `--chart-*` `--sidebar-*` `--radius` 等官方主题名——改名等于改原版，永不加前缀 |
| `--nv-*` | PC / 共享语义 | 项目自有扩展：`--nv-brand` `--nv-success` `--nv-warning` `--nv-*-strong` `--nv-ease-*` `--nv-duration-*` `--nv-shadow-*`（现 `--brand`/`--success`/… 迁移目标，ADR 附录 C） |
| `--nv-scr-*` | screen 大屏 | 现 `--sb-*` 30 项全表迁移（ADR 附录 B） |
| `--nv-m-*` | mobile | 当前空集，规范先行（mobile token 现全部来自共享层） |
| `--nv-t-*` | touch 工位 | 当前空集，规范先行 |

规则：primitive 值全库共享；**允许跨场景取值相同，但名称必须隔离**——同值用 var 引用
链表达（`--nv-scr-ease: var(--nv-ease-out-quart)`），禁止复制字面量；场景组件只允许
引用本场景前缀 + 契约层 token（contract test 拦截跨场景直引）。动效统一 motion-v 封装：
JS 预设唯一来源 `packages/ui/src/lib/motion.ts`，数值与 CSS token 同表，引用名分场景。

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

All token edits happen in the shared `packages/ui/src/styles/theme.css` or the owning
scene sheet (screen: `components/screen/tokens.css`) — never in an app `main.css`
(those only `@import` the shared files).

1. 先按 Scene Namespaces 表定前缀（契约层冻结 / `--nv-*` / `--nv-scr-*` / `--nv-m-*` /
   `--nv-t-*`）；跨场景同值用 var 引用链，不复制字面量。
2. Define the CSS custom property in the `:root` block of the owning sheet.
3. Add the dark-mode override in the `.dark {}` block（场景表如无亮色态则不适用）.
4. Add a Tailwind mapping in `@theme inline {}` (e.g. `--color-foo: var(--nv-foo)` —
   桥接名保持 utility 契约，右值指向命名空间新名).
5. Update this file.
6. If the token is design-critical (primary, brand, success/warning, elevation),
   add a contract assertion in `packages/ui/src/design-system.contract.test.ts`.
