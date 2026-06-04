<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '字典' } })

const { resources, resourcesError, resourcesPending, refreshResources } = useBusinessMasterDataResources('reference-data')

const keyword = ref('')
const sort = ref<DataTableSort | null>({ key: 'code', direction: 'asc' })
const page = ref(1)
const pageSize = ref('10')

const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return resources.value
  return resources.value.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort((a, b) =>
    String(a[key as keyof BusinessConsoleResourceItem] ?? '')
      .localeCompare(String(b[key as keyof BusinessConsoleResourceItem] ?? ''), 'zh-Hans-CN') * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSizeNumber.value
  return sortedRows.value.slice(start, start + pageSizeNumber.value)
})
const errorMessage = computed(() => formatError(resourcesError.value))

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', sortable: true, cellClass: 'font-medium' },
  { key: 'displayName', header: '名称', sortable: true },
  { key: 'active', header: '状态', sortable: true, width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', sortable: true, width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
]

watch([keyword, pageSize, () => resources.value.length], () => {
  page.value = 1
})

function rowKey(row: BusinessConsoleResourceItem) {
  return `${row.resourceType ?? 'reference-data'}:${row.code ?? row.displayName ?? ''}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="字典" :breadcrumbs="[{ label: '基础数据' }]" :count="`${listRows.length} 项参考数据`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="resourcesPending" @click="refreshResources">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="参考数据项" :value="resources.length" hint="单位、材料形态、质量原因等编码" />
      <SectionCard description="当前结果" :value="listRows.length" hint="筛选后的参考数据" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索编码、名称" />

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedRows"
      :row-key="rowKey"
      :client-sort="false"
      :loading="resourcesPending"
      empty-message="未找到参考数据。"
    >
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="sortedRows.length" />
  </BusinessLayout>
</template>
