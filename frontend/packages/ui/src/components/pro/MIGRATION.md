# Pro 层迁移与一致性说明

把 PC 业务页从 base `ui/` 迁到 `pro/` 时的接口差异、变体映射、缺口清单。
本文件是审计产物，结论均按代码事实核实（附 `文件:行号`）。

## 本轮已修复

| 项 | 改动 | 文件 |
| --- | --- | --- |
| 分页 `pageSize` 类型不兼容 | `DataTablePaginationPro.pageSize` 改 `number \| string`，内部统一数值化 → 读侧可直接接 `usePagedList` 的 string `pageSize`（见下方片段） | `data-table/DataTablePaginationPro.vue` |
| `DialogProFooter` 缺 `show-close-button` | 补回该 prop（默认 `false`），开启时渲染 `ButtonPro(outline) 关闭`，对齐 base `DialogFooter` | `dialog/DialogProFooter.vue` |
| `TabsPro` / `TabsProContent` 是裸 reka 再导出 | 补成真包装：`TabsPro` 带回 `flex gap-2 group/tabs data-horizontal:flex-col`，`TabsProContent` 带回 `flex-1 text-sm outline-none`（对齐 base `Tabs`/`TabsContent`） | `tabs/TabsPro.vue`、`tabs/TabsProContent.vue` |

### 配 usePagedList 的分页用法

`usePagedList().pageSize` 是 `string`，组件 `update:pageSize` 发 `number`。读侧已可
直接绑；写回转一下字符串（运行时本就兼容，仅为类型一致）：

```vue
<DataTablePaginationPro
  v-model:page="pl.page"
  :page-size="pl.pageSize"
  :total-items="total"
  @update:page-size="(v) => (pl.pageSize = String(v))"
/>
```

（`pl.page` 是 `number`，与组件 `page` 同型，可直接 `v-model`。）

## Badge 变体映射（base → Pro）

`BadgePro` 是**有意的色彩语义**变体集（非缺陷），与 base `Badge` 不同名。迁移时按下表换名：

| base `Badge` | → `BadgePro` | 说明 |
| --- | --- | --- |
| `default` | `solid` | 实心主色 |
| `secondary` | `neutral` | 中性灰（默认值） |
| `destructive` | `danger` | 危险红 |
| `outline` | `neutral` | Pro 无纯描边，用中性 |
| `success` / `warning` | `success` / `warning` | 同名 |
| （无） | `brand` | Pro 专有：品牌色软填充 |

证据：`pro/badge/BadgePro.vue:16-23`、`ui/badge/index.ts`。

## DataTablePro 工具栏默认（组合提示）

`DataTablePro` 定位是“完整体验”表格，**默认自带工具栏**：
`searchable` 默认 `true`、`columnSettings` 默认 `true`（`pro/data-table/DataTablePro.vue:80,83`）。

→ 若页面**已有**独立 `Toolbar`/筛选条，避免双工具栏，请显式关闭：

```vue
<DataTablePro :searchable="false" :column-settings="false" ... />
```

（默认值保留 `true` 以维持组件“开箱即用”的身份；如需整体改为按需开启，属设计决策，另议。）

## 有意透传（base 纯转发的原语，非缺陷）

下列 Pro 名称是 reka 原语的再导出（已在各 `index.ts` 注释标明）。base 对应物也只是纯转发包装（仅多一个 `data-slot` 属性，无样式/行为），故功能等价，可放心使用：

- `dialog/index.ts`：`DialogPro`(=DialogRoot)、`DialogProTrigger`、`DialogProClose`
- `select/index.ts`：`SelectProGroup`(=SelectGroup)、`SelectProValue`(=SelectValue)
- `tooltip/index.ts`：`TooltipProProvider`、`TooltipPro`(=TooltipRoot)、`TooltipProTrigger`

## Pro 层缺口（迁移时仍需混用 base / blocks）

下列在 `pro/` 暂无对应版本，业务页可继续用 base `ui/` 或 `blocks/`；是否补 Pro 版见“建议”。

| 组件 | pro/ | 现可用来源 | 建议 |
| --- | :---: | --- | --- |
| DropdownMenu* | ❌ | base `ui/dropdown-menu` | 高频，建议补 `DropdownMenuPro`（行操作菜单 RowActions 依赖） |
| Card 子件（Header/Content/Footer/Title/Description） | ⚠️ 仅 `CardPro`/`MetricCardPro` | base `ui/card` | 建议补子件 Pro 版以成套 |
| Field* / FieldGroup / FieldLabel / FieldError | ❌ | base `ui/field` | 表单高频，建议补 |
| AlertDialog* | ❌ | base `ui/alert-dialog` | 建议补（删除确认等） |
| Sheet* | ❌ | base `ui/sheet` | 建议补 |
| DateRangePicker | ❌ | base `ui/date-picker` | 中频，可后置 |
| PageHeader / RowActions / Toolbar / SectionCard(s) | ❌ | `blocks/*` | 属 pattern 层，base `ui/` 也无；按需上移 Pro |

> 这些缺口是后续“补齐 Pro 套件”的工作清单，不阻塞当前迁移（base/blocks 均可用）。
