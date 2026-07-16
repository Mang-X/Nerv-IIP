# Block: Data Table（实体列表表格）

> **现役实现 = `NvDataTable`**（`@nerv-iip/ui`，pc 层
> `packages/ui/src/components/pc/data-table/NvDataTable.vue`）。本文早期版本教的
> 「原版 `<Table>` 手动拼 骨架/空态/行菜单」写法已废弃——业务页禁手搓裸表
> （`goldStandardPages.contract.test.ts` 机器拦截 `<Table` / `<TableHeader`），
> 该历史写法存档于文末附录。

## 规则

1. **业务列表一律 `NvDataTable`**：列配置（`columns: NvDataTableColumn[]`）+
   `#cell-<key>` 单元格槽；不手搓 `<Table>`/`<TableHeader>`。
2. **三态全内建、缺一不可**：加载骨架（`:loading` + `skeletonRows`，默认 6 行）、
   空态（`emptyMessage` 文案或 `#empty` 槽放 CTA）、数据行。绝不在首次加载时闪空态，
   也绝不在数据上盖 loading 遮罩。
3. **空态必须带出路**：`#empty` 槽放［+ 新建 X］/［清空筛选］按钮（口径见
   `interaction-patterns.md` §5.1）；纯 `emptyMessage` 文案至少指名下一步在哪。
4. **行操作**：尾列 `{ key: 'actions', align: 'end' }` + `#cell-actions` 槽；高频动作
   行内 `NvButton size="sm"`，其余收 `NvRowActions`（分级规则见 `interaction-patterns.md` §2）。
5. **确认弹窗放页面层**：`NvAlertDialog` 声明在页面（单实例 + target ref），不塞进表格
   组件、不进 `v-for`。
6. **数据归页面**：`NvDataTable` 只渲染 + 派发事件；服务端分页用 `manual` 模式
   （`v-model:page` + `:total-items` + `:page-size`，1-based），服务端排序传
   `:client-sort="false"` 由页面自己重排全量。
7. **状态列用 `NvStatusBadge`**，不自己画徽标。
8. 关键能力速查：`selectable` + `v-model:selected` + `#bulk-actions`（批量条）、
   `tabs`/`tabKey`（快捷筛选段）、`searchable`/`columnSettings`/`refreshable`（内建工具条）、
   `rowClass`（按行状态弱化/着色）、`stickyHeader`/`maxBodyHeight`。

## 判定

- 「这是业务实体列表吗？」是 → `NvDataTable`；只有极简静态展示表才考虑原版 `<Table>`。
- 「首次加载看到的是骨架吗？拉空后看到的是带出路的空态、还是永远骨架/空白？」
- 「排序/分页是谁做的？」服务端做 → `manual` + `:client-sort="false"`；小数据集客户端做 →
  默认模式即可。
- 「确认弹窗在页面层单实例吗？」在表格里/循环里 → 打回。

## 正例

`apps/business-console/src/pages/mes/operation-tasks.vue:380`（黄金标准页，服务端分页 +
页面所有权排序）：

```vue
<NvDataTable
  manual
  :page="page"
  :page-size="pageSize"
  :total-items="operationTasksTotal"
  @update:page="page = $event"
  @update:page-size="(v) => (pageSize = String(v))"
  v-model:sort="sort"
  :columns="columns"
  :rows="pagedTasks"
  :row-key="rowKey"
  :client-sort="false"
  :loading="operationTasksPending"
  :searchable="false"
  :column-settings="false"
  empty-message="当前没有工序任务。确认工单已释放、排程已生成后，可开工任务会出现在这里。"
>
  <template #cell-workOrderId="{ row }">…</template>
  <template #cell-actions="{ row }">…行内按钮 + NvRowActions…</template>
</NvDataTable>
```

批量选择（组件真实 API，业务页接入规范见 `interaction-patterns.md` §5.2）：

```vue
<NvDataTable :rows="rows" row-key="workOrderId" selectable v-model:selected="selectedIds">
  <template #bulk-actions="{ selected }">
    <NvButton size="sm" variant="outline" @click="bulkRelease(selected)">批量下达</NvButton>
  </template>
</NvDataTable>
```

## 反例

❌ **0 数据时长期呈骨架态而非空态 CTA** —— 设备运行看板 0 设备时左侧设备表长期显示骨架
占位行，用户分不清「在加载」还是「没有数据」，也没有「去注册设备」出路。出处：
`frontend/DESIGN/roadmaps/2026-07-11-ux-walkthrough-findings.md` §3.4 P2-7
（`/equipment` 实机走查 + 截图 `equipment-board.png`）。审批中心 0 待办同型（同文 §3.4）。
按规则 2/3：加载结束必须落到空态，空态必须带出路。

---

## 附录：原版 `<Table>` 手拼写法（已废弃，仅读旧代码时查阅）

历史约定（`NvDataTable` 之前的过渡写法）：外包 `overflow-hidden rounded-lg border
bg-background` 容器；`template v-if / v-else-if / v-else` 切三态；空态用
`<TableEmpty :colspan="N">`；骨架 `<Skeleton>` 尺寸随内容形状（短文本 `h-5 w-32`、
邮箱 `h-5 w-48`、UUID `h-5 w-40`、图标按钮 `h-8 w-8 ml-auto`、状态徽标 `h-5 w-20`）；
行菜单 = `DropdownMenu` + `MoreHorizontalIcon` ghost 按钮。表格组件内禁
`<style scoped>`（全 Tailwind）。这些决策已整体沉淀进 `NvDataTable`/`NvRowActions`，
新代码不得再手拼。
