# AGENTS.md — @nerv-iip/ui（NvUI 组件库 · 库内规则）

> 根 `AGENTS.md` 的 NvUI 章节写的是**消费者侧**（app 怎么用）；本文件是
> **库内侧**（改库本身的规矩）。权威 ADR：
> `docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`。

## 分层与改动边界

- `components/ui/` = shadcn 原版，**byte-for-byte 零改动**（无 `Nv`、无
  `--nv-`）。定制 = 复制重建到品牌层，绝不改原版。附录 A 未列品牌版的原版件
  （`Alert`/`Empty`/`Toaster` 等）经 barrel 直接供 app 使用是合法现状。
- 品牌层：`pc/` `blocks/` `layout/`（PC）、`touch/`（工位触屏）、`screen/`
  （大屏）。一件组件跨两个表面必须建两件，绝不"一件两模式"。
- 每个表面层有自己的产品定位文件，**改该层组件前必读**：PC =
  `src/components/pc/product.md`（气质三层：成熟 B端骨架/冷静工业基调/产品级
  触感），大屏 = `src/components/screen/product.md`。

## 命名

- 新组件命名走 ADR 0020 §1.2 的 **R1–R5 判定流程**；已冻结的逐件结果 =
  附录 A，不要即兴起名。
- 包名永不改（ADR 0020 Decision 2）：品牌由 `Nv*` 前缀承载，不是包重命名。

## Token / CSS Layer（ADR 0020 §4）

- 共享令牌 `--nv-*` 在 `styles/theme.css`（`@layer nv-tokens`）；大屏独立
  `--nv-scr-*` 在 `components/screen/tokens.css`。场景令牌只允许 **var 链**
  引用系统令牌（如 `--nv-scr-ease: var(--nv-ease-out-quart)`），禁止复制
  字面量。
- 八层序由宿主 app 在入口 CSS 声明：
  `@layer theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app;`
- `styles/overrides.css` **故意不分层**，宿主在 import 点决定层级（product
  app → `layer(nv-overrides)`；VitePress 文档站 → unlayered）。不要在该文件
  内加 `@layer`，不要重新引入 `revert-layer` hack（ADR 0020 §4.2.4）。

## 门禁与同步

- 包内 contract tests：`nvui-naming` / `ui-primitives` / `blocks` /
  `design-system`（各 app 另有 `nvui-imports` 守边界）。新增导出/改名先看
  会不会打破它们。
- 新增/改动组件同步 design-system 文档站对应页
  （`frontend/apps/design-system/docs/`），组件文档站是 props 的
  source of truth。
- **欢迎 app 侧反哺**：业务页面里长出的数据展示/业务组件成熟后上提进
  对应层。入层三件事：过该层设计哲学（screen 层 = `product.md`）、按
  R1–R5 定名、补 barrel 导出 + contract test + 文档站页。
