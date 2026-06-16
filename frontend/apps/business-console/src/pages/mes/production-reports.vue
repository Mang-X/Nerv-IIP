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
  Input,
  PageHeader,
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

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = (typeof productionReports)['value'][number]
const columns: DataTableColumn<ReportRow>[] = [
  // TODO(#420): productionReportId 为后端 GUID；有人读单号 reportNo 时以它作锚点，无则降级占位，不显裸 GUID。
  { key: 'reportNo', header: '报工单', cellClass: 'font-medium' },
  { key: 'workOrderId', header: '工单' },
  { key: 'output', header: '产量', accessor: (r) => r.goodQuantity ?? 0 },
  // TODO(#420): operationTaskId 为后端 GUID，facade 暂不回工序号；不显裸 GUID，降级占位。
  { key: 'operationTaskId', header: '工序' },
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

    <p class="text-sm text-muted-foreground">
      这里是各工单的<span class="font-medium text-foreground">报工历史</span>，只查不录：报工从工序执行或工单发起。
      点行内<span class="font-medium text-foreground">查看工单</span>可回到源工单追溯这条报工。
    </p>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="报工状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="productionReports"
      row-key="productionReportId"
      :loading="productionReportsPending"
      empty-message="还没有报工记录。报工后这里会出现对应记录，去工序执行报工。"
    >
      <!-- TODO(#420): 优先用人读单号 reportNo；后端未回时降级占位，不显裸 GUID 当标识。 -->
      <template #cell-reportNo="{ row }">
        <span v-if="row.reportNo">{{ row.reportNo }}</span>
        <span v-else class="text-muted-foreground">单号待接入</span>
      </template>
      <!-- TODO(#420): workOrderId 为后端 GUID，facade 暂不回工单号；不显裸 GUID，以「查看工单」承载回链。 -->
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          查看工单
        </button>
        <span v-else class="text-muted-foreground">未关联工单</span>
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
      <!-- TODO(#420): operationTaskId 为后端 GUID，facade 暂不回工序号；不显裸 GUID，降级占位。 -->
      <template #cell-operationTaskId="{ row }">
        <span class="text-muted-foreground">{{ row.operationTaskId ? '工序待接入' : '无' }}</span>
      </template>
      <template #cell-reportedAtUtc="{ row }">{{ formatDateTime(row.reportedAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="productionReportsTotal" />

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
