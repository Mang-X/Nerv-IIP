<script setup lang="ts">
import { statusActionGate } from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import type { DispatchAssignTarget } from '@/components/mes/DispatchAssignDialog.vue'
import DispatchAssignDialog from '@/components/mes/DispatchAssignDialog.vue'
import { recoverLifecycleAction, useLifecycleWriteIntent } from '@/composables/lifecycleAction'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import {
  describeMaterialShortageStage,
  MATERIAL_READINESS_SCOPE_NOTE,
} from '@/composables/mes/materialReadinessScope'
import type { MesLifecycleActionKey } from '@/composables/mes/useMesTaskSemantics'
import {
  resolveDispatchAffordance,
  resolveDispatchState,
  resolveExecutionState,
  resolveLifecycleActions,
  resolveScheduleState,
} from '@/composables/mes/useMesTaskSemantics'
import {
  describeMesReadinessReason,
  useMesDispatchTasks,
  useMesOperationTasks,
  useMesWorkOrderDetail,
} from '@/composables/useBusinessMes'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvRowActions,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  Spinner,
} from '@nerv-iip/ui'
import {
  CheckCheckIcon,
  ClipboardCheckIcon,
  ExternalLinkIcon,
  PauseIcon,
  PlayIcon,
  RotateCwIcon,
  UserCheckIcon,
} from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { RouterLink } from 'vue-router'

/**
 * 工单行内抽屉：从工单列表的一行就地下钻，看完这张工单的**全部要点并直接动手**——
 * 状态与阻塞、每道工序派了谁排到哪、用料齐不齐，以及派工 / 开工 / 完工 / 报工。
 *
 * 之前这些能力散在四个页面（详情页、齐套页、工序页、报工），行操作里除报工外全是跳页，
 * 看一张工单要来回跳三次、每次都丢上下文。重内容（取消工单的补偿预览、追溯图）
 * 仍留在完整详情页，抽屉不复制它们、也不在这里伪造一份。
 */

