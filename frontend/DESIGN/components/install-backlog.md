# 建议安装的组件

曾经缺失组件的状态台账。已对照 `packages/ui/src/index.ts` 与 `pc/` 层核验
（ADR-0020 后的 NvUI 状态）。

## 已交付

- **#143 business-console 组件集** — 现已通过 `@nerv-iip/ui` 以其 NvUI 名称提供：
  `NvTabs*`、`NvSheet*`、`NvDatePicker`、`NvDateRangePicker`、
  `FileUpload`，以及原版规范组件 `Popover*`、`Progress`、`ScrollArea` 和
  图表层（现面向应用提供 `NvAreaChart` / `NvLineChart` /
  `NvBarChart` / `NvDonutChart`).
- **`command`** — ✅ 已作为 `NvCommand`（pc 层）交付。
- **Combobox 模式** — ✅ 已交付 `NvCombobox`（输入筛选，允许自由输入）和
  `NvSearchSelect`（可搜索的弹窗单选），用于大型数据集（SKU、设备、技术人员）。
- **`breadcrumb`** — ✅ 已作为原版 primitive 安装并从
  `@nerv-iip/ui` 导出（`Breadcrumb*`）；尚未进行 Nv 重建。

## 尚未完成

### `toggle` / `toggle-group`

尚未安装（`components/ui/` 中没有 `toggle`）。**原因**：视图模式切换
（表格与卡片视图）、筛选切换胶囊、图表时间范围选择器。
临时方案：`NvTabs` 快速筛选或 `NvDataTable` 的 `tabs` 可覆盖大多数情形；
大屏/触控层已有 `NvScreenSegmented` / `NvTouchSegmented`。

### `resizable`

用于可调整尺寸的面板布局（例如连接器（connector）配置中的代码编辑器/输出分栏视图）。

### `stepper`（自定义，未包含在 shadcn-vue core 中）

用于多步骤引导流程（注册实例 → 配置 connector → 验证连接）。

## 安装流程（遵循 ADR 0020）

1. 运行 `pnpm dlx shadcn-vue@latest add <name>`（在 `frontend/` 中）：原版组件会
   落在 `packages/ui/src/components/ui/<name>/`，并保持逐字节不变。
2. 从 `packages/ui/src/index.ts` 导出原版部件（库内部基线）。
3. 若应用界面需要它，在匹配的层（`pc/` / `touch/` / `screen/`）中复制重建品牌版本，
   按 ADR 0020 §1.2 R1–R5 命名（通常为 `Nv` + 素名），并从该层 barrel（聚合导出入口）导出；应用代码
   只能使用 `Nv*` 名称。
4. 新增或更新设计系统文档页面（`frontend/apps/design-system/docs/`）和
   契约测试（contract test，`nvui-naming`），并在 `DESIGN/components/<name>.md` 下新增规格说明。
