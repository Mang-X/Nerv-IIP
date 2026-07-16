<script setup lang="ts">
import type { BusinessConsoleMesProductionReportRow } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import {
  useMesProductionReports,
  useMesTelemetryProductionReportCandidates,
} from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
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
import { ClipboardPenIcon, RefreshCwIcon, Undo2Icon } from '@lucide/vue'
import { storeToRefs } from 'pinia'
import { computed, reactive, ref, watch } from 'vue'
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
  activateReverseDetail,
  deactivateReverseDetail,
  reverseProductionReportDetail,
  reverseProductionReportDetailError,
  reverseProductionReportDetailPending,
  reverseProductionReport,
  reverseProductionReportPending,
} = useMesProductionReports()
const { page, pageSize } = usePagedList(filters)
const candidateQueue = useMesTelemetryProductionReportCandidates()
const candidateWorkOrderId = ref('')
const candidateOperationTaskId = ref('')
const dismissalReason = ref('')
const selectedCandidateId = ref<string | null>(null)
function resetCandidateAction() {
  candidateWorkOrderId.value = ''
  candidateOperationTaskId.value = ''
  dismissalReason.value = ''
}
function toggleCandidate(candidateId?: string) {
  resetCandidateAction()
  selectedCandidateId.value =
    selectedCandidateId.value === candidateId ? null : (candidateId ?? null)
}

// 冲销是写操作(网关按 business.mes.reporting.write 鉴权)。页面准入只需 read,故写权限需单独门控:
// 无写权限的只读角色不展示冲销/重新报工入口,避免看到必然 403 的破坏性动作(AGENTS.md §5 权限同步)。
const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const canReport = computed(() =>
  (principal.value?.permissionCodes ?? []).includes(P.mesReportingWrite),
)

const quickViewWorkOrderId = ref<string | null>(null)

const errorMessage = computed(() => formatError(productionReportsError.value))

type ReportRow = BusinessConsoleMesProductionReportRow

