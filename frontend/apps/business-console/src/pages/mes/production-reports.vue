<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { useMesProductionReports } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  PageHeader,
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
const { page, pageSize } = usePagedList(filters)

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = (typeof productionReports)['value'][number]
const columns: DataTableColumn<ReportRow>[] = [
  { key: 'reportNo', header: '报工单', cellClass: 'font-medium', accessor: (r) => r.reportNo ?? r.productionReportId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'output', header: '产量', accessor: (r) => r.goodQuantity ?? 0 },
  { key: 'operationTaskId', header: '工序任务', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无' },
  { key: 'reportedAtUtc', header: '报工时间', width: 'w-44' },
]

function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function openWorkOrder(workOrderId?: string | null) {
  if (workOrderId) quickViewWorkOrderId.value = workOrderId
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

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="productionReports"
      row-key="productionReportId"
      :loading="productionReportsPending"
      empty-message="还没有报工记录。报工后这里会出现对应记录，去工序执行报工。"
    >
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          {{ row.workOrderNo ?? row.workOrderId }}
        </button>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-output="{ row }">
        <div class="flex flex-col gap-0.5 tabular-nums">
          <span>良品 {{ formatQuantity(row.goodQuantity) }}</span>
          <span v-if="(row.scrapQuantity ?? 0) > 0" class="text-xs text-warning">
            报废 {{ formatQuantity(row.scrapQuantity) }}
          </span>
          <span v-else class="text-xs text-muted-foreground">报废 0</span>
          <span v-if="(row.reworkQuantity ?? 0) > 0" class="text-xs text-muted-foreground">
            返工 {{ formatQuantity(row.reworkQuantity) }}
          </span>
        </div>
      </template>
      <template #cell-reportedAtUtc="{ row }">{{ formatDateTime(row.reportedAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="productionReportsTotal" />

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
