---
# 基础规范——Nerv-IIP 设计系统 v2
# 黑色主色、dashboard-01 基线：专业、高信息密度、亮暗双模式、动态强调色。
---

## 风格意图

这是一个对齐 shadcn **dashboard-01** 的高信息密度企业控制平面：采用近黑主色、
中性界面框架、**亮色与暗色同为一等模式**，并提供**运行时动态强调色**（品牌蓝用于强调和
页面级主 CTA）。页面画布在视觉层级上略低于卡片表面（内嵌悬浮面板），并使用
`--shadow-*` 高程尺度。气质与设计原则的权威定位：
`packages/ui/src/components/pc/product.md`（骨架=成熟 B端范式 / 基调=冷静工业
高密度 / 触感=产品级精致）。

token 的单一事实来源是 `@nerv-iip/ui` 中的
`packages/ui/src/styles/theme.css`，由 `apps/console` 和 `apps/business-console`
共同导入。绝不得按 app 重复定义 token 值。

### 硬性规则（Epic #275 / FE-1 #276）

1. `--primary` 是**近黑色**（`oklch(0.205 0 0)`），不是已停用的蓝色。
2. 品牌蓝位于 `--nv-brand`（旧别名 `--brand`），可在**运行时覆盖**，用于强调
   （链接、焦点、图表、选中状态）以及**页面级主 CTA**
   （`NvButton variant="brand"`，每个页面/工具栏一个——所有者裁决 2026-07-16）。
3. 亮色和暗色模式均随产品交付（`theme.css` 中的 `.dark` 覆盖）。
4. **绝不得编辑 shadcn-vue 原版组件。**这些组件从官方 `reka-nova` 注册表（registry）
   按原文重新拉取，再次拉取时可能被覆盖。任何定制都必须使用复制重建组件（FE-2），
   绝不能修改基础原语（primitive）。

---

## 颜色

### 语义 token（所有代码都必须使用；绝不得使用原始十六进制值或 Tailwind 调色板名称）

| Token                  | 亮色值                      | 暗色值                      | 用途                                               |
| ---------------------- | --------------------------- | --------------------------- | -------------------------------------------------- |
| `--background`         | `oklch(0.985 0 0)`          | `oklch(0.145 0 0)`          | 页面画布（位于卡片下层）                           |
| `--foreground`         | `oklch(0.145 0 0)`          | `oklch(0.985 0 0)`          | 正文文本                                           |
| `--card`               | `oklch(1 0 0)`              | `oklch(0.205 0 0)`          | 卡片 / 内嵌面板表面                                |
| `--muted`              | `oklch(0.97 0 0)`           | `oklch(0.269 0 0)`          | 弱化表面（表格条纹、悬停）                         |
| `--muted-foreground`   | `oklch(0.556 0 0)`          | `oklch(0.708 0 0)`          | 次要文本、占位文本                                 |
| `--border`             | `oklch(0.922 0 0)`          | `oklch(1 0 0 / 10%)`        | 所有边框                                           |
| `--primary`            | `oklch(0.205 0 0)`          | `oklch(0.922 0 0)`          | 主操作、激活导航（近黑色）                         |
| `--primary-foreground` | `oklch(0.985 0 0)`          | `oklch(0.205 0 0)`          | 主色表面上的文本                                   |
| `--secondary`          | `oklch(0.97 0 0)`           | `oklch(0.269 0 0)`          | 次级 / 幽灵式表面                                  |
| `--accent`             | `oklch(0.97 0 0)`           | `oklch(0.269 0 0)`          | 中性悬停表面（选中行、chip 背景）                  |
| `--nv-brand`           | `oklch(0.55 0.18 255)`      | `oklch(0.62 0.17 255)`      | **动态**强调色（链接、图表、焦点）                 |
| `--destructive`        | `oklch(0.577 0.245 27.325)` | `oklch(0.704 0.191 22.216)` | 危险操作、错误状态                                 |
| `--nv-success`         | `oklch(0.62 0.17 149)`      | `oklch(0.7 0.16 150)`       | 健康 / 启用状态                                    |
| `--nv-warning`         | `oklch(0.75 0.15 75)`       | `oklch(0.8 0.15 80)`        | 降级 / 风险状态                                    |
| `--ring`               | `oklch(0.708 0 0)`          | `oklch(0.556 0 0)`          | 焦点环                                             |
| `--sidebar`            | `oklch(0.985 0 0)`          | `oklch(0.205 0 0)`          | 侧边栏背景                                         |

