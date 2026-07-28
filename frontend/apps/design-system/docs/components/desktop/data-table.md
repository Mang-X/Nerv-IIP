---
title: NvDataTable 数据表格
pageClass: nv-wide
---

<script setup>
import {
  NvDataTable,
  NvDataTableToolbar,
  NvPagination,
  NvButton,
  NvStatusBadge,
  nvMessage,
} from '@nerv-iip/ui'
import { PlusIcon, ListFilterIcon } from '@lucide/vue'
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

# NvDataTable 数据表格

完整的高级数据表体验。`NvDataTable` 内置工具栏（搜索 · 字段筛选 · 列显隐 · 密度）、可排序表头、行选择与可点击页码分页；默认在客户端处理筛选/排序/分页。工具栏 `NvDataTableToolbar` 与分页 `NvPagination` 也可独立使用。

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
    @refresh="nvMessage.success('已刷新工单列表')"
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

## 空 / 失败 / 未查询三态

表格必须能表达「请求失败」和「还没查」，不能把它们都塞进空态——一个 500 和「真的
0 条」如果渲染成同一句「暂无数据」，看板上就是在骗人。

- **失败**：传 `:error`（Error / 响应对象 / 现成中文文案），红色警示章 + 具体原因 +
  「重新加载」按钮（触发 `retry`）。失败态里不会出现任何「暂无 / 没有」的安慰话。
- **未查询**：传 `awaiting-scope` + `awaiting-scope-message`，虚线待办章 + 一句话说清
  还要选什么。
- **空**：既没失败也已查过，才用 `empty-message`。

优先级：`error` > `loading` > `awaitingScope` > 有数据 > 空。`error` 与 `loading`
同时为真表示正在重试，仍停在失败态，只把按钮切成「重试中…」。

<Demo block>
  <NvDataTable
    :columns="columns"
    :rows="[]"
    row-key="code"
    title="工单列表"
    :searchable="false"
    :column-settings="false"
    :error="'网关返回 502，未能读取工单列表。'"
    empty-message="暂无工单"
    @retry="nvMessage.info('正在重试…')"
  />
</Demo>

<Demo block>
  <NvDataTable
    :columns="columns"
    :rows="[]"
    row-key="code"
    title="工单列表"
    :searchable="false"
    :column-settings="false"
    awaiting-scope
    awaiting-scope-message="请先选择工厂与工作中心。"
    empty-message="暂无工单"
  />
</Demo>

```vue
<NvDataTable
  :columns="columns"
  :rows="rows"
  row-key="code"
  :loading="pending"
  :error="error"
  :awaiting-scope="!hasSkuSelection"
  awaiting-scope-message="请先选择物料后查询批次。"
  empty-message="这个物料在当前工厂没有批次记录。"
  @retry="refetch"
/>
```

自定义呈现走 `#error` / `#awaiting` / `#empty` 插槽；`#error` 提供
`{ message, error }` 作用域参数。

## 操作栏 NvToolbar

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
    @refresh="nvMessage.info('正在刷新…')"
    @export="nvMessage.success('已导出 CSV')"
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
  <NvPagination
    :page="page"
    :page-size="pageSize"
    :total-items="528"
    show-jump
    @update:page="page = $event"
    @update:page-size="pageSize = $event"
  />
</Demo>

```vue
<NvPagination
  :page="page"
  :page-size="pageSize"
  :total-items="528"
  show-jump
  @update:page="page = $event"
  @update:page-size="pageSize = $event"
/>
```

## 属性

### NvDataTable

| 属性                   | 说明                                                  | 类型                                  | 默认                       |
| ---------------------- | ----------------------------------------------------- | ------------------------------------- | -------------------------- |
| `columns`              | 列定义（`key` / `header` / `sortable` / `filter` 等） | `NvDataTableColumn[]`                 | —                          |
| `rows`                 | 行数据                                                | `T[]`                                 | —                          |
| `rowKey`               | 行主键字段名或取值函数                                | `string \| (row) => string \| number` | —                          |
| `selectable`           | 行选择 + 批量操作栏                                   | `boolean`                             | `false`                    |
| `refreshable`          | 显示刷新按钮（触发 `refresh`）                        | `boolean`                             | `false`                    |
| `tabs` / `tabKey`      | 快捷筛选分段标签及其作用列                            | `{ label, value }[]` / `string`       | —                          |
| `pageSize`             | 初始每页条数                                          | `number`                              | —                          |
| `selected`             | 选中行主键（`v-model:selected`）                      | `(string \| number)[]`                | —                          |
| `loading`              | 加载骨架                                              | `boolean`                             | `false`                    |
| `error`                | 非空即进入失败态（Error / 对象 / 文案）               | `unknown`                             | —                          |
| `errorMessage`         | 覆盖失败态正文（默认取 `error` 的 message）           | `string`                              | —                          |
| `awaitingScope`        | 业务范围未选定 → 未查询态                             | `boolean`                             | `false`                    |
| `awaitingScopeMessage` | 未查询态引导语                                        | `string`                              | `请先在上方选择查询范围。` |
| `emptyMessage`         | 空态文案（仅在已查询且确实 0 条时出现）               | `string`                              | `暂无数据`                 |

事件：`refresh`（工具栏刷新，用户在已有结果上主动要最新数据）与 `retry`（失败后的
恢复动作）语义分开，互不触发。

### 列定义

| 属性          | 说明                                           | 类型     | 默认 |
| ------------- | ---------------------------------------------- | -------- | ---- |
| `key`         | 稳定列键                                       | `string` | —    |
| `header`      | 可见列头                                       | `string` | —    |
| `headerTitle` | 可聚焦的列头帮助提示，支持悬停、键盘与触屏触发 | `string` | —    |

`headerTitle` 是桌面端的轻量补充提示，不应承载完成操作所必需的信息。组件会为该列
渲染可聚焦帮助触发器；核心列名和状态仍必须脱离提示独立可读。

### NvPagination

| 属性         | 说明                            | 类型      | 默认    |
| ------------ | ------------------------------- | --------- | ------- |
| `page`       | 当前页（`v-model:page`）        | `number`  | —       |
| `pageSize`   | 每页条数（`v-model:page-size`） | `number`  | —       |
| `totalItems` | 总条数                          | `number`  | —       |
| `showJump`   | 显示跳页输入                    | `boolean` | `false` |
