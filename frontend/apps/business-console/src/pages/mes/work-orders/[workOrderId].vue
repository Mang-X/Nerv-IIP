<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { describeMesReadinessReason, useMesWorkOrderDetail } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvAlertDialog,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDataTable,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
  Spinner,
} from '@nerv-iip/ui'
import {
  ClipboardCheckIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  ShieldCheckIcon,
  XCircleIcon,
} from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工单详情',
    requiredPermissions: ['business.mes.work-orders.read'],
  },
})

const route = useRoute()
const router = useRouter()
const {
  activateCancelPreview,
  cancelPreviewError,
  cancelPreviewPending,
  cancelPreviewReady,
  cancelWorkOrder,
  cancelWorkOrderPending,
  detail,
  detailError,
  detailPending,
  filters,
  finishedGoodsReceiptRequests,
  materialIssueRequests,
  materialReadiness,
  materialReadinessError,
  materialReadinessPending,
  refreshDetail,
  refreshMaterialReadiness,
  retryCancelPreview,
} = useMesWorkOrderDetail()

watch(
  () => (route.params as Record<string, string | string[] | undefined>).workOrderId,
  (value) => {
    filters.workOrderId = (Array.isArray(value) ? value[0] : value) ?? ''
  },
  { immediate: true },
)

const operationTasks = computed(() => detail.value?.operationTasks ?? [])
const materialRows = computed(() => materialReadiness.value?.items ?? [])
const blockingReasons = computed(() => [
  ...(detail.value?.blockingReasons ?? []),
  ...(materialReadiness.value?.blockingReasons ?? []),
])
const blockingReasonDisplays = computed(() => blockingReasons.value.map(describeMesReadinessReason))
const errorMessage = computed(
  () => formatError(detailError.value) || formatError(materialReadinessError.value),
)

type TaskRow = (typeof operationTasks)['value'][number]
const taskColumns: NvDataTableColumn<TaskRow>[] = [
  {
    key: 'operationTaskId',
    header: '任务',
    cellClass: 'font-medium',
    accessor: (r) => r.operationTaskId ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'operationSequence',
    header: '序号',
    align: 'end',
    width: 'w-16',
    accessor: (r) => r.operationSequence ?? 0,
  },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '未指定' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
  { key: 'startedAtUtc', header: '开始', width: 'w-44' },
  { key: 'qualityStatus', header: '质量', accessor: (r) => r.qualityStatus ?? '未检' },
]

type MaterialRow = (typeof materialRows)['value'][number]
const materialColumns: NvDataTableColumn<MaterialRow>[] = [
  {
    key: 'materialId',
    header: '物料',
    cellClass: 'font-medium',
    accessor: (r) => r.materialId ?? '无',
  },
  { key: 'materialLotId', header: '批次', accessor: (r) => r.materialLotId ?? '未指定' },
  { key: 'requiredQuantity', header: '需求', align: 'end', width: 'w-20' },
  { key: 'availableQuantity', header: '可用', align: 'end', width: 'w-20' },
  { key: 'stagedQuantity', header: '已备', align: 'end', width: 'w-20' },
  { key: 'shortageQuantity', header: '短缺', align: 'end', width: 'w-20' },
  { key: 'status', header: '状态', width: 'w-24' },
]

// --- 取消工单（破坏性动作，原因必填 + 补偿预览，A1 §2/§4） ---
const CANCELLABLE_STATUSES = new Set(['created', 'released', 'hold'])
const CANCEL_REASONS = [
  { value: 'plan-cancelled', label: '计划取消' },
  { value: 'demand-changed', label: '需求变更' },
  { value: 'material-shortage', label: '物料短缺' },
  { value: 'quality-issue', label: '质量问题' },
  { value: 'engineering-change', label: '工程变更' },
  { value: 'duplicate', label: '重复工单' },
  { value: 'other', label: '其他（备注必填）' },
] as const
const TERMINAL_TASK_STATUSES = new Set(['completed', 'closed', 'cancelled'])
const TERMINAL_RECEIPT_STATUSES = new Set([
  'cancelled',
  'closed',
  'completed',
  'posted',
  'received',
])

const cancelOpen = ref(false)
const cancelForm = reactive({ reasonCode: '', remark: '' })