使用匹配的 Tailwind 工具类：`bg-brand`、`text-brand`、`bg-success`、
`text-warning` 等（通过 `@theme inline` 映射）。高程工具类 `shadow-xs`、
`shadow-sm`、`shadow-md`、`shadow-lg` 由 `--shadow-*` token 驱动。

### 动态强调色与颜色模式

`@nerv-iip/ui` 暴露运行时机制（仅提供 composable；切换器 UI 属于 FE-2/FE-3）：

- `useColorMode()` → `{ mode, isDark, toggle, setMode }`：切换 `<html>` 上的 `.dark`，并持久化。
- `useThemeAccent()` → `{ accent, setAccent, reset, presets }`：在运行时重写 `--brand`，并持久化。
- `initTheme()` → 在挂载前于 `main.ts` 调用一次，以便在首次绘制前应用已持久化的选择。

### 状态语义（使用 `NvStatusBadge` / `NvStatusDot` 的 `tone`，不得使用原始 Tailwind 调色板类）

状态呈现统一走 `NvStatusBadge`（胶囊，自带 `NvStatusDot`）或 `NvStatusDot`（行内点），
`tone` 五档；传后端状态字符串给 `value` 可自动解析 tone 与中文标签。

| 意图                          | `tone`    | 不得使用                                            |
| ----------------------------- | --------- | --------------------------------------------------- |
| 活跃 / 健康 / 启用            | `success` | `border-emerald-200 bg-emerald-50 …`                |
| 警告 / 降级                   | `warning` | `text-amber-*`, `text-yellow-*`                     |
| 错误 / 危险 / 报警            | `danger`  | `text-red-*`, `destructive`（不是本组件的 tone 名） |
| 信息 / 进行中                 | `info`    | `text-blue-*`                                       |
| 非活跃 / 禁用 / 未知          | `neutral` | 手写灰色类                                         |

### 应做与禁止事项

- **应当**使用 `bg-primary`、`text-foreground`、`border-border` 等 Tailwind v4 token 工具类。
- **不得**使用原始调色板名称：`bg-blue-600`、`text-gray-500`、`border-zinc-200`。
- **不得**在 `.vue` 文件中的任何位置使用原始十六进制值。
- **不得**在任何新组件中使用 `--legacy-color-*` token。它们只供等待迁移的两个旧 Console 页面（`InstanceTable`、`InstanceDetailPanel`）使用。
- **不得**直接使用 `emerald-*` 或 `amber-*`；应使用带匹配 `tone` 的 `NvStatusBadge`。

---

## 字体排版

拉丁字母与 CJK 混合字体栈，两者都**自行托管**（由 Vite 打包；运行时不请求
`fonts.googleapis.com` 或 CDN）。在 `packages/ui/src/styles/theme.css` 中统一导入一次。

- **拉丁字母 / 数字 → Inter Variable**（`@fontsource-variable/inter`）。清晰的数字可保持
  高密度数据表的可读性。
- **中文 → MiSans**（`misans` npm 包，Xiaomi，Apache-2.0，可免费商用）。
  生成的 `styles/misans.css` 将 MiSans 光学字重重新映射为标准 CSS 字重
  （400/500/600/700），使 `font-normal/medium/semibold/bold` 正确对应；woff2 还按
  `unicode-range` 切分子集（每个字重约 100 个分块），页面只会获取实际显示的字形。

完整字体栈（`--font-sans`）：
`'Inter Variable', 'MiSans', ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif`

> Inter 位于首位，因此拉丁字母和数字由 Inter 渲染；中文则回退到 MiSans。
> 升级 `misans` 后重新生成 `styles/misans.css`；参见 `DESIGN/governance.md` › 字体。

