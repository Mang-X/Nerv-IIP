<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { mesStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesShiftHandovers } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
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
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '班次交接' } })

const { filters, handovers, handoversError, handoversPending, handoversTotal, refreshHandovers } = useMesShiftHandovers()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})
const openIssueTotal = computed(() => handovers.value.reduce((s, r) => s + (r.openIssueCount ?? 0), 0))
const errorMessage = computed(() => formatError(handoversError.value))

type HandoverRow = (typeof handovers)['value'][number]
const columns: DataTableColumn<HandoverRow>[] = [
  { key: 'handoverId', header: '交接单', cellClass: 'font-medium', accessor: (r) => r.handoverId ?? '无' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '无' },
  { key: 'teamId', header: '班组', accessor: (r) => r.teamId ?? '无' },
  { key: 'handoverStatus', header: '状态', width: 'w-24' },
  { key: 'openIssueCount', header: '未结事项', align: 'end', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', width: 'w-44' },
]

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="班次交接" :breadcrumbs="[{ label: '制造执行' }]" :count="`${handoversTotal} 条交接`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="handoversPending" @click="refreshHandovers">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="交接单" :value="handoversTotal" hint="后端筛选总数" />
      <SectionCard description="本页未结事项" :value="openIssueTotal" hint="当前页统计" />
      <SectionCard description="本页当前班次" :value="handovers.length" hint="当前页统计" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="交接状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in mesStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="handovers"
      row-key="handoverId"
      :loading="handoversPending"
      empty-message="暂无班次交接。班次结束时创建交接单，记录未完成事项。"
    >
      <template #cell-handoverStatus="{ row }"><StatusBadge :value="row.handoverStatus" /></template>
      <template #cell-openIssueCount="{ row }"><span class="tabular-nums">{{ row.openIssueCount ?? 0 }}</span></template>
      <template #cell-createdAtUtc="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="handoversTotal" />
  </BusinessLayout>
</template>
