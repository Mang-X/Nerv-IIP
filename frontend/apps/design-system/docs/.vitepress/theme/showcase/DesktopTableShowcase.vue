<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  NvButton,
  NvPagination,
  NvDataTable,
  NvDataTableToolbar,
  nvMessage,
  NvPopconfirm,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { ListFilterIcon, PlusIcon } from '@lucide/vue'
import { ref } from 'vue'
// ---- NvDataTable (toolbar · filter · column settings · paginate) ----
interface WoRow {
  code: string
  product: string
  center: string
  owner: string
  qty: number
  status: string
}
const WO_PRODUCTS = [
  '前桥壳体 A2',
  '转向节 L',
  '齿轮箱端盖',
  '液压阀体 V3',
  '电机定子叠片',
  '制动卡钳',
  '减速器壳体',
  '半轴齿轮',
  '油泵端盖',
  '冷却水套',
  '法兰盘 D80',
  '同步带轮',
]
const WO_CENTERS = ['WC-CNC-07', 'WC-FORGE-02', 'WC-CNC-11', 'WC-ASM-04', 'WC-STAMP-01']
const WO_OWNERS = ['张伟', '李娜', '王强', '刘洋', '陈静']
const WO_STATUS = ['running', 'completed', 'ready', 'blocked', 'pending']
const WO_QTYS = [480, 1200, 320, 640, 5000, 260, 180, 900, 420, 760, 1500, 340]
// Deterministic seed (no random) so the showcase renders identically every time.
const tableAll: WoRow[] = Array.from({ length: 48 }, (_, i) => ({
  code: `WO-2406-${String(401 + i * 3).padStart(4, '0')}`,
  product: WO_PRODUCTS[i % WO_PRODUCTS.length],
  center: WO_CENTERS[i % WO_CENTERS.length],
  owner: WO_OWNERS[i % WO_OWNERS.length],
  qty: WO_QTYS[(i * 5) % WO_QTYS.length],
  status: WO_STATUS[(i * 2) % WO_STATUS.length],
}))
const tableColumns: NvDataTableColumn<WoRow>[] = [
  {
    key: 'code',
    header: '工单号',
    sortable: true,
    filter: 'text',
    cellClass: 'font-mono text-xs',
    hideable: false,
  },
  { key: 'product', header: '产品', sortable: true, filter: 'text', cellClass: 'font-medium' },
  {
    key: 'center',
    header: '工作中心',
    filter: 'enum',
    cellClass: 'font-mono text-xs text-muted-foreground',
  },
  { key: 'owner', header: '负责人', filter: 'enum' },
  { key: 'qty', header: '数量', align: 'end', sortable: true, cellClass: 'tabular-nums' },
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
const tableSelected = ref<(string | number)[]>(['WO-2406-0401'])
const tableTabs = [
  { label: '全部', value: '' },
  { label: '执行中', value: 'running' },
  { label: '待处理', value: 'pending' },
  { label: '已完成', value: 'completed' },
  { label: '阻塞', value: 'blocked' },
]
function onTableRefresh() {
  nvMessage.success('已刷新工单列表')
}

// ---- 独立操作栏 NvToolbar 演示 ----
const tbSearch = ref('')
const tbTab = ref('running')
const tbDensity = ref<'comfortable' | 'compact'>('comfortable')
const tbTabs = [
  { label: '全部', value: 'all', count: 48 },
  { label: '执行中', value: 'running', count: 12 },
  { label: '待处理', value: 'pending', count: 9 },
  { label: '已完成', value: 'completed', count: 18 },
  { label: '已归档', value: 'archived', count: 9 },
]

// ---- 独立分页 Pagination 演示 ----
const pgManyPage = ref(8)
const pgManySize = ref(10)
const pgFewPage = ref(1)
const pgFewSize = ref(10)
</script>

<template>
  <!-- NvDataTable：完整数据表（工具栏 + 字段筛选 + 列设置 + 密度 + 行选择 + 页码分页） -->
  <div class="mt-8 mb-3">
    <h3 class="text-sm font-semibold">数据表格 NvDataTable</h3>
    <p class="mt-1 text-xs text-muted-foreground">
      工具栏 + 快捷筛选标签 + 字段筛选 + 列显隐 + 密度 + 行选择 + 可点击页码 —— 客户端处理
      {{ tableAll.length }} 条工单。
    </p>
  </div>
  <NvDataTable
    :columns="tableColumns"
    :rows="tableAll"
    row-key="code"
    title="工单列表"
    description="近 30 天投放产线的全部工单"
    :tabs="tableTabs"
    tab-key="status"
    selectable
    refreshable
    search-placeholder="搜索工单号 / 产品 / 工作中心…"
    :page-size="10"
    v-model:selected="tableSelected"
    @refresh="onTableRefresh"
  >
    <template #cell-status="{ value }">
      <NvStatusBadge :value="String(value)" :pulse="value === 'running'" />
    </template>
    <template #bulk-actions>
      <NvButton variant="outline" size="sm">导出所选</NvButton>
      <NvPopconfirm
        :title="`确认作废选中的 ${tableSelected.length} 张工单？`"
        description="作废后不可恢复，已领用物料需手动退库。"
        confirm-text="作废"
        @confirm="nvMessage.error('已作废所选工单')"
      >
        <NvButton variant="outline" size="sm">作废</NvButton>
      </NvPopconfirm>
      <NvButton variant="brand" size="sm">下发排产</NvButton>
    </template>
    <template #actions>
      <NvButton variant="brand" size="sm">
        <template #leading><PlusIcon /></template>
        新建工单
      </NvButton>
    </template>
  </NvDataTable>

  <!-- 操作栏 NvToolbar（独立）-->
  <div class="mt-8 mb-3">
    <h3 class="text-sm font-semibold">操作栏 NvToolbar</h3>
    <p class="mt-1 text-xs text-muted-foreground">
      可独立使用：标题 + 实时计数、快捷筛选分段、搜索、字段筛选槽、密度、刷新、更多菜单（导出 /
      打印）与主操作。
    </p>
  </div>
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
    @print="nvMessage.info('已发送到打印机')"
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

  <!-- 分页 Pagination（独立）-->
  <div class="mt-8 mb-3">
    <h3 class="text-sm font-semibold">分页 Pagination</h3>
    <p class="mt-1 text-xs text-muted-foreground">
      可独立使用：可点击页码、首末 / 上下页、多页自动省略号（… 悬停跳 5 页）、每页条数与跳页。
    </p>
  </div>
  <div class="space-y-4">
    <div class="rounded-xl border bg-card px-3 py-3 shadow-sm sm:px-4">
      <p class="mb-2.5 text-xs text-muted-foreground">多页（528 条 · 53 页，含省略号 + 跳页）</p>
      <NvPagination
        :page="pgManyPage"
        :page-size="pgManySize"
        :total-items="528"
        show-jump
        @update:page="pgManyPage = $event"
        @update:page-size="pgManySize = $event"
      />
    </div>
    <div class="rounded-xl border bg-card px-3 py-3 shadow-sm sm:px-4">
      <p class="mb-2.5 text-xs text-muted-foreground">少页（36 条 · 4 页，无省略号）</p>
      <NvPagination
        :page="pgFewPage"
        :page-size="pgFewSize"
        :total-items="36"
        :page-size-options="[10, 20, 50]"
        @update:page="pgFewPage = $event"
        @update:page-size="pgFewSize = $event"
      />
    </div>
  </div>
</template>
