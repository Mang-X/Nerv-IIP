<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { mesQualityStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import {
  makeIdempotencyKey,
  useMesOperationTasks,
  useMesRelatedQualityItems,
  useMesWorkScopeSelection,
} from '@/composables/useBusinessMes'
import { useQualityReasonCodes } from '@/composables/usePromotedCatalogs'
import RecordDefectDialog from '@/components/mes/RecordDefectDialog.vue'
import {
  labelFor,
  MES_QUALITY_ITEM_STATUS_LABELS,
  QUALITY_SOURCE_TYPE_LABELS,
} from '@/data/businessLabels'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '质量与不良',
    requiredPermissions: ['business.mes.quality.read'],
  },
})

const route = useRoute()
const router = useRouter()
const {
  filters,
  qualityItems,
  qualityItemsError,
  qualityItemsPending,
  qualityItemsTotal,
  recordDefect,
  recordDefectPending,
  refreshQualityItems,
} = useMesRelatedQualityItems()
const {
  operationTasks,
  operationTasksPending,
  operationListScopeMessage,
  operationListScopeReady,
  refreshOperationTasks,
} = useMesOperationTasks()
const qualityWriteScope = useMesWorkScopeSelection(P.mesQualityWrite)
const refreshQualityWriteScope = qualityWriteScope.refreshScope
const auth = useAuthStore()
const { keyword } = useMesKeywordFilter(filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})
// 缺陷代码的中文名在质量原因码目录里；目录查不到就只显代码，不编造缺陷名。
const { reasons: qualityReasons, reasonsPending } = useQualityReasonCodes()
const reasonNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const reason of qualityReasons.value) {
    if (reason.reasonCode && reason.reasonName) map.set(reason.reasonCode, reason.reasonName)
  }
  return map
})
function defectLabel(code?: string | null) {
  if (!code) return '无'
  return reasonNameByCode.value.get(code) ?? code
}

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const errorMessage = computed(() => inlineErrorMessage(qualityItemsError.value))
// 上下文穿透：从工单/工序带入时显示来源并提供返回链接。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
const contextOperationTaskId = computed(() => firstQuery(route.query.operationTaskId))
const openCount = computed(
  () => qualityItems.value.filter((r) => (r.status ?? '').toLowerCase() !== 'closed').length,
)
const ncrCount = computed(() => qualityItems.value.filter((r) => r.ncrId).length)
// 质量项的决策点是「还有多少没关闭」——构成卡按处理状态拆分；已开 NCR 单独标注。
const qualitySegments = computed(() =>
  pagedBreakdownSegments(qualityItemsTotal.value, [
    { key: 'open', label: '未关闭', value: openCount.value, tone: 'danger' },
    {
      key: 'closed',
      label: '已关闭',
      value: qualityItems.value.length - openCount.value,
      tone: 'success',
    },
  ]),
)

type QualityRow = (typeof qualityItems)['value'][number]
const columns: NvDataTableColumn<QualityRow>[] = [
  {
    key: 'qualityItemId',
    header: '质量项',
    cellClass: 'font-medium',
    accessor: (r) => r.qualityItemId ?? '无',
  },
  {
    key: 'sourceType',
    header: '来源类型',
    accessor: (r) => labelFor(QUALITY_SOURCE_TYPE_LABELS, r.sourceType) || '未指定',
  },
  { key: 'sourceDocumentId', header: '来源单据', accessor: (r) => r.sourceDocumentId ?? '未指定' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'defectCode', header: '缺陷', accessor: (r) => defectLabel(r.defectCode) },
  { key: 'ncrId', header: 'NCR', accessor: (r) => r.ncrId ?? '无' },
]

