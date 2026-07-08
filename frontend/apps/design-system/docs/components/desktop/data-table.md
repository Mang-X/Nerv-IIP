---
title: DataTable 数据表格
pageClass: ds-wide
aside: false
---

<script setup>
import {
  NvDataTable,
  NvDataTableToolbar,
  NvDataTablePagination,
  NvButton,
  NvStatusBadge,
  messagePro,
} from '@nerv-iip/ui'
import { PlusIcon, ListFilterIcon } from 'lucide-vue-next'
import { ref } from 'vue'

const columns = [
  { key: 'code', header: '工单号', width: '160px', sortable: true, filter: 'text', cellClass: 'font-mono text-xs', hideable: false },
  { key: 'product', header: '产品', sortable: true, filter: 'text', cellClass: 'font-medium' },
  { key: 'center', header: '工作中心', width: '140px', filter: 'enum', cellClass: 'font-mono text-xs text-muted-foreground' },
  { key: 'owner', header: '负责人', width: '110px', filter: 'enum' },
  { key: 'qty', header: '数量', width: '110px', align: 'end', sortable: true, cellClass: 'tabular-nums' },
  {
    key: 'status',
    header: '状态',
    filter: 'enum',
    filterOptions: [
      { label: '执行中', value: 'running' },
      { label: '已完成', value: 'completed' },
      { label: '可开工', value: 'ready' },
      { label: '阻塞', value: 'blocked' },
      { label: '待处理', value: 'pending' },
    ],
  },
]

const PRODUCTS = ['前桥壳体 A2', '转向节 L', '齿轮箱端盖', '液压阀体 V3', '电机定子叠片', '制动卡钳']
const CENTERS = ['WC-CNC-07', 'WC-FORGE-02', 'WC-CNC-11', 'WC-ASM-04', 'WC-STAMP-01']
const OWNERS = ['张伟', '李娜', '王强', '刘洋', '陈静']
const STATUS = ['running', 'completed', 'ready', 'blocked', 'pending']
const QTYS = [480, 1200, 320, 640, 5000, 260, 180, 900]
const rows = Array.from({ length: 24 }, (_, i) => ({
  code: `WO-2406-${String(401 + i * 3).padStart(4, '0')}`,
  product: PRODUCTS[i % PRODUCTS.length],
  center: CENTERS[i % CENTERS.length],
  owner: OWNERS[i % OWNERS.length],
  qty: QTYS[(i * 5) % QTYS.length],
  status: STATUS[(i * 2) % STATUS.length],
}))

const tabs = [
  { label: '全部', value: '' },
  { label: '执行中', value: 'running' },
  { label: '待处理', value: 'pending' },
  { label: '已完成', value: 'completed' },
]
const selected = ref(['WO-2406-0401'])

const tbSearch = ref('')
const tbTab = ref('running')
const tbDensity = ref('comfortable')
const tbTabs = [
  { label: '全部', value: 'all', count: 48 },
  { label: '执行中', value: 'running', count: 12 },
  { label: '待处理', value: 'pending', count: 9 },
  { label: '已完成', value: 'completed', count: 18 },
]

const page = ref(8)
const pageSize = ref(10)
</script>

# DataTable 数据表格

完整的高级数据表体验。`NvDataTable` 内置工具栏（搜索 · 字段筛选 · 列显隐 · 密度）、可排序表头、行选择与可点击页码分页；默认在客户端处理筛选/排序/分页。工具栏 `NvDataTableToolbar` 与分页 `NvDataTablePagination` 也可独立使用。

## 完整表格

<Demo block>
  <NvDataTable
    :columns="columns"
    :rows="rows"
    row-key="code"
    title="工单列表"
    description="近 30 天投放产线的全部工单"
    :tabs="tabs"
    tab-key="status"
    selectable
    refreshable
    search-placeholder="搜索工单号 / 产品 / 工作中心…"
    :page-size="8"
    v-model:selected="selected"
    @refresh="messagePro.success('已刷新工单列表')"
  >
    <template #cell-status="{ value }">
      <NvStatusBadge :value="String(value)" :pulse="value === 'running'" />
    </template>
    <template #bulk-actions>
      <NvButton variant="outline" size="sm">导出所选</NvButton>
      <NvButton variant="brand" size="sm">下发排产</NvButton>
    </template>
    <template #actions>
      <NvButton variant="brand" size="sm">
        <template #leading><PlusIcon /></template>
        新建工单
      </NvButton>
    </template>
  </NvDataTable>
