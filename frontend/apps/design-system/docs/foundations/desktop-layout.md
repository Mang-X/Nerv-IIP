# 桌面排版与布局

以下尺度用于桌面 PC。其他使用环境见[四个表面](/guide/surfaces)。

## 字体排版

拉丁字母和数字使用由 Vite 打包的 Inter Variable。中文使用操作系统字体，例如 macOS 的苹方或 Windows 的微软雅黑。共享字体栈 `--font-sans` 由[主题样式](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/styles/theme.css)定义。

| 层级            | Tailwind 类                               | 用途                 |
| --------------- | ----------------------------------------- | -------------------- |
| 页面标题        | `text-2xl font-semibold tracking-tight`   | `<h1>` 层级、页头    |
| 小节标题        | `text-lg font-semibold`                   | 卡片标题、对话框标题 |
| 正文            | `text-sm`                                 | 默认表格 / 表单内容  |
| 说明 / 弱化文本 | `text-sm text-muted-foreground`           | ID、时间戳、提示     |
| 等宽文本        | `font-mono text-xs text-muted-foreground` | UUID、权限码、技术值 |
| 标签            | `text-sm font-medium`                     | 表单标签、列标题     |

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

栅格布局以 `flex` 为基础；多列布局可使用 CSS Grid。页面的容器宽度、响应式边距和内容加侧栏布局优先使用 [Container](/components/desktop/container)、[Page](/components/desktop/page) 和 [PageGrid](/components/desktop/page-grid)。

---

## 数据排版（B 端数据页的隐形一致性）

制造业控制台里数字是主角，桌面业务表格和详情采用以下口径：

| 规则        | 写法                                                                                                        |
| ----------- | ----------------------------------------------------------------------------------------------------------- |
| 数值列      | 表格内**右对齐** + `tabular-nums`（`NvDataTable` 列用 `align: 'right'`）                                    |
| 数量精度    | 最多 3 位小数：`value.toLocaleString(undefined, { maximumFractionDigits: 3 })`                              |
| 数量 + 单位 | 数值后接**空格 + UOM 码**：`128.5 kg`；单位不并入数字色/粗细                                                |
| KPI 大数    | `tabular-nums tracking-tight`（`NvMetricCard`/`NvStatTile` 已内置）                                         |
| 空值        | 统一 `—`（长破折号）；不用 `0`、空串、`N/A` 混用——`0` 是数据，`—` 是没有数据                                |
| 编号/技术值 | `font-mono text-xs text-muted-foreground`（单号、批次、UUID、权限码）                                       |
| 时间        | 表格/详情用**绝对时间**（本地时区、到分钟）；相对时间（"3 分钟前"）只用于事件/通知流，且 tooltip 提供绝对值 |
| 超长文本    | `truncate` + `NvTooltip` 全文；不许撑破列宽                                                                 |

---

## 图标规范

图标库：**@lucide/vue**（已是对等依赖）。

| 场景       | 尺寸 | Tailwind 类 |
| ---------- | ---- | ----------- |
| 与文本同行 | 16px | `size-4`    |
| 按钮图标   | 16px | `size-4`    |
| 空状态插图 | 48px | `size-12`   |
| 导航项     | 20px | `size-5`    |

- 每个文件只导入实际使用的图标：`import { SearchIcon } from '@lucide/vue'`。
- 装饰性图标必须始终添加 `aria-hidden="true"`。
- 纯图标按钮必须始终添加 `aria-label`。

---

## 圆角与高程

圆角从 `rounded-sm`、`rounded-md`、`rounded-lg`、`rounded-xl` 等语义层级选择。使用现有组件时以其变体为准，例如 [NvCard](/components/desktop/card) 和 [NvDialog](/components/desktop/dialog)。使用共享阴影层级表达叠放关系。