const currentStatus = computed(() => (detail.value?.status ?? '').toLowerCase())
const canCancel = computed(() => CANCELLABLE_STATUSES.has(currentStatus.value))
const cancelDisabledReason = computed(() => {
  if (!detail.value) return '工单信息加载中，请稍候。'
  if (canCancel.value) return ''
  const reasons: Record<string, string> = {
    started: '工单已开工，无法取消；请先处理在制工序。',
    running: '工单执行中，无法取消；请先暂停相关工序。',
    inProgress: '工单执行中，无法取消；请先暂停相关工序。',
    completed: '工单已完成，无法取消。',
    closed: '工单已关闭，无法取消。',
    cancelled: '工单已取消。',
  }
  return (
    reasons[currentStatus.value] ??
    `当前状态（${formatStatus(detail.value.status)}）不可取消，仅创建 / 已下达 / 暂停状态可取消。`
  )
})

// 补偿预览（后端暂无取消预览端点，按关联单据前端汇总，PR 已注明降级实现）。
// 权威来源是本工单的领料申请（取消 handler 遍历的对象）：已收料→退料指引，未收料且未终结→释放预留；
// 齐套快照仅在有已发布 MBOM 时才有行，无法覆盖无 MBOM 直接领料的工单，故不用它做补偿汇总。
const TERMINAL_ISSUE_STATUSES = new Set(['cancelled', 'closed', 'returned', 'completed'])
// 与后端取消退料规则一致：可退数量 = max(0, receivedQuantity - consumedQuantity)，且无 materialLotId 不可退
// （后端 #557：无批次退料抛业务错误）。仅当可退数量>0 且有批次时才计为退料指引，避免预览/toast 误报。
function returnableQuantityOf(row: (typeof materialIssueRequests.value)[number]): number {
  return Math.max(0, (row.receivedQuantity ?? 0) - (row.consumedQuantity ?? 0))
}
const lineSideReturnRows = computed(() =>
  materialIssueRequests.value.filter(
    (row) => (row.materialLotId ?? '').trim().length > 0 && returnableQuantityOf(row) > 0,
  ),
)
// 已收料且仍有可退量、却缺少物料批次的申请：后端退料要求批次（#557），取消整单会因此抛业务错误。
// 不能只在预览里隐藏——必须前置阻断确认，并提示修复路径（补录批次或先消耗），否则用户确认后整单取消失败。
const unreturnableNoLotRows = computed(() =>
  materialIssueRequests.value.filter(
    (row) => (row.materialLotId ?? '').trim().length === 0 && returnableQuantityOf(row) > 0,
  ),
)
const reservationRows = computed(() =>
  materialIssueRequests.value.filter(
    (row) =>
      (row.receivedQuantity ?? 0) <= 0 &&
      !TERMINAL_ISSUE_STATUSES.has((row.status ?? '').toLowerCase()),
  ),
)
const cancellableReceiptCount = computed(
  () =>
    finishedGoodsReceiptRequests.value.filter(
      (row) => !TERMINAL_RECEIPT_STATUSES.has((row.receiptStatus ?? '').toLowerCase()),
    ).length,
)
const cancellableTaskCount = computed(
  () =>
    operationTasks.value.filter(
      (task) => !TERMINAL_TASK_STATUSES.has((task.status ?? '').toLowerCase()),
    ).length,
)
const hasCompensation = computed(
  () =>
    reservationRows.value.length > 0 ||
    lineSideReturnRows.value.length > 0 ||
    cancellableReceiptCount.value > 0 ||
    cancellableTaskCount.value > 0,
)

const requiresRemark = computed(() => cancelForm.reasonCode === 'other')
// 后端 CancelWorkOrderCommandValidator 对最终 reason 限 500 字符。最终 reason = 「原因标签[：备注]」，
// 与 submitCancel 的拼接完全一致，据此前置限制与计数，避免用户填满后才收到后端 400。
const REASON_MAX_LENGTH = 500
const cancelReasonLabel = computed(
  () =>
    CANCEL_REASONS.find((option) => option.value === cancelForm.reasonCode)?.label ??
    cancelForm.reasonCode,
)
const finalReasonLength = computed(() => {
  const remark = cancelForm.remark.trim()
  return remark ? cancelReasonLabel.value.length + 1 + remark.length : cancelReasonLabel.value.length
})
const remarkMaxLength = computed(() =>
  Math.max(0, REASON_MAX_LENGTH - cancelReasonLabel.value.length - 1),
)
const canSubmitCancel = computed(() => {
  // 破坏性动作：两项补偿预览成功拿到数据前禁用确认（慢网/失败时不允许在空数据上确认）。
  if (!cancelPreviewReady.value) return false
  // 存在无批次却可退的已收料申请时，后端会拒绝整单取消——前置阻断，不允许确认。
  if (unreturnableNoLotRows.value.length > 0) return false
  if (!cancelForm.reasonCode) return false
  if (requiresRemark.value && !cancelForm.remark.trim()) return false
  if (finalReasonLength.value > REASON_MAX_LENGTH) return false
  return true
})

