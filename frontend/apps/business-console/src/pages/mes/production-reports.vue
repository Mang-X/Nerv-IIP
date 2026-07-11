<script setup lang="ts">
import type { BusinessConsoleMesProductionReportRow } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { useMesProductionReports } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
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
import { ClipboardPenIcon, RefreshCwIcon, Undo2Icon } from 'lucide-vue-next'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '报工记录',
    requiredPermissions: ['business.mes.reporting.read'],
  },
})

const router = useRouter()
const {
  filters,
  productionReports,
  productionReportsError,
  productionReportsPending,
  productionReportsTotal,
  refreshProductionReports,
  reverseProductionReport,
  reverseProductionReportPending,
} = useMesProductionReports()
const { page, pageSize } = usePagedList(filters)

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = BusinessConsoleMesProductionReportRow

// 冲销互链索引:原报工号 -> 冲销该报工的负向记录行。冲销行 reversedReportNo 指向原报工号(MES 事实层透传)。
const reversalByOriginal = computed(() => {
  const map = new Map<string, ReportRow>()
  for (const row of productionReports.value) {
    const target = row.reversedReportNo?.trim()
    if (target) map.set(target, row)
  }
  return map
})
function isReversalRow(row: ReportRow) {
  return Boolean(row.reversedReportNo?.trim())
}
function isAlreadyReversed(row: ReportRow) {
  return Boolean(row.reportNo && reversalByOriginal.value.has(row.reportNo))
}
function isClosedWorkOrder(row: ReportRow) {
  return (row.workOrderStatus ?? '').toLowerCase() === 'closed'
}
// 前端冲销分级(A1 §2):可冲销 = 有报工单号、非冲销行、未被冲销、所属工单未关闭。
// 与后端 ReverseProductionReportCommandHandler 的四重业务拒绝对齐(冲销行不可再冲销 / 原报工已冲销 /
// 已关闭工单 / 产出批次已入库),构成「前端禁 + 后端拒」双层拦截。产出批次已入库这一条列表无字段可判,
// 由后端拒绝 + 友好文案兜底。
function canReverse(row: ReportRow) {
  return (
    Boolean(row.reportNo) &&
    !isReversalRow(row) &&
    !isAlreadyReversed(row) &&
    !isClosedWorkOrder(row)
  )
}
function reverseDisabledReason(row: ReportRow) {
  if (!row.reportNo) return '该报工缺少报工单号,无法冲销。'
  if (isReversalRow(row)) return '这是一条冲销记录,冲销单不能再次冲销。'
  if (isAlreadyReversed(row)) return '该报工已冲销,不能重复冲销;如需再记录请「重新报工」。'
  if (isClosedWorkOrder(row)) return '所属工单已关闭,不允许冲销报工。'
  return ''
}
function hasLink(row: ReportRow) {
  return isReversalRow(row) || isAlreadyReversed(row)
}
function counterpartReportNo(row: ReportRow): string | undefined {
  if (isReversalRow(row)) return row.reversedReportNo?.trim() || undefined
  return row.reportNo ? reversalByOriginal.value.get(row.reportNo)?.reportNo : undefined
}

// 双向互链高亮:悬停/点击互链的一行时,原单与冲销单两行的报工单列同时高亮。
const focusedPair = ref<Set<string>>(new Set())
function focusPair(row: ReportRow) {
  const set = new Set<string>()
  if (row.reportNo) set.add(row.reportNo)
  const counterpart = counterpartReportNo(row)
  if (counterpart) set.add(counterpart)
  focusedPair.value = set
}
function clearFocus() {
  focusedPair.value = new Set()
}
function isFocused(row: ReportRow) {
  return Boolean(row.reportNo && focusedPair.value.has(row.reportNo))
}
function linkToCounterpart(row: ReportRow) {
  const counterpart = counterpartReportNo(row)
  if (!counterpart) return
  focusPair(row)
  const el = document.querySelector(`[data-report-no="${CSS.escape(counterpart)}"]`)
  el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
}

