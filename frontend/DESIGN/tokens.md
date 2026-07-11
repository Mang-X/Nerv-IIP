---
# Design Tokens — Nerv-IIP Console
# Three layers: Primitive → Semantic → Component
---

## Scene Namespaces (ADR 0020)

Token 名称按场景命名空间隔离（[ADR 0020](../../docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md) §3，
已落地 MAN-436 / #790，`--sb-*` → `--nv-scr-*` 全表映射见 ADR 附录 B）：

| 命名空间               | 场景            | 说明                                                                                                                                   |
| ---------------------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| 契约层（无前缀，冻结） | shadcn 原版依赖 | `--background` `--primary` `--border` `--chart-*` `--sidebar-*` `--radius` 等官方主题名——改名等于改原版，永不加前缀                    |
| `--nv-*`               | PC / 共享语义   | 项目自有扩展：`--nv-brand` `--nv-success` `--nv-warning` `--nv-*-strong` `--nv-ease-*` `--nv-duration-*` `--nv-shadow-*`（ADR 附录 C） |
| `--nv-scr-*`           | screen 大屏     | `--sb-*` 30 项全表已迁移（ADR 附录 B）                                                                                                 |
| `--nv-m-*`             | mobile          | 当前空集，规范先行（mobile token 现全部来自共享层）                                                                                    |
| `--nv-t-*`             | touch 工位      | 当前空集，规范先行                                                                                                                     |

规则：primitive 值全库共享；**允许跨场景取值相同，但名称必须隔离**——同值用 var 引用
链表达（`--nv-scr-ease: var(--nv-ease-out-quart)`），禁止复制字面量；场景组件只允许
引用本场景前缀 + 契约层 token（contract test 拦截跨场景直引）。动效统一 motion-v 封装：
JS 预设唯一来源 `packages/ui/src/lib/motion.ts`，数值与 CSS token 同表，引用名分场景。

**一个迁移周期内**旧名（`--brand`/`--success`/`--sb-*`/…）仍以 var 链别名保留（
`--brand: var(--nv-brand)`、`--sb-bg: var(--nv-scr-bg)`），直引旧名的在途代码不断裂；
下一周期（收口批）删除别名。运行时动态强调色由主题选择器写 `--nv-brand`（见 useTheme），
`--brand` 别名随之同步。

## Style Isolation — CSS Cascade Layers (ADR 0020 §4)

全局层序（每个产品 app `main.css` 首条语句，逐字一致）：

```css
@layer theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app;
```

- `nv-tokens`：库 token 表（theme.css 的 `:root`/`.dark`、场景 `tokens.css`）。
- `nv-components`：库手写组件样式——全部 SFC `<style>` 包 `@layer nv-components {}`，
  以及 portalled 覆盖层动效（`.nv-overlay-content`）。utilities 在其后，业务模板 class
  可覆盖组件默认样式。
- `nv-overrides`：必须赢过 utilities 的库级装饰（overlay 玻璃拟态、sidebar premium 选中态），
  独立文件 `styles/overrides.css`（文件内不包层），产品 app 以 `layer(nv-overrides)` 导入。
- `app`：app 自定义样式主权最高。

**VitePress 文档站**（ADR 0020 §4.2）：`postcssIsolateStyles({ includeFiles: [base.css, vp-doc.css] })`

- demo 容器根 `vp-raw`（`<Demo>`/`<ScreenDemo>`/`<MobileDoc>`），使 VitePress 的 base/vp-doc
  重置不再渗入组件 demo；`overrides.css` 在站内 unlayered 导入以特异性取胜；不复用 `revert-layer`。

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
--accent: oklch(0.97 0 0); /* neutral hover surface */
--ring: oklch(0.708 0 0);

/* Dynamic brand accent (--nv-*) — overridable at runtime via --nv-brand.
   The one-cycle alias `--brand: var(--nv-brand)` keeps legacy direct refs live. */
--nv-brand: oklch(0.54 0.16 256);
--nv-brand-foreground: oklch(0.985 0 0);

