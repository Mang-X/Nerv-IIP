# 设计

本文定义 Nerv-IIP 设计系统的视觉体系。令牌的事实源是 `frontend/packages/ui/src/styles/theme.css`；不得硬编码数值，必须引用语义令牌。实时文档位于 `frontend/apps/design-system/docs` 下的 VitePress 站点。

## 主题

工业控制台以深色为先（近黑基底），并通过 `<html>` 上的 `.dark` 类完整支持亮色模式。整体采用克制的 Vercel/Linear 工艺感：低色度表面、发丝级描边和单一动态强调色。配色策略为**克制**：带色倾向的中性色加一个品牌强调色。

## 颜色（OKLCH 语义令牌）

- 表面（深色）：`--background` `oklch(0.145 0 0)` → `--card` / `--popover` `oklch(0.205 0 0)` → `--muted` `oklch(0.269 0 0)`。亮色模式以高亮度值镜像对应。
- 文本：`--foreground`（深色模式下接近白色）、`--muted-foreground`，以及用于强调的高对比度 `--*-strong` 变体。
- 品牌：`--brand` 是写入 `<html>` 的**运行时动态强调色**（默认为 `oklch(0.54 0.16 256)` 蓝色）；另有 `--brand-strong` / `--brand-foreground`。预设使用由 12 个等亮度色相组成的 OKLCH 色轮。
- 语义：`--success`、`--warning`、`--destructive`（降低饱和度且固定，不参与动态强调色变化）。线条使用 `--border`、`--ring`。
- 规则：禁止在带色背景上使用灰色正文；文本应向墨色提高对比度。正文对比度必须 ≥4.5:1。

## 字体排印

- 字体族：以 Inter（无衬线）为基础；代码（WO-/WC-）和表格数字使用等宽字体。字体族不超过 3 种。
- 通过字号和字重建立层级（标题使用半粗体，正文使用常规字重）。展示文字的字间距 ≥ -0.04em；主视觉字号的 clamp 最大值 ≤6rem。标题使用 `text-wrap: balance`。
- 数字使用 `tabular-nums`。正文不得全部使用大写字母；大写仅用于短标签。

## 动效

theme.css 中的缓动令牌为：`--ease-out-quart`（通用）、`--ease-out-expo`（滑动/吸附/回弹）和 `--ease-in-out-quart`（指示器）。不得使用弹跳或弹性缓动。SwipeCell、BottomSheet、PullRefresh 统一使用橡皮筋拖拽曲线 `abs(x)^0.92*0.7`。触控反馈优先使用背景或透明度变化，而不是 `transform: scale`（避免布局位移）。每个动画都必须提供 `prefers-reduced-motion` 降级方案。

## 组件

组件分为三层，全部由令牌驱动，且**绝不修改原版** shadcn/reka：

- `@nerv-iip/ui`：桌面端/PC。由 shadcn/reka 基础层、复制重建的高品质 **Pro** 组件（Button/Input/Select/DataTable/Descriptions/Timeline/Tabs/Dialog/Popconfirm/Tooltip/charts……）、**区块**（app-shell、page-header、section-card、toolbar、data-table）、**布局**（Container/Page/PageGrid/PageColumns/PageSection）和供一体机使用的**触控组件**（StationBar/StatTile/QtyStepper）组成。
- `@nerv-iip/ui-mobile`：PDA。提供具有原生手感和手势支持的控件（swipe-cell、pull-refresh、bottom-sheet 拖拽关闭）、安全区适配、≥44px 触控目标与玻璃质感覆盖层。
- 卡片圆角上限为 12–16px；使用发丝级 `0 0 0 1px var(--border)` 描边环加轻微内侧顶部高光，不使用边框叠加重阴影。覆盖层仅使用克制的玻璃效果（半透明加背景模糊）。

## 布局

网格应适配 12 列；`Container` 通过响应式内边距约束宽度；`Page` 使用由内容区和侧栏组成的 10 列网格。一维（1D）布局使用 Flex，二维（2D）布局使用 Grid。语义化 z-index 层级依次为 dropdown → sticky → modal-backdrop → modal → toast → tooltip。
