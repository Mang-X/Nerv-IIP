<script setup lang="ts">
import type {
  BusinessConsoleInspectionPlanCharacteristicItem,
  BusinessConsoleQualityInspectionTaskItem,
} from '@nerv-iip/api-client'
import {
  characteristicRowOutOfTolerance,
  createQualityCharacteristicDraft,
  inspectionTaskSourceTypeLabel,
  INSPECTION_TASK_SOURCE_TYPES,
  qualityCharacteristicRowsValid,
  qualityInspectionOverallVerdict,
  qualityInspectionTaskFlow,
  toQualityCharacteristicResultLines,
  type QualityCharacteristicDraftRow,
  type QualityInspectionTaskCtx,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileButton,
  NvMobileInput,
  NvMobileResult,
  NvMobileTag,
  NvNumberKeyboard,
  NvPicker,
  NvScanBar,
  NvSearchBar,
  type PickerOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import RetryableListError from '@/components/RetryableListError.vue'
import {
  useBusinessQualityInspectionTasks,
  useInspectionPlanCharacteristics,
} from '@/composables/useBusinessQualityInspectionTasks'

type PlanCharacteristic = BusinessConsoleInspectionPlanCharacteristicItem

definePage({
  meta: {
    requiresAuth: true,
    title: '检验任务',
  },
})

type Task = BusinessConsoleQualityInspectionTaskItem
interface DraftRow extends QualityCharacteristicDraftRow {
  id: number
  /** 特性中文名（来自计划，仅展示）。 */
  name: string
}

const router = useRouter()

const {
  tasks,
  total,
  loaded,
  hasMore,
  loadMore,
  pending,
  error,
  refresh,
  reasonCodes,
  submitInspection,
  submitPending,
} = useBusinessQualityInspectionTasks()

// --- 结果反馈：提交成功呈现检验结论；操作失败（网络等）单独态 ---------------------
type ResultState =
  | { phase: 'success'; verdict: 'pass' | 'fail' }
  | { phase: 'error'; message: string }
const result = ref<ResultState | null>(null)

// --- 列表：来源筛选 chips + 扫码直达 + 超期置顶（均客户端，facade 无对应参数）--------
const selectedTask = ref<Task | null>(null)
const scanKeyword = ref('')
const sourceTypeFilter = ref<string | null>(null)

// 选中任务的检验计划特性（「可选可搜」的数据源；单位/公差/类型由此直接匹配特性）。
const {
  characteristics: planCharacteristics,
  planCode: planCode,
  pending: planCharacteristicsPending,
} = useInspectionPlanCharacteristics(computed(() => selectedTask.value?.inspectionPlanId))

// 计划展示：优先人读的 planCode，未加载时回退任务上的计划 GUID。
const planLabel = computed(() => planCode.value || selectedTask.value?.inspectionPlanId || '')

// 当前处于列表步（无选中任务、无结果浮层）：ScanBar 抢焦点仅在此。
const inListStep = computed(() => selectedTask.value === null && result.value === null)

function nowMs() {
  return Date.now()
}
function isOverdue(task: Task) {
  if (!task.dueAtUtc) return false
  const due = new Date(task.dueAtUtc).getTime()
  return Number.isFinite(due) && due < nowMs()
}

function matchesScan(task: Task) {
  const kw = scanKeyword.value.trim().toLowerCase()
  if (!kw) return true
  return [task.skuCode, task.sourceDocumentId, task.batchNo, task.serialNo].some((v) =>
    (v ?? '').toLowerCase().includes(kw),
  )
}

// 扫码 / 来源筛选后的集合（客户端）。
const filteredTasks = computed(() =>
  tasks.value.filter(
    (t) =>
      matchesScan(t) &&
      (sourceTypeFilter.value === null || t.sourceType === sourceTypeFilter.value),
  ),
)

// 超期置顶：超期在前（按到期时间升序，最紧急靠前），其余按到期时间升序、无到期排最后。
const displayTasks = computed(() =>
  [...filteredTasks.value].sort((a, b) => {
    const overdueDiff = Number(isOverdue(b)) - Number(isOverdue(a))
    if (overdueDiff !== 0) return overdueDiff
    const da = a.dueAtUtc ? new Date(a.dueAtUtc).getTime() : Number.POSITIVE_INFINITY
    const db = b.dueAtUtc ? new Date(b.dueAtUtc).getTime() : Number.POSITIVE_INFINITY
    return da - db
  }),
)

// 来源 chips：全部 + 三类来源，计数取扫码过滤后的集合（诚实反映当前可见范围）。
const scanFiltered = computed(() => tasks.value.filter(matchesScan))
const sourceChips = computed(() =>
  INSPECTION_TASK_SOURCE_TYPES.map((type) => ({
    type,
    label: inspectionTaskSourceTypeLabel(type),
    count: scanFiltered.value.filter((t) => t.sourceType === type).length,
  })),
)

function onScan(value: string) {
  scanKeyword.value = value
}
function clearScan() {
  scanKeyword.value = ''
}
function pickSourceType(type: string | null) {
  sourceTypeFilter.value = type
}

// --- 逐特性录结果（特性来自检验计划，可选可搜；单位/公差/类别自动匹配特性）---------
let nextRowId = 1
const rows = reactive<DraftRow[]>([])

function resetRows() {
  rows.splice(0, rows.length)
  nextRowId = 1
  dispositionReason.value = ''
}
function removeRow(id: number) {
  const index = rows.findIndex((r) => r.id === id)
  if (index >= 0) rows.splice(index, 1)
}

// 计划特性类型 → 录入行类别（variable 计量 / attribute 计数）。
function kindOfCharacteristic(c: PlanCharacteristic) {
  return c.characteristicType === 'attribute' ? 'count' : 'measured'
}
// 从计划特性构造一行：特性码/名/单位/公差/类别全部来自计划（不手输），超差用计划公差判定。
function rowFromCharacteristic(c: PlanCharacteristic): DraftRow {
  return {
    id: nextRowId++,
    ...createQualityCharacteristicDraft(kindOfCharacteristic(c)),
    characteristicCode: c.characteristicCode ?? '',
    name: c.name ?? c.characteristicCode ?? '',
    uomCode: c.unitCode ?? '',
    lowerSpecLimit: c.lowerSpecLimit ?? '',
    upperSpecLimit: c.upperSpecLimit ?? '',
  }
}
function addCharacteristic(c: PlanCharacteristic) {
  const code = c.characteristicCode ?? ''
  if (!code || rows.some((r) => r.characteristicCode === code)) return
  rows.push(rowFromCharacteristic(c))
}
function addAllCharacteristics() {
  for (const c of planCharacteristics.value) addCharacteristic(c)
}

const allValid = computed(() => qualityCharacteristicRowsValid(rows))
const overallVerdict = computed(() => qualityInspectionOverallVerdict(rows))

// 处置原因：结果不合格时后端必填（`InspectionRecord` 领域校验）；合格时不需要。
const dispositionReason = ref('')
const dispositionRequired = computed(() => overallVerdict.value === 'fail')
const canSubmit = computed(
  () => allValid.value && (!dispositionRequired.value || dispositionReason.value.trim() !== ''),
)

// StepFlow 进度：由实时状态派生（选任务 → 录到至少一条有效结果 → 提交成功）。
const liveCtx = computed<QualityInspectionTaskCtx>(() => ({
  taskId: selectedTask.value?.inspectionTaskId,
  hasResults: rows.length > 0 && allValid.value,
  submitted: result.value?.phase === 'success',
}))
const progress = computed(() => qualityInspectionTaskFlow.progress(liveCtx.value))

function selectTask(task: Task) {
  selectedTask.value = task
  resetRows()
  // 特性由计划驱动，随计划特性加载后再选；不预置空行。
}
function backToList() {
  selectedTask.value = null
  resetRows()
}

// --- 检验特性选择器（可选可搜，数据源=计划特性，排除已添加）------------------------
const charPicker = reactive<{ open: boolean }>({ open: false })
const charSearch = ref('')
const availableCharacteristics = computed<PlanCharacteristic[]>(() => {
  const added = new Set(rows.map((r) => r.characteristicCode))
  const kw = charSearch.value.trim().toLowerCase()
  return planCharacteristics.value.filter((c) => {
    const code = c.characteristicCode ?? ''
    if (!code || added.has(code)) return false
    if (!kw) return true
    return (c.name ?? '').toLowerCase().includes(kw) || code.toLowerCase().includes(kw)
  })
})
function openCharPicker() {
  charSearch.value = ''
  charPicker.open = true
}
function pickCharacteristic(c: PlanCharacteristic) {
  addCharacteristic(c)
  charPicker.open = false
}

// 计量特性的规格范围展示（来自计划，只读）。
function specRangeText(row: DraftRow) {
  const lo = row.lowerSpecLimit === '' ? null : row.lowerSpecLimit
  const hi = row.upperSpecLimit === '' ? null : row.upperSpecLimit
  const unit = row.uomCode ? ` ${row.uomCode}` : ''
  if (lo === null && hi === null) return `不限${unit}`
  return `${lo ?? '-∞'} ~ ${hi ?? '+∞'}${unit}`
}

// --- 数字键盘（计量测量值 / 计数不良数共用一个实例）-------------------------------
const keyboard = reactive<{
  show: boolean
  rowId: number | null
  field: 'measuredValue' | 'defectQuantity'
}>({ show: false, rowId: null, field: 'measuredValue' })

const keyboardValue = computed<string>({
  get: () => {
    const row = rows.find((r) => r.id === keyboard.rowId)
    return row ? String(row[keyboard.field] ?? '') : ''
  },
  set: (value) => {
    const row = rows.find((r) => r.id === keyboard.rowId)
    if (row) row[keyboard.field] = value
  },
})
// 测量值允许小数点，不良数为整数（隐藏小数键）。
const keyboardExtraKey = computed(() => (keyboard.field === 'measuredValue' ? '.' : ''))

function openKeyboard(rowId: number, field: 'measuredValue' | 'defectQuantity') {
  keyboard.rowId = rowId
  keyboard.field = field
  keyboard.show = true
}

// --- 原因码 Picker（计数特性判不合格时）-----------------------------------------
const reasonOptions = computed<PickerOption[]>(() =>
  reasonCodes.value
    .map((r) => ({ label: r.reasonName ?? r.reasonCode ?? '', value: r.reasonCode ?? '' }))
    .filter((o) => o.value !== ''),
)
const reasonPicker = reactive<{ open: boolean; rowId: number | null }>({
  open: false,
  rowId: null,
})
const reasonPickerValue = computed<string>({
  get: () => {
    const row = rows.find((r) => r.id === reasonPicker.rowId)
    return row?.defectReason ?? ''
  },
  set: (value) => {
    const row = rows.find((r) => r.id === reasonPicker.rowId)
    if (row) row.defectReason = value
  },
})
function openReasonPicker(rowId: number) {
  reasonPicker.rowId = rowId
  reasonPicker.open = true
}
function reasonLabel(code: string) {
  return reasonOptions.value.find((o) => o.value === code)?.label ?? code
}

function setCountResult(row: DraftRow, value: 'pass' | 'fail') {
  row.countResult = value
  // 由不合格改回合格时清空原因码/不良数，避免残留脏值提交。
  if (value === 'pass') {
    row.defectReason = ''
    row.defectQuantity = ''
  }
}

// --- 提交 -----------------------------------------------------------------------
async function submit() {
  const task = selectedTask.value
  if (!task?.inspectionTaskId || !canSubmit.value || submitPending.value) return
  keyboard.show = false
  const lines = toQualityCharacteristicResultLines(rows)
  const verdict = overallVerdict.value
  try {
    await submitInspection(task.inspectionTaskId, lines, dispositionReason.value)
    result.value = { phase: 'success', verdict }
  } catch (e) {
    // 端点按任务生命周期天然幂等（重试返回同一记录），故失败可安全重试。
    result.value = {
      phase: 'error',
      message: e instanceof Error ? e.message : '提交失败，请检查网络后重试。',
    }
  }
}

function nextTask() {
  // 成功后回到列表继续下一个（列表已因提交失效自动刷新，已检任务回落）。
  result.value = null
  backToList()
}
function goBack() {
  router.push('/').catch(() => {})
}

// --- 展示辅助 -------------------------------------------------------------------
function dueText(iso?: string) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString('zh-CN')
}
function taskTitle(task: Task) {
  const sku = task.skuCode ?? '未知物料'
  return `${inspectionTaskSourceTypeLabel(task.sourceType)} · ${sku}`
}
function taskSubtitle(task: Task) {
  const parts: string[] = []
  if (task.sourceDocumentId) parts.push(`来源单 ${task.sourceDocumentId}`)
  if (task.quantity != null) parts.push(`数量 ${task.quantity}${task.uomCode ?? ''}`)
  if (task.batchNo) parts.push(`批次 ${task.batchNo}`)
  return parts.join(' · ')
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <button
          v-if="!inListStep && !result"
          type="button"
          aria-label="返回列表"
          class="text-sm text-muted-foreground"
          @click="backToList"
        >
          返回
        </button>
        <h1 class="text-lg font-semibold text-foreground">检验任务</h1>
        <span v-if="!result" class="ml-auto text-xs text-muted-foreground">
          第 {{ Math.min(progress.completed + 1, progress.total) }}/{{ progress.total }} 步
        </span>
      </div>
    </template>

    <!-- 结果页：合格（绿）/ 不合格（触发 NCR）/ 提交失败（可重试）-->
    <template v-if="result">
      <NvMobileResult
        v-if="result.phase === 'success' && result.verdict === 'pass'"
        status="success"
        title="检验合格"
        description="检验结果已记录。"
      >
        <template #actions>
          <button
            type="button"
            data-testid="next-task"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
            @click="nextTask"
          >
            下一个任务
          </button>
          <button
            type="button"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
            @click="goBack"
          >
            返回工作台
          </button>
        </template>
      </NvMobileResult>

      <NvMobileResult
        v-else-if="result.phase === 'success'"
        status="error"
        title="检验不合格"
        description="系统已按检验计划自动发起 NCR 处置，请在不合格处置流程中跟进。"
      >
        <template #actions>
          <button
            type="button"
            data-testid="next-task"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
            @click="nextTask"
          >
            下一个任务
          </button>
          <button
            type="button"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
            @click="goBack"
          >
            返回工作台
          </button>
        </template>
      </NvMobileResult>

      <NvMobileResult v-else status="error" title="提交失败" :description="result.message">
        <template #actions>
          <button
            type="button"
            data-testid="retry-submit"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
            @click="
              () => {
                result = null
                void submit()
              }
            "
          >
            重试
          </button>
          <button
            type="button"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
            @click="result = null"
          >
            返回
          </button>
        </template>
      </NvMobileResult>
    </template>

    <!-- 步骤 1：待检任务列表 -->
    <div v-else-if="inListStep" class="space-y-4 p-4">
      <NvScanBar placeholder="扫来源单据 / SKU 直达" :active="inListStep" @scan="onScan" />

      <div
        v-if="scanKeyword"
        class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-2 text-sm"
      >
        <span class="truncate text-foreground">筛选：{{ scanKeyword }}</span>
        <button
          data-testid="clear-scan"
          type="button"
          class="ml-3 shrink-0 rounded-md border border-border px-3 py-1 text-sm text-foreground"
          @click="clearScan"
        >
          清除
        </button>
      </div>

      <!-- 来源类型 chips -->
      <div class="flex flex-wrap gap-2">
        <button
          type="button"
          data-testid="chip-all"
          class="min-h-touch rounded-full border px-4 text-sm font-medium"
          :class="
            sourceTypeFilter === null
              ? 'border-brand bg-brand/10 text-foreground'
              : 'border-border bg-card text-muted-foreground'
          "
          @click="pickSourceType(null)"
        >
          全部
        </button>
        <button
          v-for="chip in sourceChips"
          :key="chip.type"
          type="button"
          :data-testid="`chip-${chip.type}`"
          class="min-h-touch rounded-full border px-4 text-sm font-medium"
          :class="
            sourceTypeFilter === chip.type
              ? 'border-brand bg-brand/10 text-foreground'
              : 'border-border bg-card text-muted-foreground'
          "
          @click="pickSourceType(chip.type)"
        >
          {{ chip.label }} {{ chip.count }}
        </button>
      </div>

      <RetryableListError
        v-if="error"
        :error="error"
        :pending="pending"
        fallback="待检任务加载失败，请稍后重试。"
        test-id="tasks-error"
        @retry="() => refresh()"
      />

      <div v-else-if="pending" class="px-4 py-6 text-center text-sm text-muted-foreground">
        加载中…
      </div>

      <div
        v-else-if="tasks.length === 0"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无待检任务
      </div>

      <!-- 客户端筛选（扫码/来源）在已加载页内无命中，但服务端仍有更多页：诚实提示 + 加载更多。 -->
      <div
        v-else-if="displayTasks.length === 0 && hasMore"
        data-testid="tasks-partial-no-match"
        class="space-y-3 rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        <p>在已加载的 {{ loaded }} 条待检任务中未匹配（共 {{ total }} 条）。</p>
        <button
          data-testid="load-more"
          type="button"
          :disabled="pending"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground disabled:opacity-60"
          @click="loadMore"
        >
          加载更多
        </button>
      </div>

      <div
        v-else-if="displayTasks.length === 0"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        未找到匹配的待检任务
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          v-for="task in displayTasks"
          :key="task.inspectionTaskId"
          data-testid="task-row"
          :title="taskTitle(task)"
          :subtitle="taskSubtitle(task)"
          @select="selectTask(task)"
        >
          <template #trailing>
            <div class="flex shrink-0 flex-col items-end gap-1">
              <NvMobileTag
                v-if="isOverdue(task)"
                :data-testid="`overdue-${task.inspectionTaskId}`"
                variant="danger"
              >
                超期
              </NvMobileTag>
              <span v-if="task.dueAtUtc" class="text-xs text-muted-foreground">
                {{ dueText(task.dueAtUtc) }}
              </span>
            </div>
          </template>
        </NvListRow>
      </div>
    </div>

    <!-- 步骤 2：执行（任务上下文常显 + 逐特性录结果）-->
    <div v-else class="space-y-4 p-4">
      <!-- 任务上下文（常显，防错检）-->
      <section
        v-if="selectedTask"
        class="space-y-1 rounded-lg border border-border bg-card p-4"
        data-testid="task-context"
      >
        <div class="flex items-center gap-2">
          <NvMobileTag variant="default">
            {{ inspectionTaskSourceTypeLabel(selectedTask.sourceType) }}
          </NvMobileTag>
          <NvMobileTag v-if="isOverdue(selectedTask)" variant="danger">超期</NvMobileTag>
        </div>
        <p class="text-base font-semibold text-foreground">
          {{ selectedTask.skuCode ?? '未知物料' }}
        </p>
        <dl class="space-y-1 text-sm">
          <div v-if="selectedTask.sourceDocumentId" class="flex items-baseline justify-between gap-4">
            <dt class="shrink-0 whitespace-nowrap text-muted-foreground">来源单据</dt>
            <dd class="min-w-0 truncate text-right text-foreground">
              {{ selectedTask.sourceDocumentId }}
            </dd>
          </div>
          <div v-if="selectedTask.quantity != null" class="flex items-baseline justify-between gap-4">
            <dt class="shrink-0 whitespace-nowrap text-muted-foreground">数量</dt>
            <dd class="min-w-0 truncate text-right text-foreground">
              {{ selectedTask.quantity }}{{ selectedTask.uomCode ?? '' }}
            </dd>
          </div>
          <div v-if="planLabel" class="flex items-baseline justify-between gap-4">
            <dt class="shrink-0 whitespace-nowrap text-muted-foreground">检验计划</dt>
            <dd class="min-w-0 truncate text-right text-foreground">{{ planLabel }}</dd>
          </div>
          <div v-if="selectedTask.batchNo" class="flex items-baseline justify-between gap-4">
            <dt class="shrink-0 whitespace-nowrap text-muted-foreground">批次</dt>
            <dd class="min-w-0 truncate text-right text-foreground">{{ selectedTask.batchNo }}</dd>
          </div>
          <div v-if="selectedTask.serialNo" class="flex items-baseline justify-between gap-4">
            <dt class="shrink-0 whitespace-nowrap text-muted-foreground">序列号</dt>
            <dd class="min-w-0 truncate text-right text-foreground">{{ selectedTask.serialNo }}</dd>
          </div>
          <div v-if="selectedTask.dueAtUtc" class="flex items-baseline justify-between gap-4">
            <dt class="shrink-0 whitespace-nowrap text-muted-foreground">应检至</dt>
            <dd
              class="min-w-0 truncate text-right text-foreground"
              :class="isOverdue(selectedTask) ? 'text-destructive' : undefined"
            >
              {{ dueText(selectedTask.dueAtUtc) }}
            </dd>
          </div>
        </dl>
      </section>

      <!-- 特性结果行 -->
      <section class="space-y-3">
        <h2 class="text-sm font-medium text-muted-foreground">逐特性录结果</h2>

        <div
          v-for="row in rows"
          :key="row.id"
          data-testid="char-row"
          class="space-y-3 rounded-lg border p-3"
          :class="
            characteristicRowOutOfTolerance(row)
              ? 'border-destructive/60 bg-destructive/10'
              : 'border-border bg-card'
          "
        >
          <div class="flex items-center justify-between gap-2">
            <div class="flex min-w-0 items-center gap-2">
              <NvMobileTag :variant="row.kind === 'measured' ? 'default' : 'warning'">
                {{ row.kind === 'measured' ? '计量' : '计数' }}
              </NvMobileTag>
              <span data-testid="char-name" class="truncate text-base font-medium text-foreground">
                {{ row.name || row.characteristicCode }}
              </span>
            </div>
            <button
              type="button"
              data-testid="remove-char"
              class="shrink-0 text-sm text-muted-foreground"
              @click="removeRow(row.id)"
            >
              移除
            </button>
          </div>

          <!-- 计量特性：测量值（数字键盘）；单位/规格公差来自计划（只读）→ 超差即时红警示 -->
          <template v-if="row.kind === 'measured'">
            <label class="block space-y-1">
              <span class="text-xs text-muted-foreground">测量值{{ row.uomCode ? `（${row.uomCode}）` : '' }}</span>
              <button
                type="button"
                data-testid="measured-value"
                class="min-h-touch flex w-full items-center rounded-lg border bg-background px-3 text-base text-foreground"
                :class="
                  characteristicRowOutOfTolerance(row) ? 'border-destructive' : 'border-border'
                "
                @click="openKeyboard(row.id, 'measuredValue')"
              >
                {{ row.measuredValue === '' ? '点击录入' : row.measuredValue }}
              </button>
            </label>
            <div class="flex items-center justify-between text-xs text-muted-foreground">
              <span>规格公差</span>
              <span data-testid="spec-range" class="text-foreground">{{ specRangeText(row) }}</span>
            </div>
            <p
              v-if="characteristicRowOutOfTolerance(row)"
              data-testid="out-of-tolerance"
              class="text-sm font-medium text-destructive"
            >
              超差：测量值越出规格公差
            </p>
          </template>

          <!-- 计数特性：合格 / 不合格 + 原因码 Picker + 不良数 -->
          <template v-else>
            <div class="grid grid-cols-2 gap-2">
              <button
                type="button"
                data-testid="count-pass"
                class="min-h-touch rounded-lg border text-base font-medium"
                :class="
                  row.countResult === 'pass'
                    ? 'border-success bg-success/10 text-foreground'
                    : 'border-border bg-card text-foreground'
                "
                @click="setCountResult(row, 'pass')"
              >
                合格
              </button>
              <button
                type="button"
                data-testid="count-fail"
                class="min-h-touch rounded-lg border text-base font-medium"
                :class="
                  row.countResult === 'fail'
                    ? 'border-destructive bg-destructive/10 text-destructive'
                    : 'border-border bg-card text-foreground'
                "
                @click="setCountResult(row, 'fail')"
              >
                不合格
              </button>
            </div>
            <template v-if="row.countResult === 'fail'">
              <label class="block space-y-1">
                <span class="text-xs text-muted-foreground">原因码</span>
                <button
                  type="button"
                  data-testid="pick-reason"
                  class="min-h-touch flex w-full items-center rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  @click="openReasonPicker(row.id)"
                >
                  {{ row.defectReason ? reasonLabel(row.defectReason) : '选择原因码' }}
                </button>
              </label>
              <label class="block space-y-1">
                <span class="text-xs text-muted-foreground">不良数（可选）</span>
                <button
                  type="button"
                  data-testid="defect-qty"
                  class="min-h-touch flex w-full items-center rounded-lg border border-border bg-background px-3 text-base text-foreground"
                  @click="openKeyboard(row.id, 'defectQuantity')"
                >
                  {{ row.defectQuantity === '' ? '点击录入' : row.defectQuantity }}
                </button>
              </label>
            </template>
          </template>
        </div>

        <div
          v-if="planCharacteristicsPending"
          class="px-4 py-4 text-center text-sm text-muted-foreground"
        >
          加载检验计划特性中…
        </div>
        <div
          v-else-if="planCharacteristics.length === 0"
          data-testid="no-plan-characteristics"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-6 text-center text-sm text-muted-foreground"
        >
          该检验计划未配置检验特性。
        </div>
        <template v-else>
          <div class="grid grid-cols-2 gap-2">
            <NvMobileButton
              variant="primary"
              data-testid="add-characteristic"
              :disabled="availableCharacteristics.length === 0"
              @click="openCharPicker"
            >
              + 添加检验特性
            </NvMobileButton>
            <NvMobileButton
              variant="outline"
              data-testid="add-all-characteristics"
              :disabled="availableCharacteristics.length === 0"
              @click="addAllCharacteristics"
            >
              全部添加
            </NvMobileButton>
          </div>
          <p v-if="rows.length === 0" class="text-sm text-muted-foreground">
            从检验计划中选择检验特性并录入结果。
          </p>
        </template>
      </section>

      <!-- 处置原因（判不合格时必填，供后端记录处置 / 触发 NCR）-->
      <label v-if="dispositionRequired && rows.length > 0" class="block space-y-1">
        <span class="text-sm font-medium text-destructive">处置原因（不合格必填）</span>
        <NvMobileInput
          v-model="dispositionReason"
          data-testid="disposition-reason"
          placeholder="如 外径超差且外观不良，判退"
        />
      </label>

      <button
        type="button"
        data-testid="submit"
        :disabled="!canSubmit || submitPending"
        class="min-h-touch w-full rounded-lg text-base font-medium text-primary-foreground disabled:opacity-60"
        :class="overallVerdict === 'fail' ? 'bg-destructive' : 'bg-primary'"
        @click="submit"
      >
        {{
          submitPending
            ? '提交中…'
            : overallVerdict === 'fail'
              ? '提交（判不合格）'
              : '提交检验结果'
        }}
      </button>
    </div>

    <!-- 数字键盘（计量测量值 / 计数不良数共用）-->
    <NvNumberKeyboard
      v-model="keyboardValue"
      v-model:show="keyboard.show"
      title="录入数值"
      :extra-key="keyboardExtraKey"
    />

    <!-- 原因码 Picker -->
    <NvPicker v-model="reasonPickerValue" v-model:open="reasonPicker.open" :options="reasonOptions" title="选择原因码" />

    <!-- 检验特性选择器（可选可搜，来自检验计划）-->
    <NvBottomSheet :open="charPicker.open" title="选择检验特性" @update:open="charPicker.open = $event">
      <div class="space-y-3 pb-2">
        <NvSearchBar v-model="charSearch" placeholder="搜索特性名 / 编码" />
        <div
          v-if="availableCharacteristics.length === 0"
          class="px-4 py-6 text-center text-sm text-muted-foreground"
        >
          无匹配的检验特性
        </div>
        <div v-else class="max-h-[50vh] overflow-y-auto rounded-lg border border-border">
          <button
            v-for="c in availableCharacteristics"
            :key="c.characteristicCode"
            type="button"
            data-testid="char-option"
            class="flex min-h-touch w-full items-center justify-between gap-3 border-b border-border px-4 py-3 text-left last:border-b-0 active:bg-accent"
            @click="pickCharacteristic(c)"
          >
            <span class="min-w-0">
              <span class="block truncate text-base font-medium text-foreground">
                {{ c.name || c.characteristicCode }}
              </span>
              <span class="block truncate text-xs text-muted-foreground">
                {{ c.characteristicCode }} ·
                {{ c.characteristicType === 'attribute' ? '计数' : '计量' }}
                <template v-if="c.unitCode">· {{ c.unitCode }}</template>
              </span>
            </span>
            <NvMobileTag :variant="c.characteristicType === 'attribute' ? 'warning' : 'default'">
              {{ c.characteristicType === 'attribute' ? '计数' : '计量' }}
            </NvMobileTag>
          </button>
        </div>
      </div>
    </NvBottomSheet>
  </NvAppShellMobile>
</template>