| 层级            | Tailwind 类                               | 用途                                      |
| --------------- | ----------------------------------------- | ----------------------------------------- |
| 页面标题        | `text-2xl font-semibold tracking-tight`   | `<h1>` 层级、页头                         |
| 小节标题        | `text-lg font-semibold`                   | 卡片标题、对话框标题                      |
| 正文            | `text-sm`                                 | 默认表格 / 表单内容                       |
| 说明 / 弱化文本 | `text-sm text-muted-foreground`           | ID、时间戳、提示                          |
| 等宽文本        | `font-mono text-xs text-muted-foreground` | UUID、权限码、技术值                      |
| 标签            | `text-sm font-medium`                     | 表单标签、列标题                          |

- **不得**在作用域 CSS 中编写自定义 `font-size`；应使用上表尺度。
- **不得**使用 `text-base`（16px 对高密度数据表过大）。

---

## 间距与布局

- 基础单位：`4px`（Tailwind `1` = `4px`）。
- 页面边距：页面内容区使用 `p-6`（`24px`）。
- 卡片内边距：页头和内容使用 `p-6`，或使用 `NvCardHeader`/`NvCardContent`。
- 表单内堆叠间距：字段之间使用 `gap-4`。
- 工具栏内堆叠间距：`gap-3`。
- 表格密度：使用默认值（不得给 `TableCell` 添加额外 `py-*`）。

栅格布局以 `flex` 为基础；仅在多列表单布局中使用 CSS 网格（CSS Grid，`grid grid-cols-2 gap-4`）。

---

## 动效

动效规范的唯一来源是 `DESIGN/motion-interaction.md`（缓动/时长令牌 `--nv-ease-*` /
`--nv-duration-*`、必备交互状态、减少动效（reduced-motion）降级、提交前自检清单）。本文件不再
重复参数，避免两处漂移。速记：动效传达状态而非装饰；原版 shadcn 组件自带过渡，
不要给它们叠加自定义 `transition-*`。

---

## 数据排版（B 端数据页的隐形一致性）

制造业控制台里数字是主角，以下口径全端统一（现网既有约定的成文化）：

| 规则        | 写法                                                                                                      |
| ----------- | --------------------------------------------------------------------------------------------------------- |
| 数值列      | 表格内**右对齐** + `tabular-nums`（`NvDataTable` 列用 `align: 'right'`）                                  |
| 数量精度    | 最多 3 位小数：`value.toLocaleString(undefined, { maximumFractionDigits: 3 })`                            |
| 数量 + 单位 | 数值后接**空格 + UOM 码**：`128.5 kg`；单位不并入数字色/粗细                                              |
| KPI 大数    | `tabular-nums tracking-tight`（`NvMetricCard`/`NvStatTile` 已内置）                                       |
| 空值        | 统一 `—`（长破折号）；不用 `0`、空串、`N/A` 混用——`0` 是数据，`—` 是没有数据                               |
| 编号/技术值 | `font-mono text-xs text-muted-foreground`（单号、批次、UUID、权限码）                                     |
| 时间        | 表格/详情用**绝对时间**（本地时区、到分钟）；相对时间（"3 分钟前"）只用于事件/通知流，且 tooltip 提供绝对值 |
| 超长文本    | `truncate` + `NvTooltip` 全文；不许撑破列宽                                                               |

---

## 图标规范

图标库：**@lucide/vue**（已是对等依赖）。

| 场景                     | 尺寸 | Tailwind 类    |
| ------------------------ | ---- | -------------- |
| 与文本同行               | 16px | `size-4`       |
| 按钮图标                 | 16px | `size-4`       |
| 空状态插图               | 48px | `size-12`      |
| 导航项                   | 20px | `size-5`       |

- 每个文件只导入实际使用的图标：`import { SearchIcon } from '@lucide/vue'`。
- 装饰性图标必须始终添加 `aria-hidden="true"`。
- 纯图标按钮必须始终添加 `aria-label`。

---

## 圆角

| Token                      | 值         | 用途                           |
| -------------------------- | ---------- | ------------------------------ |
| `--radius-sm`              | `0.25rem`  | 输入框、badge                  |
| `--radius-md`              | `0.375rem` | 按钮                           |
| `--radius-lg` / `--radius` | `0.5rem`   | 卡片、对话框、表格、面板       |
| `--radius-xl`              | `0.625rem` | 大型模态覆盖层                 |

卡片 / 面板使用 `rounded-lg`，按钮使用 `rounded-md`，badge 使用 `rounded-4xl`（由组件自身处理）。