function isWorkOrder(value?: string | null) {
  return !!value && /^WO/i.test(value)
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
// ── 缺陷登记：可见工序读范围 × 质量写范围 ──────────────────────────
const canReadOperationContext = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.mesOperationsRead),
)
const canWriteQuality = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.mesQualityWrite),
)
const selectedQualityWriteScope = qualityWriteScope.selectedScope
const eligibleOperationTasks = computed(() => {
  const writeScope = selectedQualityWriteScope.value
  if (!writeScope || !canReadOperationContext.value || !canWriteQuality.value) return []
  return operationTasks.value.filter(
    (task) =>
      !!task.operationTaskId?.trim() &&
      !!task.workOrderId?.trim() &&
      qualityWriteScope.coversWorkOrder({ operationTasks: [task] }, writeScope),
  )
})
type OperationTask = (typeof operationTasks)['value'][number]
type DefectTarget = {
  key: string
  workOrderId: string
  operationTaskId?: string
  operationTasks: OperationTask[]
  label: string
}
const defectTargets = computed<DefectTarget[]>(() => {
  const targets: DefectTarget[] = []
  const tasksByWorkOrder = new Map<string, OperationTask[]>()
  for (const task of eligibleOperationTasks.value) {
    const workOrderId = task.workOrderId!.trim()
    const tasks = tasksByWorkOrder.get(workOrderId) ?? []
    tasks.push(task)
    tasksByWorkOrder.set(workOrderId, tasks)
  }
  for (const [workOrderId, tasks] of tasksByWorkOrder) {
    const workOrderLabel = tasks[0]?.workOrderNo || workOrderId
    targets.push({
      key: `work-order:${workOrderId}`,
      workOrderId,
      operationTasks: tasks,
      label: `${workOrderLabel} · 工单级（不关联具体工序）`,
    })
    for (const task of tasks) {
      const operationTaskId = task.operationTaskId!.trim()
      targets.push({
        key: `operation:${operationTaskId}`,
        workOrderId,
        operationTaskId,
        operationTasks: [task],
        label: [
          workOrderLabel,
          task.operationTaskNo || `第 ${task.operationSequence ?? '—'} 道工序`,
          task.workCenterName || task.workCenterCode || task.workCenterId,
        ]
          .filter(Boolean)
          .join(' · '),
      })
    }
  }
  return targets
})
const operationOptions = computed(() =>
  defectTargets.value.map((target) => ({ value: target.key, label: target.label })),
)
const defectOptions = computed(() =>
  qualityReasons.value
    .filter((reason) => reason.enabled !== false && !!reason.reasonCode?.trim())
    .map((reason) => ({
      value: reason.reasonCode!,
      label: reason.reasonName
        ? `${reason.reasonName}（${reason.reasonCode}）`
        : reason.reasonCode!,
    })),
)

const defectDialogOpen = shallowRef(false)
const defectShowErrors = shallowRef(false)
const defectPreflightPending = shallowRef(false)
const defectForm = reactive({ targetKey: '', defectCode: '', defectQuantity: '' })
const pendingDefectIntent = shallowRef<{
  fingerprint: string
  idempotencyKey: string
  recordedAtUtc: string
} | null>(null)
const defectPending = computed(
  () =>
    defectPreflightPending.value ||
    recordDefectPending.value ||
    operationTasksPending.value ||
    reasonsPending.value,
)
const defectEntryBlocker = computed(() => {
  if (!canWriteQuality.value) return '没有缺陷登记权限'
  if (!canReadOperationContext.value) return '没有工序上下文读取权限'
  if (!filters.organizationId.trim() || !filters.environmentId.trim()) {
    return '尚未进入有效组织与环境'
  }
  if (qualityWriteScope.scopePending.value) return '正在核验质量登记范围'
  if (!qualityWriteScope.scopeReady.value) {
    return qualityWriteScope.scopeMessage.value || '质量登记范围未就绪'
  }
  if (!operationListScopeReady.value) {
    return operationListScopeMessage.value || '工序可见范围未就绪'
  }
  if (operationTasksPending.value) return '正在读取可登记缺陷的工序'
  if (eligibleOperationTasks.value.length === 0) return '当前授权范围内暂无可登记缺陷的工序'
  return ''
})

