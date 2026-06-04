<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { useBusinessMasterDataGroups, type BusinessMasterDataGroupDefinition } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '工厂资源' } })

type ResourceRow = BusinessConsoleResourceItem & { groupTitle: string }

const resourceDefinitions: BusinessMasterDataGroupDefinition[] = [
  { key: 'site', title: '工厂' },
  { key: 'production-line', title: '产线' },
  { key: 'work-center', title: '工作中心' },
  { key: 'device-asset', title: '设备' },
  { key: 'shift', title: '班次' },
  { key: 'work-calendar', title: '工作日历' },
  { key: 'team', title: '班组' },
  { key: 'personnel-skill', title: '人员技能' },
]
const resourceTitleByKey = Object.fromEntries(resourceDefinitions.map((d) => [d.key, d.title]))

const { groups, groupsError, groupsPending, refreshGroups } = useBusinessMasterDataGroups(resourceDefinitions)

const keyword = ref('')
const groupFilter = ref('all')
const sort = ref<DataTableSort | null>({ key: 'code', direction: 'asc' })
const page = ref(1)
const pageSize = ref('10')

const allRows = computed<ResourceRow[]>(() =>
  groups.value.flatMap((group) =>
    group.rows.map((row) => ({ ...row, groupTitle: resourceTitleByKey[group.key] ?? group.title })),
  ),
)
const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return allRows.value.filter((row) => {
    const groupMatched = groupFilter.value === 'all' || row.resourceType === groupFilter.value
    const kwMatched =
      !kw ||
      [row.resourceType, row.code, row.displayName, row.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(kw))
    return groupMatched && kwMatched
  })
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort((a, b) =>
    String(a[key as keyof ResourceRow] ?? '')
      .localeCompare(String(b[key as keyof ResourceRow] ?? ''), 'zh-Hans-CN') * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSizeNumber.value
  return sortedRows.value.slice(start, start + pageSizeNumber.value)
})

function countOf(type: string) {
  return allRows.value.filter((r) => r.resourceType === type).length
}
const errorMessage = computed(() => formatError(groupsError.value))

const columns: DataTableColumn<ResourceRow>[] = [
  { key: 'groupTitle', header: '类型', sortable: true, width: 'w-28' },
  { key: 'code', header: '编码', sortable: true, cellClass: 'font-medium' },
  { key: 'displayName', header: '名称', sortable: true },
  { key: 'active', header: '状态', sortable: true, width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', sortable: true, width: 'w-28' },
]

watch([keyword, groupFilter, pageSize, () => allRows.value.length], () => {
  page.value = 1
})

function resetFilters() {
  keyword.value = ''
  groupFilter.value = 'all'
}
function rowKey(row: ResourceRow) {
  return `${row.resourceType}:${row.code ?? row.displayName ?? ''}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="工厂资源" :breadcrumbs="[{ label: '基础数据' }]" :count="`${listRows.length} 项资源`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="groupsPending" @click="refreshGroups">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="资源总数" :value="allRows.length" hint="工厂 / 产线 / 工作中心 / 设备 等" />
      <SectionCard description="工厂" :value="countOf('site')" hint="生产站点" />
      <SectionCard description="产线" :value="countOf('production-line')" hint="生产线" />
      <SectionCard description="设备" :value="countOf('device-asset')" hint="设备资产" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索类型、编码、名称">
      <template #filters>
        <Select v-model="groupFilter">
          <SelectTrigger class="h-9 w-36" aria-label="资源类型"><SelectValue placeholder="全部类型" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部类型</SelectItem>
            <SelectItem v-for="def in resourceDefinitions" :key="def.key" :value="def.key">{{ def.title }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
      <template #actions>
        <Button type="button" variant="ghost" size="sm" @click="resetFilters">重置</Button>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="groupsPending"
      empty-message="未找到工厂资源。可切换类型或清空筛选。"
    >
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="sortedRows.length" />
  </BusinessLayout>
</template>
