<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesProductionReports } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: true, title: '报工记录' } })

const {
  filters,
  productionReports,
  productionReportsError,
  productionReportsPending,
  productionReportsTotal,
  refreshProductionReports,
} = useMesProductionReports()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const keyword = ref('')
const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return productionReports.value
  return productionReports.value.filter((r) =>
    [r.productionReportId, r.workOrderId, r.operationTaskId].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

const goodTotal = computed(() => productionReports.value.reduce((s, r) => s + (r.goodQuantity ?? 0), 0))
const scrapTotal = computed(() => productionReports.value.reduce((s, r) => s + (r.scrapQuantity ?? 0), 0))
const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = (typeof productionReports)['value'][number]
const columns: DataTableColumn<ReportRow>[] = [
  { key: 'productionReportId', header: '报工单', cellClass: 'font-medium', accessor: (r) => r.productionReportId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'operationTaskId', header: '工序任务', accessor: (r) => r.operationTaskId ?? '无' },
  { key: 'goodQuantity', header: '良品', align: 'end', width: 'w-20' },
  { key: 'scrapQuantity', header: '报废', align: 'end', width: 'w-20' },
  { key: 'reworkQuantity', header: '返工', align: 'end', width: 'w-20' },
  { key: 'reportedAtUtc', header: '报工时间', width: 'w-44' },
]

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="报工记录" :breadcrumbs="[{ label: '制造执行' }]" :count="`${productionReportsTotal} 条报工`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="productionReportsPending" @click="refreshProductionReports">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="报工记录" :value="productionReportsTotal" hint="后端筛选总数" />
      <SectionCard description="本页良品数" :value="formatQuantity(goodTotal)" hint="当前页合计" />
      <SectionCard description="本页报废数" :value="formatQuantity(scrapTotal)" hint="当前页合计" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索报工单、工单、工序">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="报工状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="filtered"
      row-key="productionReportId"
      :loading="productionReportsPending"
      empty-message="暂无报工记录。新增报工请从工单与派工或工序执行进入。"
    >
      <template #cell-goodQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.goodQuantity) }}</span></template>
      <template #cell-scrapQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.scrapQuantity) }}</span></template>
      <template #cell-reworkQuantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.reworkQuantity) }}</span></template>
      <template #cell-reportedAtUtc="{ row }">{{ formatDateTime(row.reportedAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="productionReportsTotal" />
  </BusinessLayout>
</template>
