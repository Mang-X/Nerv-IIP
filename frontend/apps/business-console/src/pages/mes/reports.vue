<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { mesStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesProductionReports } from '@/composables/useBusinessMes'
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
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '报工与完工' } })

const { filters, productionReports, productionReportsError, productionReportsPending, productionReportsTotal, refreshProductionReports } = useMesProductionReports()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})
const goodTotal = computed(() => productionReports.value.reduce((s, r) => s + (r.goodQuantity ?? 0), 0))
const scrapTotal = computed(() => productionReports.value.reduce((s, r) => s + (r.scrapQuantity ?? 0), 0))
const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = (typeof productionReports)['value'][number]
const columns: DataTableColumn<ReportRow>[] = [
  { key: 'productionReportId', header: '报工单', cellClass: 'font-medium' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'operationTaskId', header: '工序任务', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无' },
  { key: 'goodQuantity', header: '合格数', align: 'end', width: 'w-20' },
  { key: 'scrapQuantity', header: '不良数', align: 'end', width: 'w-20' },
  { key: 'reworkQuantity', header: '返工数', align: 'end', width: 'w-20' },
  { key: 'reportedAtUtc', header: '报工时间', width: 'w-44' },
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
    <PageHeader title="报工与完工" :breadcrumbs="[{ label: '制造执行' }]" :count="`${productionReportsTotal} 条报工`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="productionReportsPending" @click="refreshProductionReports">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="报工记录" :value="productionReportsTotal" hint="后端筛选总数" />
      <SectionCard description="本页合格总数" :value="goodTotal" hint="当前页合计" />
      <SectionCard description="本页不良总数" :value="scrapTotal" hint="当前页合计" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="报工状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in mesStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="productionReports"
      row-key="productionReportId"
      :loading="productionReportsPending"
      empty-message="暂无报工记录。新增报工请从工单或工序上下文进入。"
    >
      <template #cell-goodQuantity="{ row }"><span class="tabular-nums">{{ row.goodQuantity ?? 0 }}</span></template>
      <template #cell-scrapQuantity="{ row }"><span class="tabular-nums">{{ row.scrapQuantity ?? 0 }}</span></template>
      <template #cell-reworkQuantity="{ row }"><span class="tabular-nums">{{ row.reworkQuantity ?? 0 }}</span></template>
      <template #cell-reportedAtUtc="{ row }">{{ formatDateTime(row.reportedAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="productionReportsTotal" />
  </BusinessLayout>
</template>