const workOrderId = defineModel<string | null>('workOrderId', { default: null })
const lifecycleIntent = useLifecycleWriteIntent<MesLifecycleActionKey>(
  (taskId, action) =>
    `op-${action}-${taskId}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
)
usePendingWriteLeaveGuard(lifecycleIntent.locked)

const emit = defineEmits<{ report: [operationTaskId: string] }>()

const { detail, detailError, detailPending, filters, materialReadiness, refreshDetail } =
  useMesWorkOrderDetail()
const { assignDispatchTask, assignDispatchTaskPending } = useMesDispatchTasks()
const {
  completeOperationTask,
  filters: operationFilters,
  operationScopeMessage,
  operationScopeReady,
  pauseOperationTask,
  resumeOperationTask,
  startOperationTask,
} = useMesOperationTasks()
const { resolveShiftLabel, resolveSkuLabel, resolveWorkCenter } = useMesDisplayNames({
  shifts: true,
})

watch(
  workOrderId,
  (id) => {
    filters.workOrderId = id ?? ''
  },
  { immediate: true },
)

const open = computed({
  get: () => Boolean(workOrderId.value),
  set: (value) => {
    if (!value && !lifecycleIntent.locked.value) workOrderId.value = null
  },
})

const errorMessage = computed(() => inlineErrorMessage(detailError.value))

const operations = computed(() =>
  [...(detail.value?.operationTasks ?? [])].sort(
    (a, b) => (a.operationSequence ?? 0) - (b.operationSequence ?? 0),
  ),
)
type OperationRow = (typeof operations)['value'][number]

const blockingReasons = computed(() =>
  (detail.value?.blockingReasons ?? []).map(describeMesReadinessReason),
)

// 缺口环节（#1291）随行预计算：每行只算一次，模板里直接读，不在单元格里反复调用。
const materialRows = computed(() =>
  (materialReadiness.value?.items ?? []).map((row) => ({
    ...row,
    stage: describeMaterialShortageStage(row),
  })),
)
const materialShortages = computed(() =>
  materialRows.value.filter((row) => (row.shortageQuantity ?? 0) > 0),
)

const operationColumns: NvDataTableColumn<OperationRow>[] = [
  {
    key: 'operationSequence',
    header: '工序',
    width: 'w-20',
    cellClass: 'font-medium',
    accessor: (r) => (r.operationSequence != null ? `第 ${r.operationSequence} 道` : '—'),
  },
  { key: 'status', header: '执行状态', width: 'w-24' },
  { key: 'assignedUserName', header: '派工', width: 'w-36' },
  { key: 'scheduleState', header: '排程', width: 'w-36' },
  {
    key: 'workCenterId',
    header: '工作中心',
    accessor: (r) =>
      r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '—',
  },
  { key: 'shiftId', header: '班次', width: 'w-24', accessor: (r) => resolveShiftLabel(r.shiftId) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const materialColumns: NvDataTableColumn<(typeof materialRows)['value'][number]>[] = [
  {
    key: 'materialId',
    header: '物料',
    cellClass: 'font-medium',
    accessor: (r) => resolveSkuLabel(r.materialId),
  },
  { key: 'requiredQuantity', header: '需求', align: 'end', width: 'w-20' },
  { key: 'availableQuantity', header: '线边可用', align: 'end', width: 'w-24' },
  { key: 'shortageQuantity', header: '缺口', align: 'end', width: 'w-20' },
  // 缺口卡在哪个环节 + 下一步动作：只给数字讲不清「为什么 MRP 说有货、这里说缺」。
  { key: 'shortageStage', header: '缺在哪个环节', width: 'w-56' },
]

// ── 工序动作 ────────────────────────────────────────────────────
const lifecyclePending = ref<string | null>(null)
const LIFECYCLE_RUNNERS: Record<
  MesLifecycleActionKey,
  (
    id: string,
    context: { organizationId: string; environmentId: string; workOrderId?: string },
    body: { idempotencyKey: string },
  ) => Promise<unknown>
> = {
  start: startOperationTask,
  pause: pauseOperationTask,
  resume: resumeOperationTask,
  complete: completeOperationTask,
}
const LIFECYCLE_DONE_MESSAGES: Record<MesLifecycleActionKey, string> = {
  start: '已开工。',
  pause: '已暂停，恢复后可继续加工。',
  resume: '已恢复加工。',
  complete: '该工序已完工。',
}
const LIFECYCLE_FAIL_ACTIONS: Record<MesLifecycleActionKey, string> = {
  start: '开工失败',
  pause: '暂停失败',
  resume: '恢复加工失败',
  complete: '完工失败',
}
const LIFECYCLE_FAIL_FALLBACKS: Record<MesLifecycleActionKey, string> = {
  start: '开工失败，请稍后重试。',
  pause: '暂停失败，请稍后重试。',
  resume: '恢复加工失败，请稍后重试。',
  complete: '完工失败，请稍后重试。',
}
function lifecycleActionEnabled(task: OperationRow, action: MesLifecycleActionKey) {
  if (!operationScopeReady.value) return false
  if (task.operationTaskId && !lifecycleIntent.permits(task.operationTaskId, action)) return false
  return statusActionGate({
    domain: 'mes-operation-task',
    action,
    facts: { status: task.status },
  }).executable
}

async function runLifecycleAction(task: OperationRow, action: MesLifecycleActionKey) {
  if (!operationScopeReady.value) {
    notifyError(operationScopeMessage.value)
    return
  }
  const operationTaskId = task.operationTaskId
  if (!operationTaskId) return
  const intent = lifecycleIntent.acquire(operationTaskId, action)
  if (!intent) return
  lifecyclePending.value = operationTaskId
  try {
    await LIFECYCLE_RUNNERS[action](
      operationTaskId,
      {
        organizationId: operationFilters.organizationId,
        environmentId: operationFilters.environmentId,
        workOrderId: task.workOrderId ?? workOrderId.value ?? undefined,
      },
      {
        idempotencyKey: intent.key,
      },
    )
    notifySuccess(LIFECYCLE_DONE_MESSAGES[action])
    lifecycleIntent.clear()
    void refreshDetail()
  } catch (error) {
    if (
      await recoverLifecycleAction(error, {
        reset: () => {
          lifecyclePending.value = null
          lifecycleIntent.clear()
        },
        refresh: refreshDetail,
        notify: (message) => notifyError(message),
      })
    ) {
      return
    }
    lifecycleIntent.recordFailure(error)
    notifyOperationFailure(LIFECYCLE_FAIL_ACTIONS[action], error, LIFECYCLE_FAIL_FALLBACKS[action])
  } finally {
    lifecyclePending.value = null
  }
}

const assignOpen = ref(false)
const assignTarget = ref<DispatchAssignTarget | null>(null)
function openAssign(task: OperationRow) {
  if (!resolveDispatchAffordance(task).enabled) return
  assignTarget.value = { ...task, workOrderNo: workOrderId.value }
  assignOpen.value = true
}

function requestReport(task: OperationRow) {
  if (!task.operationTaskId) return
  emit('report', task.operationTaskId)
}
function canReport(task: OperationRow) {
  return ['ready', 'running'].includes(resolveExecutionState(task.status).key)
}

function closeSheet() {
  if (!lifecycleIntent.locked.value) workOrderId.value = null
}

function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}
</script>

<template>
  <NvSheet v-model:open="open">
    <NvSheetContent class="flex w-full flex-col gap-0 overflow-y-auto sm:max-w-3xl">
      <NvSheetHeader>
        <NvSheetTitle>{{ workOrderId ?? '工单' }}</NvSheetTitle>
        <NvSheetDescription>
          这张工单的状态、工序进度与用料齐套；派工、开工与报工可以直接在这里完成。
        </NvSheetDescription>
      </NvSheetHeader>

      <div class="grid content-start gap-4 px-4 pb-4">
        <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
        <p
          v-else-if="detailPending && !detail"
          class="flex items-center gap-2 rounded-lg border border-dashed p-4 text-sm text-muted-foreground"
          role="status"
        >
          <Spinner aria-hidden="true" />
          正在加载工单…
        </p>

        <template v-else-if="detail">
          <!-- 概要 -->
          <section class="grid gap-3 rounded-lg border p-3 sm:grid-cols-4">
            <div class="grid gap-1">
              <span class="text-xs text-muted-foreground">状态</span>
              <NvStatusBadge class="justify-self-start" :value="detail.status" />
            </div>
            <div class="grid gap-1">
              <span class="text-xs text-muted-foreground">开工就绪</span>
              <NvStatusBadge class="justify-self-start" :value="detail.readinessStatus" />
            </div>
            <div class="grid gap-1">
              <span class="text-xs text-muted-foreground">计划数量</span>
              <span class="text-sm font-medium tabular-nums">{{
                formatQuantity(detail.quantity)
              }}</span>
            </div>
            <div class="grid gap-1">
              <span class="text-xs text-muted-foreground">物料</span>
              <span class="text-sm font-medium">{{ resolveSkuLabel(detail.skuId) }}</span>
            </div>
          </section>

          <!-- 阻塞（有就先显，最要紧） -->
          <section
            v-if="blockingReasons.length"
            class="grid gap-1.5 rounded-lg border border-warning/30 bg-warning/10 p-3"
          >
            <p class="text-sm font-medium text-foreground">
              {{ blockingReasons.length }} 项开工阻塞，需先处理
            </p>
            <p
              v-for="reason in blockingReasons"
              :key="reason.code"
              class="text-xs text-muted-foreground"
            >
              {{ reason.label }}{{ reason.detail ? `（${reason.detail}）` : '' }} ——
              {{ reason.nextStep }}
            </p>
          </section>

          <!-- 工序 -->
          <section class="grid gap-2">
            <h3 class="text-sm font-semibold text-foreground">工序（{{ operations.length }}）</h3>
            <p
              v-if="operationScopeMessage"
              data-testid="operation-scope-message"
              class="text-sm text-destructive"
              role="alert"
            >
              {{ operationScopeMessage }}
            </p>
            <NvDataTable
              :columns="operationColumns"
              :rows="operations"
              row-key="operationTaskId"
              :pagination="false"
              :searchable="false"
              :column-settings="false"
              density="compact"
              empty-message="该工单还没有工序。工单下达后会按工艺路线生成工序。"
            >
              <template #cell-status="{ row }">
                <NvStatusBadge
                  :label="resolveExecutionState(row.status).label"
                  :tone="resolveExecutionState(row.status).tone"
                />
              </template>
              <template #cell-assignedUserName="{ row }">
                <NvStatusBadge
                  :label="resolveDispatchState(row).label"
                  :tone="resolveDispatchState(row).tone"
                />
              </template>
              <template #cell-scheduleState="{ row }">
                <NvStatusBadge
                  :label="resolveScheduleState(row).label"
                  :tone="resolveScheduleState(row).tone"
                />
              </template>
              <template #cell-actions="{ row }">
                <NvRowActions :label="`工序操作 第 ${row.operationSequence ?? ''} 道`">
                  <NvDropdownMenuItem
                    v-for="action in resolveLifecycleActions(row)"
                    :key="action.key"
                    :disabled="
                      !action.enabled ||
                      !lifecycleActionEnabled(row, action.key) ||
                      lifecyclePending === row.operationTaskId
                    "
                    :title="action.blockedReason"
                    @click="runLifecycleAction(row, action.key)"
                  >
                    <PlayIcon v-if="action.key === 'start'" aria-hidden="true" />
                    <PauseIcon v-else-if="action.key === 'pause'" aria-hidden="true" />
                    <RotateCwIcon v-else-if="action.key === 'resume'" aria-hidden="true" />
                    <CheckCheckIcon v-else aria-hidden="true" />
                    {{ action.label }}
                  </NvDropdownMenuItem>
                  <NvDropdownMenuSeparator />
                  <NvDropdownMenuItem
                    :disabled="!resolveDispatchAffordance(row).enabled"
                    :title="resolveDispatchAffordance(row).blockedReason"
                    @click="openAssign(row)"
                  >
                    <UserCheckIcon aria-hidden="true" />
                    {{ resolveDispatchAffordance(row).label }}
                  </NvDropdownMenuItem>
                  <NvDropdownMenuItem :disabled="!canReport(row)" @click="requestReport(row)">
                    <ClipboardCheckIcon aria-hidden="true" />
                    报工
                  </NvDropdownMenuItem>
                </NvRowActions>
              </template>
            </NvDataTable>
          </section>

          <!-- 用料齐套 -->
          <section class="grid gap-2">
            <div class="flex items-center justify-between gap-3">
              <h3 class="text-sm font-semibold text-foreground">用料齐套</h3>
              <span v-if="materialShortages.length" class="text-xs text-warning">
                {{ materialShortages.length }} 项缺料
              </span>
            </div>
            <!-- 口径自解释：齐套 ≠ MRP 的全厂库存口径，必须写在表格旁边（#1291）。 -->
            <p class="text-xs text-muted-foreground" data-testid="material-readiness-scope">
              {{ MATERIAL_READINESS_SCOPE_NOTE }}
            </p>
            <NvDataTable
              :columns="materialColumns"
              :rows="materialRows"
              :row-key="(r) => `${r.materialId}-${r.materialLotId ?? ''}`"
              :pagination="false"
              :searchable="false"
              :column-settings="false"
              density="compact"
              empty-message="暂无用料需求记录。"
            >
              <template #cell-requiredQuantity="{ row }">
                <span class="tabular-nums">{{ formatQuantity(row.requiredQuantity) }}</span>
              </template>
              <template #cell-availableQuantity="{ row }">
                <span class="tabular-nums">{{ formatQuantity(row.availableQuantity) }}</span>
              </template>
              <template #cell-shortageQuantity="{ row }">
                <span
                  class="tabular-nums"
                  :class="(row.shortageQuantity ?? 0) > 0 ? 'font-medium text-warning' : undefined"
                >
                  {{ formatQuantity(row.shortageQuantity) }}
                </span>
              </template>
              <template #cell-shortageStage="{ row }">
                <div class="grid gap-0.5">
                  <NvStatusBadge
                    class="justify-self-start"
                    :value="row.stage.label"
                    :label="row.stage.label"
                    :tone="row.stage.tone"
                  />
                  <span class="text-xs text-muted-foreground">{{ row.stage.nextAction }}</span>
                </div>
              </template>
            </NvDataTable>
          </section>
        </template>
      </div>

      <NvSheetFooter class="mt-auto flex-row flex-wrap gap-2">
        <NvButton v-if="workOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(workOrderId)}`">
            <ExternalLinkIcon aria-hidden="true" />
            打开完整详情
          </RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="ghost"
          :disabled="lifecycleIntent.locked.value"
          @click="closeSheet"
          >关闭</NvButton
        >
      </NvSheetFooter>
    </NvSheetContent>

    <DispatchAssignDialog
      v-model:open="assignOpen"
      :target="assignTarget"
      :assign="
        (operationTaskId, body) =>
          assignDispatchTask(operationTaskId, {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            ...body,
          })
      "
      :pending="assignDispatchTaskPending"
      @assigned="refreshDetail"
    />
  </NvSheet>
</template>
