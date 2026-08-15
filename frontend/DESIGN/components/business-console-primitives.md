# Business Console 原语

这些原语填补了 #143 中 Business Console 密集详情页及相邻编辑界面的能力缺口。
名称已更新为 NvUI（ADR 0020）的当前状态。

## 导出项

- `NvTabs`, `NvTabsList`, `NvTabsTrigger`, `NvTabsContent`
- `NvSheet`, `NvSheetTrigger`, `NvSheetContent`, `NvSheetHeader`, `NvSheetTitle`, `NvSheetDescription`, `NvSheetFooter`, `NvSheetClose`
- `Popover`, `PopoverTrigger`, `PopoverContent`, `PopoverAnchor` — 原版继续作为规范名称（尚无 `NvPopover`）；多数弹出层需求已由 `NvSelect` / `NvSearchSelect` / `NvCombobox` / `NvDatePicker` / `NvPopconfirm` 覆盖
- `Progress` — 原版继续作为规范名称（尚未完成 Nv 品牌层重建）
- `ScrollArea`, `ScrollBar` — 原版继续作为规范名称（尚未完成 Nv 品牌层重建）

## 契约

1. Tabs 用于同一对象详情中的并列分区，不得用于应用级导航。
2. Sheet 应保留列表上下文，用于相邻的详情、检查和编辑面板；列表旁较长的表单应优先使用 `NvSheet`，而非 `NvDialog`。
3. Popover 用于紧凑的锚定控件；模态工作流应使用 `NvDialog` 或 `NvSheet`。
4. Progress 表示数值化的工作完成度；状态标签仍应使用 `NvStatusBadge`。
5. ScrollArea 用于受限列表和面板，不得用于整页滚动。

## 规则

所有部件均应从 `@nerv-iip/ui` 导入。应用代码不得深层导入 shadcn、
`reka-ui` 或 `packages/ui/src/components/ui/*`。