// 冲销确认(破坏性动作,原因必填,A1 §2.5)。最终 reason = 原因标签[:备注],后端 MaximumLength(500)。
const REVERSE_REASONS = [
  { value: 'mis-report', label: '误报工' },
  { value: 'wrong-quantity', label: '数量填错' },
  { value: 'wrong-operation', label: '报错工序' },
  { value: 'quality-rework', label: '质量返工冲抵' },
  { value: 'duplicate', label: '重复报工' },
  { value: 'other', label: '其他（备注必填）' },
] as const
const REASON_MAX_LENGTH = 500

const reverseOpen = ref(false)
const reverseTarget = ref<ReportRow | null>(null)
const reverseForm = reactive({ reasonCode: '', remark: '' })
const requiresRemark = computed(() => reverseForm.reasonCode === 'other')
const reverseReasonLabel = computed(
  () =>
    REVERSE_REASONS.find((option) => option.value === reverseForm.reasonCode)?.label ??
    reverseForm.reasonCode,
)
const finalReasonLength = computed(() => {
  const remark = reverseForm.remark.trim()
  return remark
    ? reverseReasonLabel.value.length + 1 + remark.length
    : reverseReasonLabel.value.length
})
const remarkMaxLength = computed(() =>
  Math.max(0, REASON_MAX_LENGTH - reverseReasonLabel.value.length - 1),
)
const canSubmitReverse = computed(() => {
  if (!reverseTarget.value?.reportNo) return false
  if (!reverseForm.reasonCode) return false
  if (requiresRemark.value && !reverseForm.remark.trim()) return false
  if (finalReasonLength.value > REASON_MAX_LENGTH) return false
  return true
})