function resetCancelForm() {
  cancelForm.reasonCode = ''
  cancelForm.remark = ''
}
function openCancelDialog() {
  if (!canCancel.value) return
  activateCancelPreview()
  cancelOpen.value = true
}
async function submitCancel() {
  if (!canSubmitCancel.value || cancelWorkOrderPending.value) return
  const label =
    CANCEL_REASONS.find((option) => option.value === cancelForm.reasonCode)?.label ??
    cancelForm.reasonCode
  const remark = cancelForm.remark.trim()
  const reason = remark ? `${label}：${remark}` : label
  // 快照补偿计数：取消成功后相关查询会失效刷新，届时预留/退料列表已清空
  const releasedCount = reservationRows.value.length
  const returnCount = lineSideReturnRows.value.length
  try {
    await cancelWorkOrder(reason)
    cancelOpen.value = false
    resetCancelForm()
    notifySuccess(
      `已取消工单 ${filters.workOrderId}：${releasedCount} 项预留释放、${returnCount} 项退料指引生成。`,
    )
  } catch (error) {
    notifyError(error, '取消工单失败，请稍后重试。')
  }
}

function refreshAll() {
  void refreshDetail()
  void refreshMaterialReadiness()
}
function openRoute(path: string) {
  void router.push({
    path,
    query: {
      workOrderId: filters.workOrderId,
      skuId: detail.value?.skuId ?? undefined,
      quantity: detail.value?.quantity?.toString() ?? undefined,
    },
  })
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatStatus(value?: string | null) {
  const map: Record<string, string> = {
    blocked: '阻塞',
    cancelled: '已取消',
    closed: '已关闭',
    completed: '已完成',
    created: '已创建',
    hold: '已暂停',
    ready: '可开工',
    released: '已下达',
    running: '执行中',
    started: '已开工',
    warning: '预警',
  }
  return value ? (map[value.toLowerCase()] ?? value) : '未知'
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      :title="`工单 ${filters.workOrderId}`"
      :breadcrumbs="[{ label: '制造执行' }, { label: '工单与派工' }]"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          @click="openRoute('/mes/production-reports')"
        >
          <ClipboardCheckIcon aria-hidden="true" />
          报工记录
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="openRoute('/mes/receipts')">
          <PackageCheckIcon aria-hidden="true" />
          完工入库
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          @click="openRoute('/quality/inspections')"
        >
          <ShieldCheckIcon aria-hidden="true" />
          质量检验
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="detailPending || materialReadinessPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>

        <!-- 取消工单：仅创建/已下达/暂停可用，其余禁用并 tooltip 说明原因 -->
        <NvButton
          v-if="canCancel"
          size="sm"
          type="button"
          variant="destructive"
          @click="openCancelDialog"
        >
          <XCircleIcon aria-hidden="true" />
          取消工单
        </NvButton>
        <NvTooltipProvider v-else>
          <NvTooltip>
            <NvTooltipTrigger as-child>
              <span tabindex="0">
                <NvButton size="sm" type="button" variant="destructive" disabled>
                  <XCircleIcon aria-hidden="true" />
                  取消工单
                </NvButton>
              </span>
            </NvTooltipTrigger>
            <NvTooltipContent>{{ cancelDisabledReason }}</NvTooltipContent>
          </NvTooltip>
        </NvTooltipProvider>
      </template>
    </NvPageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvSectionCards :columns="4">
      <NvSectionCard
        description="工单状态"
        :value="formatStatus(detail?.status)"
        :hint="detail?.skuId ?? '无物料'"
      />
      <NvSectionCard
        description="计划数量"
        :value="formatQuantity(detail?.quantity)"
        hint="工单计划量"
      />
      <NvSectionCard description="工序数" :value="operationTasks.length" hint="执行任务" />
      <NvSectionCard
        description="用料状态"
        :value="formatStatus(materialReadiness?.readinessStatus)"
        hint="齐套检查"
      />
    </NvSectionCards>

    <div v-if="blockingReasons.length" class="rounded-lg border bg-background p-4">
      <h2 class="text-sm font-semibold text-foreground">开工阻塞</h2>
      <div class="mt-3 grid gap-2">
        <div
          v-for="reason in blockingReasonDisplays"
          :key="reason.code"
          class="rounded-md border border-warning/30 bg-warning/10 p-3"
        >
          <NvStatusBadge :label="reason.label" tone="warning" />
          <p class="mt-2 text-sm text-muted-foreground">{{ reason.nextStep }}</p>
        </div>
      </div>
    </div>

    <div class="grid gap-2">
      <span class="text-sm font-semibold text-foreground">工序任务</span>
      <NvDataTable
        :columns="taskColumns"
        :rows="operationTasks"
        row-key="operationTaskId"
        :loading="detailPending"
        empty-message="暂无工序任务。"
        :searchable="false"
        :column-settings="false"
      >
        <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
        <template #cell-operationSequence="{ row }"
          ><span class="tabular-nums">{{ row.operationSequence ?? 0 }}</span></template
        >
        <template #cell-startedAtUtc="{ row }">{{
          formatDateTime(row.startedAtUtc ?? row.plannedStartUtc)
        }}</template>
      </NvDataTable>
    </div>

    <div class="grid gap-2">
      <span class="text-sm font-semibold text-foreground">用料齐套</span>
      <NvDataTable
        :columns="materialColumns"
        :rows="materialRows"
        :row-key="(r) => `${r.materialId}-${r.materialLotId}`"
        :loading="materialReadinessPending"
        empty-message="暂无用料行。"
        :searchable="false"
        :column-settings="false"
      >
        <template #cell-requiredQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.requiredQuantity) }}</span></template
        >
        <template #cell-availableQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span></template
        >
        <template #cell-stagedQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.stagedQuantity) }}</span></template
        >
        <template #cell-shortageQuantity="{ row }"
          ><span class="tabular-nums">{{ formatQuantity(row.shortageQuantity) }}</span></template
        >
        <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      </NvDataTable>
    </div>

    <!-- 取消工单确认（含补偿预览区），A1 §2 破坏性动作原因必填 -->
    <NvAlertDialog v-model:open="cancelOpen">
      <NvAlertDialogContent class="sm:max-w-lg">
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>取消工单 · {{ filters.workOrderId }}</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            取消后将释放库存预留、生成线边退料指引，并取消该工单未完成的完工入库请求与工序任务。此操作不可撤销。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>

        <section aria-label="补偿预览" class="grid gap-3 rounded-lg border bg-muted/30 p-3 text-sm">
          <div class="flex items-center justify-between">
            <span class="font-semibold text-foreground">补偿预览</span>
            <span class="text-xs text-muted-foreground">按关联单据汇总</span>
          </div>

          <div v-if="cancelPreviewPending" class="flex items-center gap-2 text-muted-foreground">
            <Spinner aria-hidden="true" />
            <span>正在加载补偿预览…</span>
          </div>
          <div
            v-else-if="cancelPreviewError"
            class="grid gap-2 rounded-md border border-destructive/40 bg-destructive/5 p-2"
          >
            <p class="text-destructive">补偿预览加载失败，为避免误操作已禁用确认。请重试后再确认取消。</p>
            <NvButton
              type="button"
              variant="outline"
              size="sm"
              class="justify-self-start"
              @click="retryCancelPreview"
            >
              重试
            </NvButton>
          </div>
          <template v-else>
          <div class="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">预留释放</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ reservationRows.length }}
              </p>
            </div>
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">退料指引</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ lineSideReturnRows.length }}
              </p>
            </div>
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">完工入库请求</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ cancellableReceiptCount }}
              </p>
            </div>
            <div class="rounded-md border bg-background p-2">
              <p class="text-xs text-muted-foreground">工序任务</p>
              <p class="text-lg font-semibold tabular-nums text-foreground">
                {{ cancellableTaskCount }}
              </p>
            </div>
          </div>

          <div
            v-if="unreturnableNoLotRows.length"
            class="grid gap-1 rounded-md border border-destructive/40 bg-destructive/5 p-2"
          >
            <p class="text-xs font-medium text-destructive">
              以下已收料申请缺少物料批次，无法退料，直接取消整单会被后端拒绝。请先补录批次或消耗后再取消。
            </p>
            <ul class="max-h-24 divide-y overflow-y-auto rounded-md border bg-background">
              <li
                v-for="row in unreturnableNoLotRows"
                :key="`nolot-${row.requestId}`"
                class="flex items-center justify-between gap-2 px-2 py-1"
              >
                <span class="truncate">{{ row.materialCode ?? row.materialId }}</span>
                <span class="shrink-0 tabular-nums">{{
                  formatQuantity(returnableQuantityOf(row))
                }}</span>
              </li>
            </ul>
          </div>

          <div v-if="reservationRows.length" class="grid gap-1">
            <p class="text-xs font-medium text-muted-foreground">将释放的预留（物料 / 数量）</p>
            <ul class="max-h-28 divide-y overflow-y-auto rounded-md border bg-background">
              <li
                v-for="row in reservationRows"
                :key="`res-${row.requestId}`"
                class="flex items-center justify-between gap-2 px-2 py-1"
              >
                <span class="truncate"
                  >{{ row.materialCode ?? row.materialId
                  }}<span v-if="row.materialLotId" class="text-muted-foreground">
                    · {{ row.materialLotId }}</span
                  ></span
                >
                <span class="shrink-0 tabular-nums">{{
                  formatQuantity(row.requestedQuantity)
                }}</span>
              </li>
            </ul>
          </div>

          <div v-if="lineSideReturnRows.length" class="grid gap-1">
            <p class="text-xs font-medium text-muted-foreground">待退回线边物料（物料 / 数量）</p>
            <ul class="max-h-28 divide-y overflow-y-auto rounded-md border bg-background">
              <li
                v-for="row in lineSideReturnRows"
                :key="`ret-${row.requestId}`"
                class="flex items-center justify-between gap-2 px-2 py-1"
              >
                <span class="truncate"
                  >{{ row.materialCode ?? row.materialId
                  }}<span v-if="row.materialLotId" class="text-muted-foreground">
                    · {{ row.materialLotId }}</span
                  ></span
                >
                <span class="shrink-0 tabular-nums">{{
                  formatQuantity(returnableQuantityOf(row))
                }}</span>
              </li>
            </ul>
          </div>

          <p v-if="!hasCompensation" class="text-muted-foreground">
            该工单当前无可释放的预留或待退回线边物料，取消仅流转工单状态。
          </p>
          </template>
        </section>

        <NvFieldGroup class="grid gap-3">
          <NvField>
            <NvFieldLabel for="cancel-reason">
              取消原因 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvSelect v-model="cancelForm.reasonCode">
              <NvSelectTrigger id="cancel-reason" aria-label="取消原因">
                <NvSelectValue placeholder="选择取消原因" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in CANCEL_REASONS"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="cancel-remark">
              备注 <span v-if="requiresRemark" class="text-destructive">*</span>
            </NvFieldLabel>
            <NvInput
              id="cancel-remark"
              v-model="cancelForm.remark"
              :maxlength="remarkMaxLength"
              placeholder="补充取消说明，随请求提交并进入审计"
            />
            <p
              class="text-xs"
              :class="finalReasonLength > REASON_MAX_LENGTH ? 'text-destructive' : 'text-muted-foreground'"
            >
              取消原因（含标签）共 {{ finalReasonLength }} / {{ REASON_MAX_LENGTH }} 字符
            </p>
          </NvField>
        </NvFieldGroup>

        <NvAlertDialogFooter>
          <NvButton
            type="button"
            variant="outline"
            :disabled="cancelWorkOrderPending"
            @click="cancelOpen = false"
          >
            返回
          </NvButton>
          <NvButton
            type="button"
            variant="destructive"
            :disabled="!canSubmitCancel || cancelWorkOrderPending"
            @click="submitCancel"
          >
            <Spinner v-if="cancelWorkOrderPending" aria-hidden="true" />
            <XCircleIcon v-else aria-hidden="true" />
            确认取消工单
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </BusinessLayout>
</template>
