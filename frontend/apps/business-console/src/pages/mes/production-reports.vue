<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { useMesProductionReports, useMesTelemetryProductionReportCandidates } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvPageHeader } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '报工记录',
    requiredPermissions: ['business.mes.reporting.read'],
  },
})

const {
  filters,
  productionReports,
  productionReportsError,
  productionReportsPending,
  productionReportsTotal,
  refreshProductionReports,
} = useMesProductionReports()
const { page, pageSize } = usePagedList(filters)
const candidateQueue = useMesTelemetryProductionReportCandidates()
const candidateWorkOrderId = ref('')
const candidateOperationTaskId = ref('')
const dismissalReason = ref('')
const selectedCandidateId = ref<string | null>(null)

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = (typeof productionReports)['value'][number]
const columns: NvDataTableColumn<ReportRow>[] = [
  {
    key: 'reportNo',
    header: '报工单',
    cellClass: 'font-medium',
    accessor: (r) => r.reportNo ?? r.productionReportId ?? '无',
  },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'output', header: '产量', accessor: (r) => r.goodQuantity ?? 0 },
  {
    key: 'operationTaskId',
    header: '工序任务',
    accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无',
  },
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
async function promoteCandidate(candidate: { candidateId?: string; workOrderId?: string | null; operationTaskId?: string | null }) {
  if (!candidate.candidateId) return
  const workOrderId = candidateWorkOrderId.value.trim() || candidate.workOrderId?.trim()
  const operationTaskId = candidateOperationTaskId.value.trim() || candidate.operationTaskId?.trim()
  if (!workOrderId || !operationTaskId) return
  await candidateQueue.promote(candidate.candidateId, workOrderId, operationTaskId)
  selectedCandidateId.value = null
}
async function dismissCandidate(candidateId?: string) {
  if (!candidateId || !dismissalReason.value.trim()) return
  await candidateQueue.dismiss(candidateId, dismissalReason.value.trim())
  selectedCandidateId.value = null
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="报工记录"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${productionReportsTotal} 条报工`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="productionReportsPending"
          @click="refreshProductionReports"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="productionReportsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="productionReports"
      row-key="productionReportId"
      :loading="productionReportsPending"
      empty-message="还没有报工记录。报工后这里会出现对应记录，去工序执行报工。"
      :searchable="false"
      :column-settings="false"
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
    </NvDataTable>

    <section class="mt-8 space-y-4" aria-labelledby="telemetry-candidate-title">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id="telemetry-candidate-title" class="text-lg font-semibold">遥测报工待确认</h2>
          <p class="text-sm text-muted-foreground">来自设备计数的真实草稿与挂起记录，共 {{ candidateQueue.total.value }} 条。</p>
        </div>
        <div class="flex gap-2">
          <select v-model="candidateQueue.filters.status" class="rounded-md border border-border bg-card px-3 py-2 text-sm">
            <option value="pending-confirmation">待确认</option><option value="draft">草稿</option><option value="">全部</option>
          </select>
          <NvButton variant="outline" size="sm" @click="candidateQueue.refresh">刷新队列</NvButton>
        </div>
      </div>
      <p v-if="candidateQueue.error.value" class="text-sm text-destructive" role="alert">{{ formatError(candidateQueue.error.value) }}</p>
      <div v-if="candidateQueue.candidates.value.length" class="space-y-3">
        <article v-for="candidate in candidateQueue.candidates.value" :key="candidate.candidateId" class="rounded-lg border border-border bg-card p-4">
          <div class="flex flex-wrap justify-between gap-3">
            <div><p class="font-medium">{{ candidate.deviceAssetId }} · {{ candidate.tagKey }}</p><p class="text-sm text-muted-foreground">{{ candidate.goodQuantity }} 件 · {{ formatDateTime(candidate.bucketEndUtc) }} · {{ candidate.suspensionReason ?? candidate.status }}</p></div>
            <NvButton size="sm" variant="outline" @click="selectedCandidateId = selectedCandidateId === candidate.candidateId ? null : (candidate.candidateId ?? null)">处理</NvButton>
          </div>
          <div v-if="selectedCandidateId === candidate.candidateId" class="mt-4 grid gap-3 md:grid-cols-2">
            <label class="text-sm">工单<input v-model="candidateWorkOrderId" :placeholder="candidate.workOrderId ?? '输入真实工单号'" class="mt-1 w-full rounded-md border border-border bg-background px-3 py-2" /></label>
            <label class="text-sm">工序任务<input v-model="candidateOperationTaskId" :placeholder="candidate.operationTaskId ?? '输入真实工序任务号'" class="mt-1 w-full rounded-md border border-border bg-background px-3 py-2" /></label>
            <label class="text-sm md:col-span-2">忽略原因<input v-model="dismissalReason" placeholder="忽略时必填" class="mt-1 w-full rounded-md border border-border bg-background px-3 py-2" /></label>
            <div class="flex gap-2 md:col-span-2"><NvButton size="sm" :disabled="candidateQueue.actionPending.value" @click="promoteCandidate(candidate)">确认并转正</NvButton><NvButton size="sm" variant="outline" :disabled="candidateQueue.actionPending.value || !dismissalReason.trim()" @click="dismissCandidate(candidate.candidateId)">忽略</NvButton></div>
          </div>
        </article>
      </div>
      <p v-else-if="!candidateQueue.pending.value" class="rounded-lg border border-dashed border-border p-8 text-center text-sm text-muted-foreground">当前没有遥测报工候选。</p>
    </section>

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