function clearDefectIntent() {
  pendingDefectIntent.value = null
}

function openDefectDialog() {
  if (defectEntryBlocker.value) return
  const routeTaskId = contextOperationTaskId.value
  const routeWorkOrderId = contextWorkOrderId.value
  const preferred = defectTargets.value.find((target) =>
    routeTaskId
      ? target.operationTaskId === routeTaskId &&
        (!routeWorkOrderId || target.workOrderId === routeWorkOrderId)
      : !!routeWorkOrderId && !target.operationTaskId && target.workOrderId === routeWorkOrderId,
  )
  defectForm.targetKey = preferred?.key ?? ''
  defectForm.defectCode = ''
  defectForm.defectQuantity = ''
  defectShowErrors.value = false
  clearDefectIntent()
  defectDialogOpen.value = true
}

function validDefectQuantity() {
  const quantity = Number(defectForm.defectQuantity)
  return Number.isFinite(quantity) && quantity > 0 ? quantity : undefined
}

function findEligibleDefectTarget(targetKey: string) {
  const writeScope = selectedQualityWriteScope.value
  const target = defectTargets.value.find((candidate) => candidate.key === targetKey)
  if (
    !target?.workOrderId.trim() ||
    !writeScope ||
    !qualityWriteScope.coversWorkOrder({ operationTasks: target.operationTasks }, writeScope)
  ) {
    return undefined
  }
  return target
}

