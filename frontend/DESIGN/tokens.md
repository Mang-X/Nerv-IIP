---
# 设计 token — Nerv-IIP Console
# 三层结构：原始值 → 语义值 → 组件值
---

## 场景命名空间（ADR 0020）

token 名称按场景命名空间隔离（[ADR 0020](../../docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md) §3，
已落地 MAN-436 / #790，`--sb-*` → `--nv-scr-*` 全表映射见 ADR 附录 B）：

| 命名空间               | 场景            | 说明                                                                                                                                   |
| ---------------------- | --------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| 契约层（无前缀，冻结） | shadcn 原版依赖 | `--background` `--primary` `--border` `--chart-*` `--sidebar-*` `--radius` 等官方主题名——改名等于改原版，永不加前缀                    |
| `--nv-*`               | PC / 共享语义   | 项目自有扩展：`--nv-brand` `--nv-success` `--nv-warning` `--nv-*-strong` `--nv-ease-*` `--nv-duration-*` `--nv-shadow-*`（ADR 附录 C） |
| `--nv-scr-*`           | screen 大屏     | 原 `--sb-*` 30 项全表已迁移（ADR 附录 B）                                                                                              |
| `--nv-m-*`             | mobile          | 当前空集，规范先行（mobile token 现全部来自共享层）                                                                                    |
| `--nv-t-*`             | touch 工位      | 当前空集，规范先行                                                                                                                     |

规则：primitive 值全库共享；**允许跨场景取值相同，但名称必须隔离**——同值用 var 引用
链表达（`--nv-scr-ease: var(--nv-ease-out-quart)`），禁止复制字面量；场景组件只允许
引用本场景前缀 + 契约层 token（契约测试拦截跨场景直引）。动效统一由 motion-v 封装：
JS 预设唯一来源 `packages/ui/src/lib/motion.ts`，数值与 CSS token 同表，引用名分场景。

**一个迁移周期内**旧名（`--brand`/`--success`/`--sb-*`/…）仍以 var 链别名保留（
`--brand: var(--nv-brand)`、`--sb-bg: var(--nv-scr-bg)`），直引旧名的在途代码不断裂；
下一周期（收口批）删除别名（screen 侧 `--sb-*` 别名已随收口批删除，`--brand` 等共享
别名仍在）。运行时动态强调色由主题选择器写 `--nv-brand`（见 useTheme），
`--brand` 别名随之同步。

## 样式隔离——CSS 层叠层（ADR 0020 §4）

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

- 演示容器根 `vp-raw`（`<Demo>`/`<ScreenDemo>`/`<MobileDoc>`），使 VitePress 的 base/vp-doc
  重置不再渗入组件演示；`overrides.css` 在站内以未分层方式导入以取得更高特异性；不复用 `revert-layer`。

## 第 1 层：原始值

原始 OKLCH 值只在共享的 `@nerv-iip/ui` 主题文件
`packages/ui/src/styles/theme.css` 中定义一次（由两个 app 的 `main.css` 导入）。
**绝不得在组件模板中直接引用。**

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

同一文件后续提供完整的 `.dark { … }` 覆盖（dashboard-01 暗色基线：
`--background: oklch(0.145 0 0)`、`--card: oklch(0.205 0 0)`、`--primary: oklch(0.922 0 0)` 等）。

契约测试 `packages/ui/src/design-system.contract.test.ts` 守护品牌关键值：近黑色
`--primary`、动态 `--nv-brand`（+ `--color-brand`、`--chart-1`）、
`--nv-success`/`--nv-warning`、`--background`≠`--card` 高程 + `--nv-shadow-*`、
场景命名空间 + CSS 层叠层契约（ADR 0020 §3/§4），以及 `.dark` 覆盖的存在性。
不得在未更新测试的情况下更改这些值。

---

## 第 2 层：语义工具类（通过 `@theme inline` 接入 Tailwind v4）

这些是在模板中使用的值。`main.css` 中的 `@theme inline` 区块将每个 CSS 自定义属性
映射为 Tailwind 颜色工具类。

| Tailwind 工具类         | CSS 变量             | 使用时机                               |
| ----------------------- | -------------------- | -------------------------------------- |
| `bg-background`         | `--background`       | 页面主体                               |
| `bg-card`               | `--card`             | 卡片、面板表面                         |
| `bg-muted`              | `--muted`            | 悬停行、chip 背景                      |
| `bg-primary`            | `--primary`          | CTA 按钮、激活导航                     |
| `bg-accent`             | `--accent`           | 选中行、标签 chip 背景                 |
| `bg-destructive`        | `--destructive`      | 危险区域（不得用于 success / warning） |
| `text-foreground`       | `--foreground`       | 主要正文文本                           |
| `text-muted-foreground` | `--muted-foreground` | 次要文本、说明文字                     |
| `text-primary`          | `--primary`          | 链接式强调                             |
| `text-destructive`      | `--destructive`      | 行内错误消息                           |
| `border-border`         | `--border`           | 所有分隔线和输入框边框                 |
| `ring-ring`             | `--ring`             | 焦点环（由 shadcn 处理）               |

---

## 第 3 层：组件 token

这些 token 由 shadcn-vue 组件通过 CVA 在内部处理。除非本文件明确记录，否则不得使用任意
CSS 类覆盖它们。

### Badge 变体（扩展）

| 变体          | 使用时机               |
| ------------- | ---------------------- |
| `default`     | 主要标签、系统信息     |
| `secondary`   | 禁用 / 非活跃状态      |
| `outline`     | 中性类别、标签         |
| `destructive` | 错误状态、删除相关     |
| `success`     | 活跃 / 启用 / 健康状态 |
| `warning`     | 降级 / 风险状态        |
| `ghost`       | 弱化标签               |

### Button 变体

| 变体          | 使用时机                             |
| ------------- | ------------------------------------ |
| `default`     | 主 CTA（每个工具栏 / 表单一个）      |
| `outline`     | 次要操作                             |
| `ghost`       | 纯图标控件、表格行操作               |
| `destructive` | 不可逆的破坏性操作（前置确认对话框） |
| `link`        | 行内导航                             |

---

## 旧 token（新代码中不得使用）

`--legacy-color-*` token 仅为向后兼容两个阶段 8 之前的组件而存在：

- `frontend/apps/console/src/components/console/InstanceTable.vue`
- `frontend/apps/console/src/components/console/InstanceDetailPanel.vue`

它们是迁移目标。重写这些组件时，必须同时删除 `<style scoped>` 区块和 `main.css` 中
所有 `--legacy-color-*` 定义。

---

## 新增 token

所有 token 编辑都发生在共享的 `packages/ui/src/styles/theme.css` 或所属场景表
（screen：`components/screen/tokens.css`）中，绝不得在 app `main.css` 中编辑
（这些文件只通过 `@import` 导入共享文件）。

1. 先按场景命名空间表定前缀（契约层冻结 / `--nv-*` / `--nv-scr-*` / `--nv-m-*` /
   `--nv-t-*`）；跨场景同值用 var 引用链，不复制字面量。
2. 在所属 token 表的 `:root` 区块中定义 CSS 自定义属性。
3. 在 `.dark {}` 区块中添加暗色模式覆盖（场景表如无亮色态则不适用）。
4. 在 `@theme inline {}` 中添加 Tailwind 映射（例如 `--color-foo: var(--nv-foo)`；
   桥接名保持 utility 契约，右值指向命名空间新名）。
5. 更新本文件。
6. 如果 token 对设计至关重要（primary、brand、success/warning、高程），就在
   `packages/ui/src/design-system.contract.test.ts` 中添加契约断言。
