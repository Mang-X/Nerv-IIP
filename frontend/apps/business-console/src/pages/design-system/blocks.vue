<script setup lang="ts">
import type { NvDataTableColumn, NvDataTableSort } from '@nerv-iip/ui'
import {
  NvAppShellInset,
  Button,
  NvDataTable,
  NvPagination,
  DropdownMenuItem,
  NvPageHeader,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  NvStatusBadge,
  NvThemePicker,
  NvThemeToggle,
  NvToolbar,
} from '@nerv-iip/ui'
import { BoxesIcon, FactoryIcon, GaugeIcon, LayersIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: false, title: '设计系统 · Blocks' } })

interface WorkOrderRow {
  code: string
  product: string
  qty: number
  progress: number
  status: string
}

const allRows: WorkOrderRow[] = [
  { code: 'WO-24080', product: '减振器总成 A', qty: 1200, progress: 64, status: 'running' },
  { code: 'WO-24081', product: '活塞杆 B', qty: 800, progress: 100, status: 'completed' },
  { code: 'WO-24082', product: '阀片组件 C', qty: 540, progress: 0, status: 'blocked' },
  { code: 'WO-24083', product: '导向器 D', qty: 2000, progress: 32, status: 'ready' },
  { code: 'WO-24084', product: '油封 E', qty: 360, progress: 12, status: 'pending' },
  { code: 'WO-24085', product: '缓冲块 F', qty: 950, progress: 78, status: 'running' },
]

const search = ref('')
const sort = ref<NvDataTableSort | null>({ key: 'code', direction: 'asc' })
const page = ref(1)
const pageSize = ref('5')

const filtered = computed(() => {
  const kw = search.value.trim().toLowerCase()
  if (!kw) return allRows
  return allRows.filter((r) => `${r.code} ${r.product}`.toLowerCase().includes(kw))
})
const pageSizeNum = computed(() => Number(pageSize.value) || 5)
const paged = computed(() => {
  const start = (page.value - 1) * pageSizeNum.value
  return filtered.value.slice(start, start + pageSizeNum.value)
})

const columns: NvDataTableColumn<WorkOrderRow>[] = [
  { key: 'code', header: '工单号', sortable: true, width: 'w-32', cellClass: 'font-medium' },
  { key: 'product', header: '产品', sortable: true },
  { key: 'qty', header: '数量', align: 'end', sortable: true, width: 'w-24' },
  { key: 'progress', header: '进度', align: 'end', sortable: true, width: 'w-28' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'actions', header: '', align: 'end', width: 'w-12' },
]

const navItems = [
  { title: '生产驾驶舱', icon: GaugeIcon, active: true },
  { title: '工单', icon: FactoryIcon },
  { title: '物料齐套', icon: BoxesIcon },
  { title: '工艺版本', icon: LayersIcon },
]
</script>

<template>
  <NvAppShellInset>
    <template #sidebar-header>
      <div class="flex items-center gap-2 px-1 py-1.5">
        <div
          class="flex size-8 items-center justify-center rounded-lg bg-sidebar-primary text-sm font-extrabold text-sidebar-primary-foreground"
        >
          N
        </div>
        <span class="truncate text-sm font-semibold">Nerv-IIP</span>
      </div>
    </template>

    <template #sidebar>
      <SidebarGroup>
        <SidebarGroupLabel>制造执行</SidebarGroupLabel>
        <SidebarMenu>
          <SidebarMenuItem v-for="item in navItems" :key="item.title">
            <SidebarMenuButton :is-active="item.active">
              <component :is="item.icon" />
              <span>{{ item.title }}</span>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarGroup>
    </template>

    <template #header>
      <span class="text-sm font-semibold">设计系统 · Block 组件库</span>
      <div class="ml-auto flex items-center gap-1">
        <NvThemePicker />
        <NvThemeToggle />
      </div>
    </template>

    <NvPageHeader
      title="生产驾驶舱"
      :breadcrumbs="[{ label: '制造执行', href: '#' }]"
      :count="`${filtered.length} 个工单`"
    >
      <template #actions>
        <Button variant="outline" size="sm">导出</Button>
        <Button size="sm">新建工单</Button>
      </template>
    </NvPageHeader>

    <NvSectionCards>
      <NvSectionCard
        description="在制工单"
        :value="42"
        :trend="{ value: '+8.2%', direction: 'up' }"
        footnote="较上周稳步提升"
        hint="本周新开 12 单"
      />
      <NvSectionCard
        description="按时完工率"
        value="94.6%"
        :trend="{ value: '+1.4%', direction: 'up' }"
        footnote="高于目标 92%"
        hint="近 30 天"
      />
      <NvSectionCard
        description="受阻工单"
        :value="3"
        :trend="{ value: '-2', direction: 'down' }"
        footnote="齐套待解决"
        hint="物料短缺 2 单"
      />
      <NvSectionCard
        description="设备综合效率"
        value="81.3%"
        :trend="{ value: '0.0%', direction: 'flat' }"
        footnote="与昨日持平"
        hint="OEE"
      />
    </NvSectionCards>

    <NvToolbar v-model:search="search" search-placeholder="按工单号或产品搜索">
      <template #actions>
        <Button variant="outline" size="sm">筛选</Button>
      </template>
    </NvToolbar>

    <NvDataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="paged"
      row-key="code"
      empty-message="未找到匹配的工单。"
    >
      <template #cell-qty="{ value }">
        <span class="tabular-nums">{{ value }}</span>
      </template>
      <template #cell-progress="{ value }">
        <span class="tabular-nums">{{ value }}%</span>
      </template>
      <template #cell-status="{ value }">
        <NvStatusBadge :value="String(value)" />
      </template>
      <template #cell-actions>
        <NvRowActions>
          <DropdownMenuItem>查看详情</DropdownMenuItem>
          <DropdownMenuItem>派工</DropdownMenuItem>
          <DropdownMenuItem variant="destructive">关闭工单</DropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvPagination
      v-model:page="page"
      v-model:page-size="pageSize"
      :total-items="filtered.length"
      :page-size-options="[5, 10, 20]"
    />
  </NvAppShellInset>
</template>