function openReverse(row: ReportRow) {
  if (!canReverse(row)) return
  reverseTarget.value = row
  reverseForm.reasonCode = ''
  reverseForm.remark = ''
  reverseOpen.value = true
}
async function submitReverse() {
  const row = reverseTarget.value
  if (!row?.reportNo || !canSubmitReverse.value || reverseProductionReportPending.value) return
  const remark = reverseForm.remark.trim()
  const reason = remark ? `${reverseReasonLabel.value}：${remark}` : reverseReasonLabel.value
  try {
    const response = await reverseProductionReport(row.reportNo, {
      reason,
      idempotencyKey: `reverse-${row.reportNo}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    })
    reverseOpen.value = false
    const reversalNo = response?.data?.reportNo
    notifySuccess(
      `已冲销报工 ${row.reportNo}${reversalNo ? `,生成冲销单 ${reversalNo}` : ''}；工单累计数量已回退。`,
    )
  } catch (error) {
    notifyError(error, '冲销报工失败,请稍后重试。')
  }
}

// 重新报工:取该报工的工单 / 工序作为上下文,预填工单页报工弹窗。冲销行的工单/工序沿用原报工,
// 故在冲销记录行上「重新报工」即为原工单 / 原工序重新录一单。
function reReport(row: ReportRow) {
  if (!row.workOrderId || !row.operationTaskId) return
  void router.push({
    path: '/mes/work-orders',
    query: { workOrderId: row.workOrderId, operationTaskId: row.operationTaskId },
  })
}

const columns: NvDataTableColumn<ReportRow>[] = [
  { key: 'reportNo', header: '报工单', cellClass: 'font-medium' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'output', header: '产量', accessor: (r) => r.goodQuantity ?? 0 },
  {
    key: 'operationTaskId',
    header: '工序任务',
    accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无',
  },
  { key: 'reportedAtUtc', header: '报工时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-40' },
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
      <template #cell-reportNo="{ row }">
        <div
          :data-report-no="row.reportNo ?? undefined"
          class="-mx-1 flex flex-col gap-1 rounded-md px-1 py-0.5 transition-colors"
          :class="isFocused(row) ? 'bg-brand/10 ring-1 ring-brand/40' : ''"
          @mouseenter="hasLink(row) && focusPair(row)"
          @mouseleave="clearFocus"
        >
          <div class="flex items-center gap-1.5">
            <span>{{ row.reportNo ?? row.productionReportId ?? '无' }}</span>
            <NvStatusBadge v-if="isReversalRow(row)" label="负向冲销" tone="danger" />
            <NvStatusBadge v-else-if="isAlreadyReversed(row)" label="已冲销" tone="warning" />
          </div>
          <button
            v-if="isReversalRow(row)"
            type="button"
            class="inline-flex w-fit items-center gap-1 text-xs text-destructive underline-offset-4 hover:underline"
            @click="linkToCounterpart(row)"
          >
            <Undo2Icon class="size-3" aria-hidden="true" />
            冲销自 {{ row.reversedReportNo }}
          </button>
          <button
            v-else-if="isAlreadyReversed(row)"
            type="button"
            class="inline-flex w-fit items-center gap-1 text-xs text-warning underline-offset-4 hover:underline"
            @click="linkToCounterpart(row)"
          >
            <Undo2Icon class="size-3" aria-hidden="true" />
            查看冲销单 {{ reversalByOriginal.get(row.reportNo ?? '')?.reportNo }}
          </button>
        </div>
      </template>

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
        <div
          class="flex flex-col gap-0.5 tabular-nums"
          :class="isReversalRow(row) ? 'text-destructive' : ''"
        >
          <span>良品 {{ formatQuantity(row.goodQuantity) }}</span>
          <span
            v-if="(row.scrapQuantity ?? 0) !== 0"
            class="text-xs"
            :class="isReversalRow(row) ? 'text-destructive' : 'text-warning'"
          >
            报废 {{ formatQuantity(row.scrapQuantity) }}
          </span>
          <span v-else class="text-xs text-muted-foreground">报废 0</span>
          <span
            v-if="(row.reworkQuantity ?? 0) !== 0"
            class="text-xs"
            :class="isReversalRow(row) ? 'text-destructive' : 'text-muted-foreground'"
          >
            返工 {{ formatQuantity(row.reworkQuantity) }}
          </span>
        </div>
      </template>

      <template #cell-reportedAtUtc="{ row }">{{ formatDateTime(row.reportedAtUtc) }}</template>

      <template #cell-actions="{ row }">
        <div class="flex items-center justify-end gap-1">
          <!-- 冲销记录行:唯一有意义的后续动作是重新报工(冲销单不可再冲销) -->
          <NvButton
            v-if="isReversalRow(row)"
            size="sm"
            type="button"
            variant="outline"
            @click="reReport(row)"
          >
            <ClipboardPenIcon aria-hidden="true" />
            重新报工
          </NvButton>
          <!-- 可冲销:高频破坏性动作提为行内按钮 -->
          <NvButton
            v-else-if="canReverse(row)"
            size="sm"
            type="button"
            variant="destructive"
            @click="openReverse(row)"
          >
            <Undo2Icon aria-hidden="true" />
            冲销
          </NvButton>
          <!-- 不可冲销(已冲销 / 工单已关闭 / 缺单号):禁用并 tooltip 说明原因 -->
          <NvTooltipProvider v-else>
            <NvTooltip>
              <NvTooltipTrigger as-child>
                <span tabindex="0">
                  <NvButton size="sm" type="button" variant="destructive" disabled>
                    <Undo2Icon aria-hidden="true" />
                    冲销
                  </NvButton>
                </span>
              </NvTooltipTrigger>
              <NvTooltipContent>{{ reverseDisabledReason(row) }}</NvTooltipContent>
            </NvTooltip>
          </NvTooltipProvider>
        </div>
      </template>
    </NvDataTable>

    <!-- 冲销确认(破坏性,原因必填),A1 §2.5 -->
    <NvAlertDialog v-model:open="reverseOpen">
      <NvAlertDialogContent class="sm:max-w-lg">
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>冲销报工 · {{ reverseTarget?.reportNo }}</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            冲销将生成一条负向报工记录,回退工单累计良品/报废数量,并同步回退产出批次谱系与物料消耗。此操作不可撤销。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>

        <section
          v-if="reverseTarget"
          aria-label="原报工明细"
          class="grid gap-2 rounded-lg border bg-muted/30 p-3 text-sm"
        >
          <div class="flex items-center justify-between">
            <span class="font-semibold text-foreground">原报工明细</span>
            <span class="text-xs text-muted-foreground">只读</span>
          </div>
          <dl class="grid grid-cols-2 gap-x-4 gap-y-1.5">
            <div class="flex justify-between gap-2">
              <dt class="text-muted-foreground">工单</dt>
              <dd class="font-medium">
                {{ reverseTarget.workOrderNo ?? reverseTarget.workOrderId ?? '无' }}
              </dd>
            </div>
            <div class="flex justify-between gap-2">
              <dt class="text-muted-foreground">工序任务</dt>
              <dd class="font-medium">
                {{ reverseTarget.operationTaskNo ?? reverseTarget.operationTaskId ?? '无' }}
              </dd>
            </div>
            <div class="flex justify-between gap-2">
              <dt class="text-muted-foreground">良品</dt>
              <dd class="font-medium tabular-nums">
                {{ formatQuantity(reverseTarget.goodQuantity) }}
              </dd>
            </div>
            <div class="flex justify-between gap-2">
              <dt class="text-muted-foreground">报废</dt>
              <dd class="font-medium tabular-nums">
                {{ formatQuantity(reverseTarget.scrapQuantity) }}
              </dd>
            </div>
            <div class="flex justify-between gap-2">
              <dt class="text-muted-foreground">返工</dt>
              <dd class="font-medium tabular-nums">
                {{ formatQuantity(reverseTarget.reworkQuantity) }}
              </dd>
            </div>
            <div class="flex justify-between gap-2">
              <dt class="text-muted-foreground">报工时间</dt>
              <dd class="font-medium">{{ formatDateTime(reverseTarget.reportedAtUtc) }}</dd>
            </div>
          </dl>
          <p class="text-xs text-muted-foreground">
            消耗批次与产出批次谱系将由后端随冲销整体回退(负向消耗),此处不逐条列示。
          </p>
        </section>

        <NvFieldGroup class="grid gap-3">
          <NvField>
            <NvFieldLabel for="reverse-reason">
              冲销原因 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvSelect v-model="reverseForm.reasonCode">
              <NvSelectTrigger id="reverse-reason" aria-label="冲销原因">
                <NvSelectValue placeholder="选择冲销原因" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="option in REVERSE_REASONS"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="reverse-remark">
              备注 <span v-if="requiresRemark" class="text-destructive">*</span>
            </NvFieldLabel>
            <NvInput
              id="reverse-remark"
              v-model="reverseForm.remark"
              :maxlength="remarkMaxLength"
              placeholder="补充冲销说明,随请求提交并进入审计"
            />
            <p
              class="text-xs"
              :class="
                finalReasonLength > REASON_MAX_LENGTH ? 'text-destructive' : 'text-muted-foreground'
              "
            >
              冲销原因（含标签）共 {{ finalReasonLength }} / {{ REASON_MAX_LENGTH }} 字符
            </p>
          </NvField>
        </NvFieldGroup>

        <NvAlertDialogFooter>
          <NvButton
            type="button"
            variant="outline"
            :disabled="reverseProductionReportPending"
            @click="reverseOpen = false"
          >
            返回
          </NvButton>
          <NvButton
            type="button"
            variant="destructive"
            :disabled="!canSubmitReverse || reverseProductionReportPending"
            @click="submitReverse"
          >
            <Spinner v-if="reverseProductionReportPending" aria-hidden="true" />
            <Undo2Icon v-else aria-hidden="true" />
            确认冲销
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
