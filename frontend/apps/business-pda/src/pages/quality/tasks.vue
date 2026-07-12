<script setup lang="ts">
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import {
  characteristicRowOutOfTolerance,
  createQualityCharacteristicDraft,
  inspectionTaskSourceTypeLabel,
  INSPECTION_TASK_SOURCE_TYPES,
  qualityCharacteristicRowsValid,
  qualityInspectionOverallVerdict,
  qualityInspectionTaskFlow,
  toQualityCharacteristicResultLines,
  type CharacteristicResultKind,
  type QualityCharacteristicDraftRow,
  type QualityInspectionTaskCtx,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvListRow,
  NvMobileButton,
  NvMobileInput,
  NvMobileResult,
  NvMobileTag,
  NvNumberKeyboard,
  NvPicker,
  NvScanBar,
  type PickerOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import RetryableListError from '@/components/RetryableListError.vue'
import { useBusinessQualityInspectionTasks } from '@/composables/useBusinessQualityInspectionTasks'

definePage({
  meta: {
    requiresAuth: true,
    title: '检验任务',
  },
})

type Task = BusinessConsoleQualityInspectionTaskItem
interface DraftRow extends QualityCharacteristicDraftRow {
  id: number
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

// --- 逐特性录结果 ---------------------------------------------------------------
let nextRowId = 1
const rows = reactive<DraftRow[]>([])

function resetRows() {
  rows.splice(0, rows.length)
  nextRowId = 1
}
function addRow(kind: CharacteristicResultKind) {
  rows.push({ id: nextRowId++, ...createQualityCharacteristicDraft(kind) })
}
function removeRow(id: number) {
  const index = rows.findIndex((r) => r.id === id)
  if (index >= 0) rows.splice(index, 1)
}

const allValid = computed(() => qualityCharacteristicRowsValid(rows))
const overallVerdict = computed(() => qualityInspectionOverallVerdict(rows))

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
  // 默认给一行计量特性，减少空态点击成本。
  addRow('measured')
}
function backToList() {
  selectedTask.value = null
  resetRows()
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
  if (!task?.inspectionTaskId || !allValid.value || submitPending.value) return
  keyboard.show = false
  const lines = toQualityCharacteristicResultLines(rows)
  const verdict = overallVerdict.value
  try {
    await submitInspection(task.inspectionTaskId, lines)
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
        <dl class="grid grid-cols-2 gap-x-3 gap-y-1 text-sm">
          <div v-if="selectedTask.sourceDocumentId" class="col-span-2 flex justify-between gap-3">
            <dt class="text-muted-foreground">来源单据</dt>
            <dd class="truncate text-foreground">{{ selectedTask.sourceDocumentId }}</dd>
          </div>
          <div v-if="selectedTask.quantity != null" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">数量</dt>
            <dd class="text-foreground">
              {{ selectedTask.quantity }}{{ selectedTask.uomCode ?? '' }}
            </dd>
          </div>
          <div v-if="selectedTask.inspectionPlanId" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">检验计划</dt>
            <dd class="truncate text-foreground">{{ selectedTask.inspectionPlanId }}</dd>
          </div>
          <div v-if="selectedTask.batchNo" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">批次</dt>
            <dd class="truncate text-foreground">{{ selectedTask.batchNo }}</dd>
          </div>
          <div v-if="selectedTask.serialNo" class="flex justify-between gap-3">
            <dt class="text-muted-foreground">序列号</dt>
            <dd class="truncate text-foreground">{{ selectedTask.serialNo }}</dd>
          </div>
          <div v-if="selectedTask.dueAtUtc" class="col-span-2 flex justify-between gap-3">
            <dt class="text-muted-foreground">应检至</dt>
            <dd
              class="text-foreground"
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
            <NvMobileTag :variant="row.kind === 'measured' ? 'default' : 'warning'">
              {{ row.kind === 'measured' ? '计量' : '计数' }}
            </NvMobileTag>
            <button
              type="button"
              data-testid="remove-char"
              class="text-sm text-muted-foreground"
              @click="removeRow(row.id)"
            >
              移除
            </button>
          </div>

          <label class="block space-y-1">
            <span class="text-xs text-muted-foreground">检验特性</span>
            <NvMobileInput
              v-model="row.characteristicCode"
              data-testid="char-code"
              placeholder="如 外径 / 外观"
            />
          </label>

          <!-- 计量特性：测量值（数字键盘）+ 单位 + 上下限 → 超差即时红警示 -->
          <template v-if="row.kind === 'measured'">
            <div class="grid grid-cols-2 gap-2">
              <label class="space-y-1">
                <span class="text-xs text-muted-foreground">测量值</span>
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
              <label class="space-y-1">
                <span class="text-xs text-muted-foreground">单位</span>
                <NvMobileInput v-model="row.uomCode" data-testid="uom" placeholder="如 mm" />
              </label>
            </div>
            <div class="grid grid-cols-2 gap-2">
              <label class="space-y-1">
                <span class="text-xs text-muted-foreground">下限</span>
                <NvMobileInput
                  v-model="row.lowerSpecLimit"
                  data-testid="lower"
                  type="number"
                  inputmode="decimal"
                  placeholder="可选"
                />
              </label>
              <label class="space-y-1">
                <span class="text-xs text-muted-foreground">上限</span>
                <NvMobileInput
                  v-model="row.upperSpecLimit"
                  data-testid="upper"
                  type="number"
                  inputmode="decimal"
                  placeholder="可选"
                />
              </label>
            </div>
            <p
              v-if="characteristicRowOutOfTolerance(row)"
              data-testid="out-of-tolerance"
              class="text-sm font-medium text-destructive"
            >
              超差：测量值越出规格限
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

        <div class="grid grid-cols-2 gap-2">
          <NvMobileButton variant="outline" data-testid="add-measured" @click="addRow('measured')">
            + 计量特性
          </NvMobileButton>
          <NvMobileButton variant="outline" data-testid="add-count" @click="addRow('count')">
            + 计数特性
          </NvMobileButton>
        </div>

        <p v-if="rows.length === 0" class="text-sm text-muted-foreground">
          请添加至少一条检验特性并录入结果。
        </p>
      </section>

      <button
        type="button"
        data-testid="submit"
        :disabled="!allValid || submitPending"
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
  </NvAppShellMobile>
</template>
