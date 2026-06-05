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
import { inferPartnerRole, roleLabel, type PartnerRole } from './masterDataPageHelpers'

definePage({ meta: { requiresAuth: true, title: '客户与供应商' } })

type PartnerRow = BusinessConsoleResourceItem & { role: PartnerRole }

const {
  filters,
  resources,
  resourcesError,
  resourcesPending,
  resourcesTotal,
  refreshResources,
} = useBusinessMasterDataResources('business-partner')

const keyword = ref('')
const roleFilter = ref<PartnerRole>('all')
const sort = ref<DataTableSort | null>({ key: 'code', direction: 'asc' })
const page = ref(1)
const pageSize = ref('10')

const allRows = computed<PartnerRow[]>(() =>
  resources.value.map((row) => ({ ...row, role: inferPartnerRole(row) })),
)
const listRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return allRows.value.filter((row) => {
    const roleMatched = roleFilter.value === 'all' || row.role === roleFilter.value
    const kwMatched =
      !kw ||
      [row.code, row.displayName, roleLabel(row.role), row.snapshotVersion]
        .some((value) => (value ?? '').toLowerCase().includes(kw))
    return roleMatched && kwMatched
  })
})
const sortedRows = computed(() => {
  if (!sort.value) return listRows.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...listRows.value].sort((a, b) =>
    String(key === 'role' ? roleLabel(a.role) : a[key as keyof PartnerRow] ?? '')
      .localeCompare(String(key === 'role' ? roleLabel(b.role) : b[key as keyof PartnerRow] ?? ''), 'zh-Hans-CN') * factor,
  )
})
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRows = computed(() => sortedRows.value)

const customerCount = computed(() => allRows.value.filter((r) => r.role === 'customer').length)
const supplierCount = computed(() => allRows.value.filter((r) => r.role === 'supplier').length)
const errorMessage = computed(() => formatError(resourcesError.value))

const columns: DataTableColumn<PartnerRow>[] = [
  { key: 'code', header: '编码', sortable: true, cellClass: 'font-medium' },
  { key: 'displayName', header: '名称', sortable: true },
  { key: 'role', header: '角色', sortable: true, width: 'w-28' },
  { key: 'active', header: '状态', sortable: true, width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', sortable: true, width: 'w-28' },
]

watch([keyword, roleFilter, pageSize], () => {
  page.value = 1
})

watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

function resetFilters() {
  keyword.value = ''
  roleFilter.value = 'all'
}
function rowKey(row: PartnerRow) {
  return `${row.resourceType}:${row.code ?? row.displayName ?? ''}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="客户与供应商" :breadcrumbs="[{ label: '基础数据' }]" :count="`${resourcesTotal} 个伙伴`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="resourcesPending" @click="refreshResources">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="伙伴总数" :value="resourcesTotal" hint="客户与供应商档案" />
      <SectionCard description="客户" :value="customerCount" hint="支撑销售需求与发货" />
      <SectionCard description="供应商" :value="supplierCount" hint="支撑采购与收货检验" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索编码、名称、版本">
      <template #filters>
        <Select v-model="roleFilter">
          <SelectTrigger class="h-9 w-32" aria-label="伙伴角色"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部角色</SelectItem>
            <SelectItem value="customer">客户</SelectItem>
            <SelectItem value="supplier">供应商</SelectItem>
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
      :loading="resourcesPending"
      empty-message="未找到客户或供应商。"
    >
      <template #cell-role="{ row }">
        {{ roleLabel(row.role) }}
      </template>
      <template #cell-active="{ row }">
        <StatusBadge :value="row.active === false ? 'disabled' : 'active'" />
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="resourcesTotal" />
  </BusinessLayout>
</template>
