<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { describeMesReadinessReason, useMesWipSummary } from '@/composables/useBusinessMes'
import { mesOperationTaskStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePagination,
  DataTablePro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '在制跟踪' } })

const { filters, refreshWip, wipError, wipPending, wipRows, wipTotal } = useMesWipSummary()
const { resolveWorkCenter } = useMesDisplayNames()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
const statusFilter = shallowRef('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(wipError.value))

type WipRow = (typeof wipRows)['value'][number]
// facade 回显示字段（workOrderNo / operationTaskNo / workCenterName），accessor 优先取人读显示值。
const columns: DataTableProColumn<WipRow>[] = [
  { key: 'workOrderId', header: '工单', cellClass: 'font-medium', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'operationTaskId', header: '工序任务', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'progress', header: '在制进度', width: 'w-48', accessor: (r) => r.goodQuantity ?? 0 },
  { key: 'blockingReasons', header: '卡点' },
]

// 完成度（0–1）= 已产良品 / 计划数，用于进度条宽度。
function progressRatio(row: WipRow) {
  const planned = row.plannedQuantity ?? 0
  if (planned <= 0) return row.goodQuantity != null && row.goodQuantity > 0 ? 1 : 0
  return Math.min(1, Math.max(0, (row.goodQuantity ?? 0) / planned))
}
function readinessList(reasons?: string[] | null) {
  return (reasons ?? []).map(describeMesReadinessReason)
}
function openWorkOrder(workOrderId?: string | null) {
  if (workOrderId) quickViewWorkOrderId.value = workOrderId
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="在制跟踪" :breadcrumbs="[{ label: '制造执行' }]" :count="`${wipTotal} 行在制`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="wipPending" @click="refreshWip">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <Toolbar :show-search="false">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="在制状态"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="option in mesOperationTaskStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="wipRows"
      :row-key="(r) => `${r.workOrderId}-${r.operationTaskId}`"
      :loading="wipPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无在制数据。工单释放并排程、工序开工后，在制行会出现在这里。"
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
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
      <template #cell-progress="{ row }">
        <div class="flex flex-col gap-1">
          <span class="text-sm tabular-nums">
            已产 {{ formatQuantity(row.goodQuantity) }} / 计划 {{ formatQuantity(row.plannedQuantity) }}
          </span>
          <div class="h-1.5 w-full overflow-hidden rounded-full bg-muted" role="presentation">
            <div
              class="h-full rounded-full bg-success transition-all"
              :style="{ width: `${Math.round(progressRatio(row) * 100)}%` }"
            />
          </div>
          <span v-if="(row.scrapQuantity ?? 0) > 0" class="text-xs text-warning tabular-nums">
            报废 {{ formatQuantity(row.scrapQuantity) }}
          </span>
        </div>
      </template>
      <template #cell-blockingReasons="{ row }">
        <div v-if="row.blockingReasons?.length" class="grid gap-2">
          <div v-for="reason in readinessList(row.blockingReasons)" :key="`${row.operationTaskId}-${reason.code}`" class="grid gap-0.5">
            <StatusBadgePro :label="reason.label" tone="warning" />
            <p class="text-xs text-muted-foreground">{{ reason.nextStep }}</p>
          </div>
        </div>
        <span v-else class="text-muted-foreground">无卡点</span>
      </template>
    </DataTablePro>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="wipTotal" />

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