async function submitDefect() {
  const quantity = validDefectQuantity()
  const targetKey = defectForm.targetKey.trim()
  const defectCode = defectForm.defectCode.trim()
  if (defectEntryBlocker.value || !targetKey || !defectCode || quantity === undefined) {
    defectShowErrors.value = true
    return
  }

  defectPreflightPending.value = true
  let latestTarget: ReturnType<typeof findEligibleDefectTarget>
  let latestScope: NonNullable<(typeof selectedQualityWriteScope)['value']>
  try {
    await Promise.all([refreshOperationTasks(), refreshQualityWriteScope()])
    latestTarget = findEligibleDefectTarget(targetKey)
    const refreshedScope = selectedQualityWriteScope.value
    if (!latestTarget || !refreshedScope) {
      throw new Error('所选工单或工序已不在当前主体可见且可登记缺陷的范围，请重新选择。')
    }
    latestScope = refreshedScope
  } catch (error) {
    notifyOperationFailure(
      '缺陷登记前置检查失败',
      error,
      '未能在当前主体可见范围内确认工单与工序，请刷新后重试。',
    )
    return
  } finally {
    defectPreflightPending.value = false
  }

  const fingerprint = JSON.stringify({
    organizationId: filters.organizationId.trim(),
    environmentId: filters.environmentId.trim(),
    workOrderId: latestTarget.workOrderId,
    operationTaskId: latestTarget.operationTaskId ?? null,
    defectCode,
    quantity,
    scopeKind: latestScope.kind,
    scopeId: latestScope.id,
  })
  if (pendingDefectIntent.value?.fingerprint !== fingerprint) {
    pendingDefectIntent.value = {
      fingerprint,
      idempotencyKey: makeIdempotencyKey('record-defect'),
      recordedAtUtc: new Date().toISOString(),
    }
  }

  try {
    const response = await recordDefect({
      workOrderId: latestTarget.workOrderId,
      ...(latestTarget.operationTaskId ? { operationTaskId: latestTarget.operationTaskId } : {}),
      defectCode,
      quantity,
      recordedAtUtc: pendingDefectIntent.value.recordedAtUtc,
      idempotencyKey: pendingDefectIntent.value.idempotencyKey,
      scopeKind: latestScope.kind,
      scopeId: latestScope.id,
    })
    if (response?.data?.accepted !== true) {
      throw new Error('缺陷登记结果未确认，请刷新质量记录核实后再重试。')
    }

    const receipt = response.data.downstreamDocumentId?.trim()
    defectDialogOpen.value = false
    clearDefectIntent()
    const refreshResults = await Promise.allSettled([
      refreshQualityItems(),
      refreshOperationTasks(),
    ])
    notifySuccess(receipt ? `缺陷 ${receipt} 已登记。` : '缺陷登记已受理。')
    const refreshFailure = refreshResults.find(
      (result): result is PromiseRejectedResult => result.status === 'rejected',
    )
    if (refreshFailure) {
      notifyOperationFailure(
        '缺陷已登记，但状态刷新失败',
        refreshFailure.reason,
        '缺陷已登记，但最新状态刷新失败，请手动刷新。',
      )
    }
  } catch (error) {
    notifyOperationFailure('缺陷登记失败', error, '缺陷登记失败，请根据服务端原因检查后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="质量与不良"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${qualityItemsTotal} 条质量项`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          :disabled="Boolean(defectEntryBlocker)"
          :title="defectEntryBlocker || '登记生产过程缺陷'"
          @click="openDefectDialog"
        >
          登记缺陷
        </NvButton>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="qualityItemsPending"
          @click="refreshQualityItems"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="breakdown"
        label="质量项"
        :value="qualityItemsTotal"
        unit="条"
        :segments="qualitySegments"
      />
      <NvMetricCard
        variant="alert"
        label="已开不合格品单"
        :value="ncrCount"
        unit="张"
        :tone="ncrCount > 0 ? 'danger' : 'neutral'"
        :status="
          ncrCount > 0 ? { label: '待处置', tone: 'danger' } : { label: '无', tone: 'success' }
        "
        :foot-start="
          ncrCount > 0
            ? '不合格品单需给出返工、让步或报废结论后才能关闭对应质量项。'
            : '当前质量项都未升级为不合格品单。'
        "
        :action="ncrCount > 0 ? { label: '去质量处置' } : undefined"
        @action="router.push({ path: '/quality/ncrs' })"
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="质量项 / 来源单据 / 缺陷代码"
          aria-label="搜索质量项"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="质量状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesQualityStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="qualityItemsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="qualityItems"
      row-key="qualityItemId"
      :loading="qualityItemsPending"
      :error="qualityItemsError"
      :error-message="errorMessage"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无质量或不良记录。点击上方「登记缺陷」记录生产过程中的不良，登记后可在这里跟进处置与关闭。"
      @retry="refreshQualityItems"
    >
      <template #cell-sourceDocumentId="{ row }">
        <RouterLink
          v-if="isWorkOrder(row.sourceDocumentId)"
          :to="`/mes/work-orders/${encodeURIComponent(row.sourceDocumentId!)}`"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.sourceDocumentId }}
        </RouterLink>
        <span v-else>{{ row.sourceDocumentId ?? '未指定' }}</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status"
          :label="labelFor(MES_QUALITY_ITEM_STATUS_LABELS, row.status) || '未知'"
        />
      </template>
      <template #cell-ncrId="{ row }">
        <RouterLink
          v-if="row.ncrId"
          :to="{
            path: '/quality/ncrs',
            query: {
              ncrId: row.ncrId,
              workOrderId: isWorkOrder(row.sourceDocumentId) ? row.sourceDocumentId : undefined,
            },
          }"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.ncrId }}
        </RouterLink>
        <span v-else class="text-muted-foreground">无</span>
      </template>
    </NvDataTable>

    <RecordDefectDialog
      v-model:open="defectDialogOpen"
      v-model:target-key="defectForm.targetKey"
      v-model:defect-code="defectForm.defectCode"
      v-model:defect-quantity="defectForm.defectQuantity"
      :operation-options="operationOptions"
      :defect-options="defectOptions"
      :pending="defectPending"
      :show-errors="defectShowErrors"
      @submit="submitDefect"
    />
  </BusinessLayout>
</template>