/* Semantic status — --destructive is a contract name; success/warning are --nv-* */
--destructive: oklch(0.55 0.2 25);
--nv-success: oklch(0.6 0.12 160);
--nv-warning: oklch(0.72 0.13 68);

/* Elevation scale (--nv-*; Tailwind `shadow-*` utilities bridge to these) */
--nv-shadow-xs: 0 1px 2px 0 oklch(0 0 0 / 0.04);
--nv-shadow-sm: 0 1px 3px 0 oklch(0 0 0 / 0.08), 0 1px 2px -1px oklch(0 0 0 / 0.06);
--nv-shadow-md: 0 4px 8px -2px oklch(0 0 0 / 0.08), 0 2px 4px -2px oklch(0 0 0 / 0.05);
--nv-shadow-lg: 0 12px 24px -6px oklch(0 0 0 / 0.1), 0 4px 8px -4px oklch(0 0 0 / 0.05);

/* Charts — chart-1 (contract name) tracks the dynamic brand via the --nv-* value */
--chart-1: var(--nv-brand);
--chart-2: oklch(0.64 0.11 200);
--chart-3: oklch(0.7 0.12 72);
--chart-4: oklch(0.62 0.15 14);
--chart-5: oklch(0.56 0.12 292);
```

A full `.dark { … }` override follows in the same file (dashboard-01 dark baseline:
`--background: oklch(0.145 0 0)`, `--card: oklch(0.205 0 0)`, `--primary: oklch(0.922 0 0)`, …).

The contract test `packages/ui/src/design-system.contract.test.ts` guards the
brand-critical values: near-black `--primary`, the dynamic `--nv-brand` (+ `--color-brand`,
`--chart-1`), `--nv-success`/`--nv-warning`, the `--background`≠`--card` elevation +
`--nv-shadow-*`, the scene-namespace + cascade-layer contract (ADR 0020 §3/§4), and
presence of the `.dark` override. Do not change those without updating the test.

---

## Layer 2: Semantic Utilities (Tailwind v4 via `@theme inline`)

These are the values you use in templates. The `@theme inline` block in `main.css` maps each CSS custom property to a Tailwind color utility.

| Tailwind utility        | CSS var              | When to use                                   |
| ----------------------- | -------------------- | --------------------------------------------- |
| `bg-background`         | `--background`       | Page body                                     |
| `bg-card`               | `--card`             | Card, panel surfaces                          |
| `bg-muted`              | `--muted`            | Hover rows, chip backgrounds                  |
| `bg-primary`            | `--primary`          | CTA buttons, active nav                       |
| `bg-accent`             | `--accent`           | Selected row, tag chip bg                     |
| `bg-destructive`        | `--destructive`      | Danger zones (DO NOT use for success/warning) |
| `text-foreground`       | `--foreground`       | Primary body text                             |
| `text-muted-foreground` | `--muted-foreground` | Secondary text, captions                      |
| `text-primary`          | `--primary`          | Link-style emphasis                           |
| `text-destructive`      | `--destructive`      | Inline error messages                         |
| `border-border`         | `--border`           | All dividers and input borders                |
| `ring-ring`             | `--ring`             | Focus rings (handled by shadcn)               |

---

## Layer 3: Component Tokens

These are handled internally by shadcn-vue components via CVA. Do not override these with arbitrary classes unless documented here.

### Badge variants (extended)

| Variant       | When to use                      |
| ------------- | -------------------------------- |
| `default`     | Primary label, system info       |
| `secondary`   | Disabled / inactive state        |
| `outline`     | Neutral categories, tags         |
| `destructive` | Error state, deletion-related    |
| `success`     | Active / enabled / healthy state |
| `warning`     | Degraded / at-risk state         |
| `ghost`       | De-emphasized label              |

### Button variants

| Variant       | When to use                                                  |
| ------------- | ------------------------------------------------------------ |
| `default`     | Primary call-to-action (one per toolbar/form)                |
| `outline`     | Secondary actions                                            |
| `ghost`       | Icon-only controls, table row actions                        |
| `destructive` | Irreversible destructive action (preceded by confirm dialog) |
| `link`        | Inline navigation                                            |

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
