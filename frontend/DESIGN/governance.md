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
`@lucide/vue` → `@lucide/vue`).

- **Registry:** `https://shadcn-vue.com/r/styles/reka-nova/<component>.json`
- **CLI equivalent:** `pnpm dlx shadcn-vue@2.7.3 add <component>` (style `reka-nova`,
  `components.json`; the direct-JSON pull is used because the CLI mutates `main.css`
  and runs a dependency install that is unreliable in CI/offline).
- **Pinned versions:** `shadcn-vue@2.7.3`, `reka-ui@^2.9.7`, `tailwindcss@^4.3.0`,
  `@lucide/vue@1.0.0`. Table data-table helpers add `@tanstack/vue-table@^8.21.3`.
- **Re-pulled (pure原版):** button, card, table, input, select, dropdown-menu, dialog,
  sheet, tabs, breadcrumb, sidebar, pagination, tooltip, popover, skeleton, empty.
- **Intentionally NOT re-pulled:** `badge` carries a project customization (`success` /
  `warning` variants, consumed widely incl. `BusinessStatusBadge`). It is **pending a
  copy-rebuilt StatusBadge in FE-2**; success/warning already exist as `--success` /
  `--warning` tokens. Other extended components (avatar, chart, file-upload, date-picker,
  field, …) are likewise customizations, not part of the original re-pull set.

## NvUI Naming & Scene Namespaces (ADR 0020)

组件库品牌名 **NvUI**，品牌前缀 `Nv`。完整规则、判定流程与全量旧名→新名映射表冻结在
[ADR 0020](../../docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md)，
执行批次为 MAN-433（库侧）/ MAN-435（分 app codemod）/ MAN-436（守护收口）。要点：

- `Nv` 前缀 = 品牌定制层唯一标识；无前缀 = shadcn 原版底座（或待收口的 deprecated 旧名）。
- PC 层（pc/blocks/layout）取素名：`NvButton`、`NvDataTable`、`NvPageHeader`（素名
  优先权归 PC）。screen/mobile/touch 与 PC 潜在同名者保留场景词根（`NvScreenButton`、
  `NvMobileDialog`、`NvTouchButton`），天然独有名直接 Nv（`NvScanBar`、`NvOeeHero`）。
- 新组件命名必须走 ADR 0020 §1.2 的 R1–R5 判定流程；先定场景归属（表面/视距/输入方式
  决定目录与 token 命名空间），再定名。
- shadcn 原版（`components/ui/`）零改动零重命名——本文件既有红线不变，且由 contract
  test 断言"原版目录不出现 `Nv`/`--nv-` 字样"机器守护。
- 迁移期（MAN-433 合入后）旧名是 `@deprecated` 别名：**新代码禁止使用旧名**
  （`NvButton`、`--nv-scr-*`、`.nv-*`/`.nv-scr-*` 类）。
- CSS 类名前缀与 token 命名空间对齐：PC `nv-*`、screen `nv-scr-*`、mobile `nv-m-*`、
  touch `nv-t-*`；Nv 件 `data-slot` 值以 `nv-` 开头。

## Style Layers (CSS cascade layers, ADR 0020)

全部组件样式进入 cascade layer，全局唯一层序（每个 app `main.css` 首条语句声明）：

```css
@layer theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app;
```

- 库内禁止 unlayered 规则（白名单：`@font-face`/`@keyframes`/`@property`/`@import`/
  `@custom-variant`/`@theme`）；SFC `<style>` 统一包 `@layer nv-components`；历史
  "故意 unlayered"的玻璃拟态/sidebar 选中态收编进 `nv-overrides`（位于 utilities 之后）。
- app 自定义 CSS 一律进 `@layer app`（层序最后，app 主权最高）。
- VitePress 文档站是例外宿主：utilities 与 overrides 需 unlayered 导入以赢过 VitePress
  自带裸重置；启用 `postcssIsolateStyles()` + demo 统一容器挂 `vp-raw`；`--vp-*` 桥接
  映射保留；**禁用 `revert-layer`**（历史坑）。细则见 ADR 0020 §4.2。

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

Tokens live ONLY in `packages/ui/src/styles/theme.css`（PC/共享）或对应场景 token 表
（screen: `components/screen/tokens.css`；mobile/touch 表在首个场景 token 出现时建立）。
Never add token values to an app `main.css`.

1. 先定命名空间（ADR 0020 §3）：shadcn 契约名冻结不动；PC/共享自有语义用 `--nv-*`；
   场景专属用 `--nv-scr-*` / `--nv-m-*` / `--nv-t-*`。跨场景同值必须用 var 引用链
   （`--nv-scr-green: var(--nv-success)`），禁止复制字面量；场景组件只允许引用本场景
   前缀 + 契约层 token。
2. Add the CSS custom property to the `:root {}` of the owning token sheet.
3. Add the dark override to `.dark {}`（场景表如无亮色态则不适用）.
4. Add the Tailwind mapping in `@theme inline {}`（桥接名保持 utility 契约，右值指向
   `--nv-*` 新名）.
5. Update `DESIGN/tokens.md` and `DESIGN/foundation.md`.
6. If the token is a brand constraint (`--primary`, `--nv-brand`, `--nv-success`/
   `--nv-warning`, elevation), add an assertion to
   `packages/ui/src/design-system.contract.test.ts`.

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

ADR 0020 落地批（MAN-433）将把守护面扩展到：八层层序声明、库内零 unlayered（白名单
外）、`--nv-scr-*` 全表与 `--nv-scr-*` 别名期形态、关键 var 引用链、原版目录纯净
（无 `Nv`/`--nv-` 字样）、Nv 件 `data-slot` 命名空间、跨场景 token 引用污染、旧名零
新增。清单见 ADR 0020 §4.4。

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
- [ ] 新组件名过 ADR 0020 §1.2 判定流程（Nv 前缀 + 场景词根判定），无旧名
      （`*Pro`、裸场景名、`--nv-scr-*`、`.nv-*`/`.nv-scr-*`）新增
- [ ] 新增/改动的手写样式在正确的 cascade layer 内（SFC → `nv-components`；赢
      utilities 的库级装饰 → `nv-overrides`；app 自定义 → `app`），无白名单外 unlayered
- [ ] DESIGN/ docs updated if a new pattern or component was introduced
- [ ] `design-system.contract.test.ts` passes