// 冲销互链两个方向均由后端逐行投影,跨服务端分页稳定,不从当前页推断:
//   冲销行  → reversedReportNo(指向被冲销的原报工)
//   已冲销原报工 → reversalReportNo(指向冲销它的负向记录),非空即"已冲销"
// 原单与冲销单常因 ReportedAtUtc 倒序分处不同页,靠当前页 map 会误判原单未冲销、错误重开冲销(review 修复)。
function isReversalRow(row: ReportRow) {
  return Boolean(row.reversedReportNo?.trim())
}
function isAlreadyReversed(row: ReportRow) {
  return Boolean(row.reversalReportNo?.trim())
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
  return row.reversalReportNo?.trim() || undefined
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
// 跨页定位:对方记录在其它服务端分页时,scrollTo 找不到 DOM。此时按对方报工单号过滤列表(keyword 匹配
// reportNo)并回到第一页,拉取到后由 watch 滚动高亮。pendingLocateReportNo 记录待定位目标。
const pendingLocateReportNo = ref<string | null>(null)
function scrollToReport(reportNo: string) {
  // 用属性遍历匹配,避免依赖 CSS.escape(测试环境无该 API),reportNo 亦无需转义
  const el = Array.from(document.querySelectorAll('[data-report-no]')).find(
    (node) => node.getAttribute('data-report-no') === reportNo,
  )
  el?.scrollIntoView({ behavior: 'smooth', block: 'center' })
  return Boolean(el)
}
function linkToCounterpart(row: ReportRow) {
  const counterpart = counterpartReportNo(row)
  if (!counterpart) return
  focusPair(row)
  // 对方已在当前页:直接滚动高亮
  if (scrollToReport(counterpart)) return
  // 对方在别的分页:按其报工单号筛选并回第一页,等结果返回后再定位(见下方 watch)
  pendingLocateReportNo.value = counterpart
  filters.keyword = counterpart
  page.value = 1
}
function clearLocateFilter() {
  pendingLocateReportNo.value = null
  filters.keyword = ''
  page.value = 1
  clearFocus()
}
// 跨页定位过滤拉取到对方记录后:滚动到它并只高亮它(原单已不在本页,双向高亮退化为定位对方)。
// flush:'post' 确保在列表 DOM 用新结果重渲染之后再执行,scrollToReport 才能命中新行。
watch(
  productionReports,
  (rows) => {
    const target = pendingLocateReportNo.value
    if (!target) return
    if (rows.some((r) => r.reportNo === target)) {
      scrollToReport(target)
      focusedPair.value = new Set([target])
      pendingLocateReportNo.value = null
    }
  },
  { flush: 'post' },
)

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
// 冲销意图以报工单号为准存活:同一报工从打开→(经确认框 Escape / 组件自身 close /「返回」后)重开→重试,
// 始终复用同一意图,命中后端 handler 幂等重放分支(真机记录里出现过服务端已提交却返回 502)。
// 后端 fingerprint = ReportNo + Reason + ReversedAtUtc(endpoint 对缺失 reversedAtUtc 每次补新 UtcNow),
// CodeAllocator 对"同 key 不同 fingerprint"抛冲突而非重放。故意图必须冻结参与 fingerprint 的全部字段:
//   - idempotencyKey / reversedAtUtc:打开时冻结;
//   - reason(reasonCode + remark):**首次提交时冻结**,重试与重开都沿用同一 reason(避免用户改原因导致
//     fingerprint 变而冲突)。**只在冲销成功后清除**;未发送过的意图若用户「返回」明确放弃,则整个丢弃、
//     下次打开重新生成 key + timestamp + reason(见 cancelReverse)。
interface ReverseIntent {
  idempotencyKey: string
  reversedAtUtc: string
  reasonCode?: string
  remark?: string
}
const reverseIntents = reactive(new Map<string, ReverseIntent>())
function reverseIntentFor(reportNo: string): ReverseIntent {
  let intent = reverseIntents.get(reportNo)
  if (!intent) {
    intent = {
      idempotencyKey: `reverse-${reportNo}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
      reversedAtUtc: new Date().toISOString(),
    }
    reverseIntents.set(reportNo, intent)
  }
  return intent
}
// 原因已冻结(意图已提交过):重试须沿用同一 reason,锁定表单不许改。
const reverseReasonLocked = computed(() => {
  const reportNo = reverseTarget.value?.reportNo
  return Boolean(reportNo && reverseIntents.get(reportNo)?.reasonCode !== undefined)
})
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
const reverseProductionReportDetailMatchesTarget = computed(
  () =>
    !!reverseTarget.value?.reportNo &&
    reverseProductionReportDetail.value?.report?.reportNo === reverseTarget.value.reportNo,
)
const canSubmitReverse = computed(() => {
  if (!reverseTarget.value?.reportNo) return false
  if (!reverseForm.reasonCode) return false
  if (requiresRemark.value && !reverseForm.remark.trim()) return false
  if (finalReasonLength.value > REASON_MAX_LENGTH) return false
  if (reverseProductionReportDetailPending.value || reverseProductionReportDetailError.value) return false
  if (!reverseProductionReportDetailMatchesTarget.value) return false
  return true
})
const reverseConsumedMaterialLots = computed(() => {
  if (!reverseProductionReportDetailMatchesTarget.value) return []
  return reverseProductionReportDetail.value?.consumedMaterialLots ?? []
})

function composeReason(reasonCode: string, remark: string): string {
  const label = REVERSE_REASONS.find((option) => option.value === reasonCode)?.label ?? reasonCode
  const trimmed = remark.trim()
  return trimmed ? `${label}：${trimmed}` : label
}
function openReverse(row: ReportRow) {
  if (!canReport.value || !canReverse(row) || !row.reportNo) return
  reverseTarget.value = row
  // 确保本意图(key + reversedAtUtc)已冻结(重开复用,不重新生成)
  const intent = reverseIntentFor(row.reportNo)
  // 已提交过(reason 已冻结,结果未知)→ 恢复原因并锁定,重试沿用同一 fingerprint;否则空表单自由选。
  reverseForm.reasonCode = intent.reasonCode ?? ''
  reverseForm.remark = intent.remark ?? ''
  activateReverseDetail(row.reportNo)
  reverseOpen.value = true
}
// 统一处理确认框开关(替代 v-model:open):请求进行中禁止关闭——Escape / 点遮罩 / 组件自身 close 都拦下,
// 避免未决冲销意图被中途关闭后重开换 key。
function onReverseOpenChange(next: boolean) {
  if (!next && reverseProductionReportPending.value) return
  reverseOpen.value = next
  if (!next) deactivateReverseDetail()
}
// 「返回」= 明确放弃:该意图从未发送(reason 未冻结)则整个丢弃,下次打开生成新 key + timestamp + reason;
// 已发送过的意图(结果未知)保留,以便重开沿用同一 fingerprint 命中幂等重放。
function cancelReverse() {
  const reportNo = reverseTarget.value?.reportNo
  if (reportNo && reverseIntents.get(reportNo)?.reasonCode === undefined) {
    reverseIntents.delete(reportNo)
  }
  reverseOpen.value = false
  deactivateReverseDetail()
}
async function submitReverse() {
  const row = reverseTarget.value
  if (!row?.reportNo || !canSubmitReverse.value || reverseProductionReportPending.value) return
  const intent = reverseIntentFor(row.reportNo)
  // 首次提交冻结 reason(reasonCode + remark);之后重试沿用冻结值,即使用户在重开后改了表单也不采用,
  // 保证 fingerprint(ReportNo + Reason + ReversedAtUtc)跨重试完全稳定。
  if (intent.reasonCode === undefined) {
    reverseIntents.set(row.reportNo, {
      ...intent,
      reasonCode: reverseForm.reasonCode,
      remark: reverseForm.remark.trim(),
    })
  }
  const frozen = reverseIntents.get(row.reportNo)!
  const reason = composeReason(frozen.reasonCode ?? '', frozen.remark ?? '')
  try {
    const response = await reverseProductionReport(row.reportNo, {
      reason,
      // 复用本报工意图冻结的 reason + reversedAtUtc + key,超时/失败后重试或关闭重开都保持 fingerprint 稳定,
      // 命中后端幂等重放(仅冻结 key、每次让 endpoint 补新 UtcNow 或改 reason 都会因 fingerprint 变而冲突)
      reversedAtUtc: intent.reversedAtUtc,
      idempotencyKey: intent.idempotencyKey,
    })
    // 意图已成功兑现,清除该报工的意图
    reverseIntents.delete(row.reportNo)
    reverseOpen.value = false
    deactivateReverseDetail()
    const reversalNo = response?.data?.reportNo
    notifySuccess(
      `已冲销报工 ${row.reportNo}${reversalNo ? `,生成冲销单 ${reversalNo}` : ''}；工单累计数量已回退。`,
    )
  } catch (error) {
    // 失败保留 key:同一确认框重试、或 Escape 关闭后重开,都复用同一 key 命中后端幂等重放,不产生第二次意图
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
async function promoteCandidate(candidate: {
  candidateId?: string
  workOrderId?: string | null
  operationTaskId?: string | null
}) {
  if (!candidate.candidateId) return
  const workOrderId = candidateWorkOrderId.value.trim() || candidate.workOrderId?.trim()
  const operationTaskId = candidateOperationTaskId.value.trim() || candidate.operationTaskId?.trim()
  if (!workOrderId || !operationTaskId) return
  await candidateQueue.promote(candidate.candidateId, workOrderId, operationTaskId)
  selectedCandidateId.value = null
  resetCandidateAction()
}
async function dismissCandidate(candidateId?: string) {
  if (!candidateId || !dismissalReason.value.trim()) return
  await candidateQueue.dismiss(candidateId, dismissalReason.value.trim())
  selectedCandidateId.value = null
  resetCandidateAction()
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

    <!-- 跨页互链定位:点击「查看冲销单/冲销自」时若对方在别页,按其单号筛选定位;给出可清除的说明 -->
    <div
      v-if="filters.keyword"
      class="flex items-center justify-between gap-3 rounded-md border bg-muted/40 px-3 py-2 text-sm"
    >
      <span class="text-muted-foreground">
        已按报工单「<span class="font-medium text-foreground">{{ filters.keyword }}</span
        >」筛选以定位关联记录。
      </span>
      <NvButton size="sm" type="button" variant="outline" @click="clearLocateFilter">
        清除筛选
      </NvButton>
    </div>

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
            查看冲销单 {{ row.reversalReportNo }}
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
        <!-- 冲销/重新报工都是写操作:无 reporting.write 权限的只读角色不展示,避免必然 403 的破坏性入口 -->
        <div v-if="canReport" class="flex items-center justify-end gap-1">
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
        <span v-else class="text-muted-foreground">—</span>
      </template>
    </NvDataTable>

    <!-- 冲销确认(破坏性,原因必填),A1 §2.5。用 :open + @update:open 统一拦截关闭(pending 时禁关) -->
    <NvAlertDialog :open="reverseOpen" @update:open="onReverseOpenChange">
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
          <div class="space-y-2 border-t pt-3">
            <h3 class="text-sm font-medium">物料批次消耗</h3>
            <p
              v-if="reverseProductionReportDetailError"
              class="text-xs text-destructive"
              role="alert"
            >
              物料消耗明细加载失败，请稍后重试。为避免不完整冲销，当前无法确认。
            </p>
            <p
              v-else-if="
                reverseProductionReportDetailPending ||
                !reverseProductionReportDetailMatchesTarget
              "
              class="text-xs text-muted-foreground"
            >
              正在加载物料消耗明细…
            </p>
            <p
              v-else-if="reverseConsumedMaterialLots.length === 0"
              class="text-xs text-muted-foreground"
            >
              本次报工没有物料批次消耗。
            </p>
            <ul v-else class="space-y-2" aria-label="物料批次消耗明细">
              <li
                v-for="lot in reverseConsumedMaterialLots"
                :key="`${lot.materialId}-${lot.materialLotId}-${lot.materialIssueRequestNo}`"
                class="grid gap-1 rounded-md border px-3 py-2 text-xs sm:grid-cols-2"
              >
                <span>物料：{{ lot.materialId ?? '无' }}</span>
                <span>物料批次：{{ lot.materialLotId ?? '无' }}</span>
                <span>
                  数量：{{ formatQuantity(lot.consumedQuantity) }} {{ lot.uomCode ?? '' }}
                </span>
                <span>领料申请：{{ lot.materialIssueRequestNo ?? '无' }}</span>
              </li>
            </ul>
          </div>
        </section>

        <NvFieldGroup class="grid gap-3">
          <p
            v-if="reverseReasonLocked"
            class="rounded-md border border-warning/30 bg-warning/10 px-3 py-2 text-xs text-muted-foreground"
          >
            本次冲销已提交但结果未确定。为保证幂等重放,只能沿用首次的冲销原因重试,不能更换原因——「返回」也不会放弃本次请求。如需改用其它原因,请先在报工列表核实上次冲销是否已生效,再据实处理。
          </p>
          <NvField>
            <NvFieldLabel for="reverse-reason">
              冲销原因 <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvSelect v-model="reverseForm.reasonCode" :disabled="reverseReasonLocked">
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
              :disabled="reverseReasonLocked"
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
            @click="cancelReverse"
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

    <section class="mt-8 space-y-4" aria-labelledby="telemetry-candidate-title">
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 id="telemetry-candidate-title" class="text-lg font-semibold">遥测报工待确认</h2>
          <p class="text-sm text-muted-foreground">
            来自设备计数的真实草稿与挂起记录，共 {{ candidateQueue.total.value }} 条。
          </p>
        </div>
        <div class="flex gap-2">
          <NvSelect v-model="candidateQueue.filters.status"
            ><NvSelectTrigger class="w-32"><NvSelectValue /></NvSelectTrigger
            ><NvSelectContent
              ><NvSelectItem value="pending-confirmation">待确认</NvSelectItem
              ><NvSelectItem value="draft">草稿</NvSelectItem
              ><NvSelectItem value="all">全部</NvSelectItem></NvSelectContent
            ></NvSelect
          >
          <NvButton variant="outline" size="sm" @click="candidateQueue.refresh">刷新队列</NvButton>
        </div>
      </div>
      <p v-if="candidateQueue.error.value" class="text-sm text-destructive" role="alert">
        {{ formatError(candidateQueue.error.value) }}
      </p>
      <div v-if="candidateQueue.candidates.value.length" class="space-y-3">
        <article
          v-for="candidate in candidateQueue.candidates.value"
          :key="candidate.candidateId"
          class="rounded-lg border border-border bg-card p-4"
        >
          <div class="flex flex-wrap justify-between gap-3">
            <div>
              <p class="font-medium">{{ candidate.deviceAssetId }} · {{ candidate.tagKey }}</p>
              <p class="text-sm text-muted-foreground">
                {{ candidate.goodQuantity }} 件 · {{ formatDateTime(candidate.bucketEndUtc) }} ·
                {{ candidate.suspensionReason ?? candidate.status }}
              </p>
            </div>
            <NvButton size="sm" variant="outline" @click="toggleCandidate(candidate.candidateId)"
              >处理</NvButton
            >
          </div>
          <div
            v-if="selectedCandidateId === candidate.candidateId"
            class="mt-4 grid gap-3 md:grid-cols-2"
          >
            <label class="text-sm"
              >工单<NvInput
                v-model="candidateWorkOrderId"
                :placeholder="candidate.workOrderId ?? '输入真实工单号'"
                class="mt-1"
            /></label>
            <label class="text-sm"
              >工序任务<NvInput
                v-model="candidateOperationTaskId"
                :placeholder="candidate.operationTaskId ?? '输入真实工序任务号'"
                class="mt-1"
            /></label>
            <label class="text-sm md:col-span-2"
              >忽略原因<NvInput v-model="dismissalReason" placeholder="忽略时必填" class="mt-1"
            /></label>
            <div class="flex gap-2 md:col-span-2">
              <NvButton
                size="sm"
                :disabled="candidateQueue.actionPending.value"
                @click="promoteCandidate(candidate)"
                >确认并转正</NvButton
              ><NvButton
                size="sm"
                variant="outline"
                :disabled="candidateQueue.actionPending.value || !dismissalReason.trim()"
                @click="dismissCandidate(candidate.candidateId)"
                >忽略</NvButton
              >
            </div>
          </div>
        </article>
      </div>
      <p
        v-else-if="!candidateQueue.pending.value"
        class="rounded-lg border border-dashed border-border p-8 text-center text-sm text-muted-foreground"
      >
        当前没有遥测报工候选。
      </p>
    </section>

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
