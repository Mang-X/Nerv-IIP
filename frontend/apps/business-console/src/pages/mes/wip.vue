<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { describeMesReadinessReason, useMesWipSummary } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Input,
  PageHeader,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: true, title: '在制跟踪' } })

const { filters, refreshWip, wipError, wipPending, wipRows, wipTotal } = useMesWipSummary()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(wipError.value))

type WipRow = (typeof wipRows)['value'][number]
// facade 返回的是人读编码（workOrderId=WO-…、workCenterId=WC-…），直接展示即可分辨，不藏占位。
const columns: DataTableColumn<WipRow>[] = [
  { key: 'workOrderId', header: '工单', cellClass: 'font-medium' },
  { key: 'workCenterId', header: '工作中心', width: 'w-32' },
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
        <Button size="sm" type="button" variant="outline" :disabled="wipPending" @click="refreshWip">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="在制状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="wipRows"
      :row-key="(r) => `${r.workOrderId}-${r.operationTaskId}`"
      :loading="wipPending"
      empty-message="暂无在制数据。工单释放并排程、工序开工后，在制行会出现在这里。"
    >
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          {{ row.workOrderId }}
        </button>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-workCenterId="{ row }">
        <span v-if="row.workCenterId">{{ row.workCenterId }}</span>
        <span v-else class="text-muted-foreground">未指定</span>
      </template>
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
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
            <StatusBadge :label="reason.label" tone="warning" />
            <p class="text-xs text-muted-foreground">{{ reason.nextStep }}</p>
          </div>
        </div>
        <span v-else class="text-muted-foreground">无卡点</span>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="wipTotal" />

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