</Demo>

```vue
<NvDataTable
  :columns="columns"
  :rows="rows"
  row-key="code"
  title="工单列表"
  :tabs="tabs"
  tab-key="status"
  selectable
  refreshable
  :page-size="8"
  v-model:selected="selected"
  @refresh="onRefresh"
>
  <template #cell-status="{ value }">
    <NvStatusBadge :value="String(value)" :pulse="value === 'running'" />
  </template>
  <template #actions>
    <NvButton variant="brand" size="sm">新建工单</NvButton>
  </template>
</NvDataTable>
```

## 操作栏 Toolbar

<Demo block>
  <NvDataTableToolbar
    v-model:search="tbSearch"
    v-model:tab="tbTab"
    v-model:density="tbDensity"
    title="工单列表"
    :count="48"
    :tabs="tbTabs"
    searchable
    search-placeholder="搜索工单…"
    show-density
    refreshable
    show-more
    @refresh="messagePro.info('正在刷新…')"
    @export="messagePro.success('已导出 CSV')"
  >
    <template #filters>
      <NvButton variant="outline" size="sm">
        <template #leading><ListFilterIcon /></template>
        筛选
      </NvButton>
    </template>
    <template #actions>
      <NvButton variant="brand" size="sm">
        <template #leading><PlusIcon /></template>
        新建工单
      </NvButton>
    </template>
  </NvDataTableToolbar>
</Demo>

```vue
<NvDataTableToolbar
  v-model:search="search"
  v-model:tab="tab"
  v-model:density="density"
  title="工单列表"
  :count="48"
  :tabs="tabs"
  searchable
  show-density
  refreshable
/>
```

## 分页 Pagination

<Demo block>
  <NvDataTablePagination
    :page="page"
    :page-size="pageSize"
    :total-items="528"
    show-jump
    @update:page="page = $event"
    @update:page-size="pageSize = $event"
  />
</Demo>

```vue
<NvDataTablePagination
  :page="page"
  :page-size="pageSize"
  :total-items="528"
  show-jump
  @update:page="page = $event"
  @update:page-size="pageSize = $event"
/>
```

## 属性

### DataTablePro

| 属性              | 说明                                                  | 类型                                  | 默认    |
| ----------------- | ----------------------------------------------------- | ------------------------------------- | ------- |
| `columns`         | 列定义（`key` / `header` / `sortable` / `filter` 等） | `DataTableProColumn[]`                | —       |
| `rows`            | 行数据                                                | `T[]`                                 | —       |
| `rowKey`          | 行主键字段名或取值函数                                | `string \| (row) => string \| number` | —       |
| `selectable`      | 行选择 + 批量操作栏                                   | `boolean`                             | `false` |
| `refreshable`     | 显示刷新按钮（触发 `refresh`）                        | `boolean`                             | `false` |
| `tabs` / `tabKey` | 快捷筛选分段标签及其作用列                            | `{ label, value }[]` / `string`       | —       |
| `pageSize`        | 初始每页条数                                          | `number`                              | —       |
| `selected`        | 选中行主键（`v-model:selected`）                      | `(string \| number)[]`                | —       |

### DataTablePaginationPro

| 属性         | 说明                            | 类型      | 默认    |
| ------------ | ------------------------------- | --------- | ------- |
| `page`       | 当前页（`v-model:page`）        | `number`  | —       |
| `pageSize`   | 每页条数（`v-model:page-size`） | `number`  | —       |
| `totalItems` | 总条数                          | `number`  | —       |
| `showJump`   | 显示跳页输入                    | `boolean` | `false` |
