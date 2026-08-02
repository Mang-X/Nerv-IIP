<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { openDownloadGrantBlob } from '@nerv-iip/business-core'
import {
  createTimeoutFetch,
  describeRequestError,
  isIndeterminateError,
  REQUEST_TIMEOUT_MS,
} from '@/api/request-timeout'
import MesWorkScopeFilter from '@/components/mes/MesWorkScopeFilter.vue'
import TaskListShell from '@/components/task-list/TaskListShell.vue'
import {
  type OperationActionContext,
  useMesCurrentOperationSops,
  useMesOperationTasks,
} from '@/composables/useBusinessMes'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import {
  isLifecycleActionUpdated,
  LIFECYCLE_ACTION_UPDATED_MESSAGE,
} from '@/composables/lifecycleActionRecovery'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import {
  NvAppShellMobile,
  NvListRow,
  NvMobileButton,
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  NvMobileToast,
  NvScanBar,
  type DropdownOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import MesOperationExecutionPanel from './components/MesOperationExecutionPanel.vue'
import {
  actionsForOperationTask as actionsFor,
  OPERATION_SUCCESS_TITLES as SUCCESS_TITLES,
  operationTaskRowSubtitle as rowSubtitle,
  operationTaskRowTitle as rowTitle,
  taskDisplayReference,
  type OperationActionKind as ActionKind,
  type OperationResultState as ResultState,
} from './components/operationPresentation'

definePage({
  meta: {
    requiresAuth: true,
    title: '工序执行',
  },
})

type Task = BusinessConsoleMesOperationTaskRow
type CurrentSop = { fileId?: string | null; fileName?: string | null }

const {
  filters,
  operationTasks,
  total,
  loaded = computed(() => operationTasks.value.length),
  loadingMore = shallowRef(false),
  refreshing = shallowRef(false),
  loadMoreError = shallowRef<unknown>(),
  loadMore = () => Promise.resolve(),
  pending,
  error,
  startTask,
  pauseTask,
  resumeTask,
  completeTask,
  actionPending,
  operationListScope,
  operationListContextIdentity,
  operationActionContextIdentity,
  operationListScopeMessage,
  operationListScopeReady,
  operationScopeMessage,
  operationScopeReady,
  captureOperationActionContext,
  isOperationActionContextCurrent,
  refresh,
  lastUpdatedAt,
  hasSuccessfulResponse,
  hasFailedResponse,
} = useMesOperationTasks()
const route = useRoute()
const router = useRouter()

function queryString(value: unknown) {
  return typeof value === 'string' ? value.trim() : ''
}

const requestedWorkOrderId = computed(() => queryString(route.query.workOrderId))
const requestedOperationTaskId = computed(() => queryString(route.query.operationTaskId))
const hasCompleteTaskDeepLink = computed(() =>
  Boolean(requestedWorkOrderId.value && requestedOperationTaskId.value),
)
const hasAnyTaskDeepLink = computed(() =>
  Boolean(requestedWorkOrderId.value || requestedOperationTaskId.value),
)
const hasInvalidTaskDeepLink = computed(
  () => hasAnyTaskDeepLink.value && !hasCompleteTaskDeepLink.value,
)
const deepLinkIdentity = computed(
  () =>
    `${operationListContextIdentity.value}\u0000${operationActionContextIdentity.value}\u0000${requestedWorkOrderId.value}\u0000${requestedOperationTaskId.value}`,
)
const operationPageGeneration = ref(0)
const operationSelectionGeneration = shallowRef(0)
const operationSelectionIdentity = shallowRef('')
const operationPageIdentity = computed(
  () =>
    `${operationListContextIdentity.value}\u0000${operationActionContextIdentity.value}\u0000${requestedWorkOrderId.value}\u0000${requestedOperationTaskId.value}`,
)
const deepLinkMessage = ref('')

const visibleOperationTasks = computed(() =>
  hasInvalidTaskDeepLink.value
    ? []
    : hasCompleteTaskDeepLink.value
      ? operationTasks.value.filter(
          (task) =>
            task.workOrderId === requestedWorkOrderId.value &&
            task.operationTaskId === requestedOperationTaskId.value,
        )
      : operationTasks.value,
)
const workScopeKindLabels: Record<string, string> = {
  self: '本人',
  team: '班组',
  'work-center': '工作中心',
  workshop: '车间',
  organization: '组织',
}
const mesScope = computed(() => {
  const selectedScope = operationListScope.value
  if (!selectedScope) return '当前主体授权作业范围未就绪'
  const kind = workScopeKindLabels[selectedScope.kind] ?? selectedScope.kind
  const name = selectedScope.displayName || selectedScope.id
  return `当前主体授权作业范围 · ${name}（${kind}）`
})
const mesEmptyExplanation = computed(() =>
  operationListScopeReady.value
    ? '当前主体授权作业范围内暂无工序任务。'
    : operationListScopeMessage.value || '尚未取得当前主体的授权作业范围，未发起查询。',
)
const showOperationTasksEmpty = computed(
  () =>
    !pending.value &&
    !error.value &&
    !hasFailedResponse.value &&
    hasSuccessfulResponse.value &&
    operationTasks.value.length === 0,
)
const operationListError = computed(() =>
  error.value || hasFailedResponse.value
    ? (error.value ?? new Error('工序任务服务未成功返回'))
    : undefined,
)
const {
  filters: sopFilters,
  currentSops,
  pending: sopsPending,
  error: sopsError,
  refresh: refreshSops,
  createSopFileDownloadGrant,
} = useMesCurrentOperationSops()

// SOP 文件下载走 PDA 全局超时 fetch —— 弱网/离线有界失败，不无限挂起（#814）。
const downloadFetch = createTimeoutFetch()

const ACTION_FNS: Record<
  ActionKind,
  (
    workOrderId: string,
    operationTaskId: string,
    idempotencyKey: string,
    context: OperationActionContext,
  ) => Promise<unknown>
> = {
  start: (workOrderId, operationTaskId, idempotencyKey, context) =>
    startTask(workOrderId, operationTaskId, { idempotencyKey, context }),
  pause: (workOrderId, operationTaskId, idempotencyKey, context) =>
    pauseTask(workOrderId, operationTaskId, { idempotencyKey, context }),
  resume: (workOrderId, operationTaskId, idempotencyKey, context) =>
    resumeTask(workOrderId, operationTaskId, { idempotencyKey, context }),
  complete: (workOrderId, operationTaskId, idempotencyKey, context) =>
    completeTask(workOrderId, operationTaskId, { idempotencyKey, context }),
}

// 稳定的逐动作幂等键：用户发起某动作时铸造一次，重试该动作复用同键；
// 换动作或重新打开面板 → 新键。
const operationKey = ref('')
const operationContext = shallowRef<OperationActionContext | null>(null)
const operationResultUnknown = ref(false)
const operationResultContextConflict = shallowRef<'identity' | 'route' | null>(null)
usePendingWriteLeaveGuard(operationResultUnknown)

// --- BottomSheet 状态 ---
const selected = ref<Task | null>(null)
const sheetOpen = computed({
  get: () => selected.value !== null,
  set: (open) => {
    if (!open) closeSheet()
  },
})
// 完成是终态动作 → sheet 内二次确认
const confirmingComplete = ref(false)

// --- 结果反馈 ---
const result = ref<ResultState | null>(null)
const openingSopFileId = ref<string | null>(null)
const sopFileError = ref('')
const toast = reactive({ show: false, message: '', type: 'error' as const })

const availableActions = computed(() => actionsFor(selected.value))

const scanActive = computed(() => selected.value === null && result.value === null)
const statusOptions: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待开始', value: 'queued' },
  { label: '进行中', value: 'inProgress' },
  { label: '已暂停', value: 'paused' },
  { label: '已阻塞', value: 'blocked' },
  { label: '已完成', value: 'completed' },
]
const statusModel = computed<string | number>({
  get: () => filters.status ?? '',
  set: (value) => (filters.status = String(value) || undefined),
})
const taskFilterState = computed(() => ({
  status: filters.status ?? '',
  keyword: filters.keyword ?? '',
}))

function clearOperationResultIntent() {
  result.value = null
  operationKey.value = ''
  operationContext.value = null
  operationResultUnknown.value = false
  operationResultContextConflict.value = null
}

function shouldPreserveOperationContextConflict(
  routeWorkOrderId: string,
  routeOperationTaskId: string,
) {
  const state = result.value
  const conflict = operationResultContextConflict.value
  if (!conflict || !state) return false
  if (conflict === 'route') return true
  if (!routeWorkOrderId && !routeOperationTaskId) return true
  return state.workOrderId === routeWorkOrderId && state.taskId === routeOperationTaskId
}

function isOperationResultRouteCurrent(state: ResultState) {
  if (!hasAnyTaskDeepLink.value) return true
  if (!hasCompleteTaskDeepLink.value) return false
  return (
    requestedWorkOrderId.value === state.workOrderId &&
    requestedOperationTaskId.value === state.taskId &&
    requestedWorkOrderId.value === state.context.workOrderId &&
    requestedOperationTaskId.value === state.context.operationTaskId
  )
}

function restoreTaskListState(state: { filters: Record<string, unknown> }) {
  if (hasAnyTaskDeepLink.value) return
  const status = state.filters.status
  const keyword = state.filters.keyword
  filters.status = typeof status === 'string' && status ? status : undefined
  filters.keyword = typeof keyword === 'string' && keyword ? keyword : undefined
}

function openSheet(task: Task) {
  if (operationResultUnknown.value) return
  const selectionIdentity = `${task.workOrderId ?? ''}\u0000${task.operationTaskId ?? ''}`
  if (operationSelectionIdentity.value !== selectionIdentity) {
    operationSelectionIdentity.value = selectionIdentity
    operationSelectionGeneration.value += 1
  }
  clearOperationResultIntent()
  sopFileError.value = ''
  confirmingComplete.value = false
  selected.value = task
  sopFilters.operationCode = task.operationCode?.trim() ?? ''
  sopFilters.workCenterCode = (task.workCenterCode ?? task.workCenterId)?.trim() ?? ''
  sopFilters.routingCode = ''
  sopFilters.routingRevision = ''
  sopFilters.asOfDate = ''
}

const deepLinkOpenedIdentity = ref('')
watch(
  [
    requestedWorkOrderId,
    requestedOperationTaskId,
    operationListContextIdentity,
    operationActionContextIdentity,
  ],
  ([workOrderId, operationTaskId]) => {
    operationPageGeneration.value += 1
    closeSheet()
    if (
      !operationResultUnknown.value &&
      !shouldPreserveOperationContextConflict(workOrderId, operationTaskId)
    ) {
      clearOperationResultIntent()
    }
    deepLinkOpenedIdentity.value = ''
    deepLinkMessage.value = ''
    const completePair = Boolean(workOrderId && operationTaskId)
    filters.workOrderId = completePair ? workOrderId : undefined
    filters.operationTaskId = completePair ? operationTaskId : undefined
    filters.keyword = undefined
    if ((workOrderId || operationTaskId) && !completePair) {
      deepLinkMessage.value = '工序任务链接缺少工单或任务标识，无法安全打开。'
    }
  },
  { immediate: true },
)
watch(
  [
    visibleOperationTasks,
    pending,
    hasSuccessfulResponse,
    operationScopeReady,
    deepLinkIdentity,
    operationResultContextConflict,
  ],
  ([tasks, isPending, successful, manageScopeReady, identity, contextConflict]) => {
    if (!hasCompleteTaskDeepLink.value) return
    if (contextConflict) {
      deepLinkOpenedIdentity.value = identity
      return
    }
    if (deepLinkOpenedIdentity.value === identity || isPending || !successful || !manageScopeReady)
      return
    const exactTask = tasks[0]
    if (!exactTask) {
      deepLinkMessage.value = '未在当前主体授权作业范围内找到指定工序任务。'
      return
    }
    deepLinkMessage.value = ''
    deepLinkOpenedIdentity.value = identity
    openSheet(exactTask)
  },
  { immediate: true, flush: 'post' },
)

function closeSheet() {
  selected.value = null
  confirmingComplete.value = false
}

async function recoverLifecycleUpdate() {
  closeSheet()
  clearOperationResultIntent()
  try {
    await refresh()
  } catch {
    // 刷新失败不阻断固定冲突提示，用户可随后手动刷新列表。
  }
  toast.message = LIFECYCLE_ACTION_UPDATED_MESSAGE
  toast.show = true
}

type OperationPageSnapshot = {
  generation: number
  identity: string
  selectionGeneration: number
  selectionIdentity: string
  context: OperationActionContext
}

function captureOperationPageSnapshot(context: OperationActionContext): OperationPageSnapshot {
  return {
    generation: operationPageGeneration.value,
    identity: operationPageIdentity.value,
    selectionGeneration: operationSelectionGeneration.value,
    selectionIdentity: operationSelectionIdentity.value,
    context,
  }
}

function isOperationPageSnapshotCurrent(snapshot: OperationPageSnapshot) {
  return (
    operationPageGeneration.value === snapshot.generation &&
    operationPageIdentity.value === snapshot.identity &&
    operationSelectionGeneration.value === snapshot.selectionGeneration &&
    operationSelectionIdentity.value === snapshot.selectionIdentity &&
    isOperationActionContextCurrent(snapshot.context)
  )
}

async function discardStaleOperationResult(snapshot: OperationPageSnapshot) {
  if (result.value?.context === snapshot.context) {
    result.value = null
    operationResultContextConflict.value = null
  }
  if (operationContext.value === snapshot.context) {
    operationKey.value = ''
    operationContext.value = null
    operationResultUnknown.value = false
    operationResultContextConflict.value = null
  }
  try {
    await refresh()
  } catch {
    // 上下文已变化时只丢弃旧结果；当前列表仍可由用户再次手动刷新。
  }
}

async function openSopFile(sop: CurrentSop) {
  const fileId = sop.fileId?.trim()
  if (!fileId) {
    sopFileError.value = '当前SOP未绑定可查看的文件。'
    return
  }
  sopFileError.value = ''
  openingSopFileId.value = fileId
  try {
    const grant = await createSopFileDownloadGrant(fileId)
    if (!grant) throw new Error('无法获取SOP查看授权。')
    await openDownloadGrantBlob(grant, { fetch: downloadFetch, timeoutMs: REQUEST_TIMEOUT_MS })
  } catch (error) {
    sopFileError.value = error instanceof Error ? error.message : '无法打开SOP。'
  } finally {
    openingSopFileId.value = null
  }
}

async function runAction(action: ActionKind) {
  if (!operationScopeReady.value) {
    toast.message = operationScopeMessage.value
    toast.show = true
    return
  }
  const task = selected.value
  if (!task?.workOrderId || !task.operationTaskId || !availableActions.value.includes(action))
    return
  // 完成是终态动作，先进入二次确认；在用户发起该动作（点动作按钮）时铸造稳定键
  if (action === 'complete' && !confirmingComplete.value) {
    confirmingComplete.value = true
    if (!operationResultUnknown.value) {
      operationKey.value = makeIdempotencyKey()
      operationContext.value = captureOperationActionContext(
        action,
        task.workOrderId,
        task.operationTaskId,
      )
    }
    return
  }
  // 非完成动作点击即发起；完成动作此处为确认（沿用进入确认时铸造的键）
  if (action !== 'complete') {
    if (!operationResultUnknown.value) {
      operationKey.value = makeIdempotencyKey()
      operationContext.value = captureOperationActionContext(
        action,
        task.workOrderId,
        task.operationTaskId,
      )
    }
  }
  const id = task.operationTaskId
  const workOrderId = task.workOrderId
  const displayReference = taskDisplayReference(task)
  const key = operationKey.value
  const context = operationContext.value
  if (!context) return
  const pageSnapshot = captureOperationPageSnapshot(context)
  closeSheet()
  try {
    await ACTION_FNS[action](workOrderId, id, key, context)
    if (!isOperationPageSnapshotCurrent(pageSnapshot)) {
      await discardStaleOperationResult(pageSnapshot)
      return
    }
    operationResultUnknown.value = false
    operationResultContextConflict.value = null
    result.value = {
      status: 'success',
      title: SUCCESS_TITLES[action],
      description: displayReference,
      action,
      displayReference,
      workOrderId,
      taskId: id,
      context,
    }
  } catch (e) {
    if (!isOperationPageSnapshotCurrent(pageSnapshot)) {
      await discardStaleOperationResult(pageSnapshot)
      return
    }
    if (isLifecycleActionUpdated(e)) {
      await recoverLifecycleUpdate()
      return
    }
    operationResultUnknown.value = isIndeterminateError(e)
    operationResultContextConflict.value = null
    result.value = {
      status: 'error',
      title: '操作失败',
      description: `${displayReference}\n${describeRequestError(e, '请检查网络后重试。').message}`,
      action,
      displayReference,
      workOrderId,
      taskId: id,
      context,
    }
  }
}

async function retry() {
  if (!operationScopeReady.value) {
    toast.message = operationScopeMessage.value
    toast.show = true
    return
  }
  const state = result.value
  if (!state) return
  const { action, displayReference, workOrderId, taskId, context } = state
  // 重试同一动作：复用发起时铸造的稳定幂等键，不重新铸造。
  const key = operationKey.value
  if (!isOperationResultRouteCurrent(state)) {
    operationResultUnknown.value = false
    operationResultContextConflict.value = 'route'
    result.value = {
      ...state,
      status: 'error',
      title: '操作失败',
      description: `${displayReference}\n工序任务链接已变化，旧操作不能在当前链接重试。请恢复原工单与任务链接后重试，或返回当前列表重新发起。`,
    }
    return
  }
  if (!isOperationActionContextCurrent(context)) {
    operationResultUnknown.value = false
    operationResultContextConflict.value = 'identity'
    result.value = {
      ...state,
      status: 'error',
      title: '操作失败',
      description: `${displayReference}\n账号、组织、环境或作业范围已变化，旧操作不能重试。请返回当前列表重新发起。`,
    }
    return
  }
  const pageSnapshot = captureOperationPageSnapshot(context)
  operationResultContextConflict.value = null
  result.value = null
  try {
    await ACTION_FNS[action](workOrderId, taskId, key, context)
    if (!isOperationPageSnapshotCurrent(pageSnapshot)) {
      await discardStaleOperationResult(pageSnapshot)
      return
    }
    operationResultUnknown.value = false
    operationResultContextConflict.value = null
    result.value = {
      status: 'success',
      title: SUCCESS_TITLES[action],
      description: displayReference,
      action,
      displayReference,
      workOrderId,
      taskId,
      context,
    }
  } catch (e) {
    if (!isOperationPageSnapshotCurrent(pageSnapshot)) {
      await discardStaleOperationResult(pageSnapshot)
      return
    }
    if (isLifecycleActionUpdated(e)) {
      await recoverLifecycleUpdate()
      return
    }
    operationResultUnknown.value = isIndeterminateError(e)
    operationResultContextConflict.value = null
    result.value = {
      status: 'error',
      title: '操作失败',
      description: `${displayReference}\n${describeRequestError(e, '请检查网络后重试。').message}`,
      action,
      displayReference,
      workOrderId,
      taskId,
      context,
    }
  }
}

function continueWork() {
  if (operationResultUnknown.value) return
  // 成功后回到列表态，作废本次操作幂等键 → 下次发起铸造新键
  clearOperationResultIntent()
}
function backToList() {
  if (operationResultUnknown.value) return
  clearOperationResultIntent()
  router.push('/').catch(() => {})
}

function onScan(value: string) {
  filters.keyword = value
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileButton
          type="button"
          aria-label="返回"
          :disabled="operationResultUnknown"
          variant="text"
          size="sm"
          class="min-h-touch text-muted-foreground"
          @click="backToList"
        >
          返回
        </NvMobileButton>
        <h1 class="text-lg font-semibold text-foreground">工序执行</h1>
      </div>
    </template>

    <MesOperationExecutionPanel
      :result="result"
      :selected="selected"
      :open="sheetOpen"
      :action-pending="actionPending"
      :operation-scope-ready="operationScopeReady"
      :confirming-complete="confirmingComplete"
      :current-sops="currentSops"
      :sops-pending="sopsPending"
      :sops-error="sopsError"
      :opening-sop-file-id="openingSopFileId"
      :sop-file-error="sopFileError"
      :operation-result-unknown="operationResultUnknown"
      @update:open="sheetOpen = $event"
      @action="runAction"
      @retry="retry"
      @continue="continueWork"
      @back="backToList"
      @cancel-complete="confirmingComplete = false"
      @refresh-sops="() => refreshSops()"
      @open-sop="openSopFile"
    />

    <TaskListShell
      v-if="!result"
      state-key="mes-operation-tasks"
      :scope="mesScope"
      source="工序任务服务（服务端按当前主体与所选授权作业范围过滤）"
      :loaded="loaded"
      :total="total"
      :updated-at="lastUpdatedAt"
      :pending="pending"
      :refreshing="refreshing"
      :loading-more="loadingMore"
      :error="operationListError"
      :load-more-error="loadMoreError"
      :filter-state="taskFilterState"
      :empty-description="mesEmptyExplanation"
      error-test-id="operation-tasks-error"
      failure-explanation="工序任务服务未成功返回，请刷新重试。"
      @refresh="() => refresh()"
      @retry="() => refresh()"
      @load-more="loadMore"
      @retry-load-more="loadMore"
      @restore="restoreTaskListState"
    >
      <template #filters>
        <div class="space-y-3 px-4 py-3">
          <NvScanBar placeholder="扫描工单 / 工序号" :active="scanActive" @scan="onScan" />
          <MesWorkScopeFilter permission-code="business.mes.operations.read" />
          <NvMobileDropdownMenu>
            <NvMobileDropdownMenuItem
              v-model="statusModel"
              title="任务状态"
              :options="statusOptions"
            />
          </NvMobileDropdownMenu>
          <p
            v-if="deepLinkMessage"
            data-testid="operation-deep-link-message"
            class="rounded-lg border border-destructive/40 px-4 py-3 text-sm text-destructive"
            role="alert"
          >
            {{ deepLinkMessage }}
          </p>
          <p
            v-if="operationListScopeMessage"
            data-testid="operation-list-scope-message"
            class="rounded-lg border border-destructive/40 px-4 py-3 text-sm text-destructive"
            role="alert"
          >
            {{ operationListScopeMessage }}
          </p>
          <p
            v-if="operationListScopeReady && operationScopeMessage"
            data-testid="operation-scope-message"
            class="rounded-lg border border-destructive/40 px-4 py-3 text-sm text-destructive"
            role="alert"
          >
            {{ operationScopeMessage }}
          </p>
        </div>
      </template>

      <div class="space-y-4 p-4">
        <div
          v-if="showOperationTasksEmpty"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          当前主体授权作业范围内暂无工序任务
        </div>

        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="task in visibleOperationTasks"
            :key="task.operationTaskId ?? `${task.workOrderId}-${task.operationSequence}`"
            :title="rowTitle(task)"
            :subtitle="rowSubtitle(task)"
            @select="openSheet(task)"
          />
        </div>
      </div>
    </TaskListShell>

    <NvMobileToast
      :show="toast.show"
      :message="toast.message"
      :type="toast.type"
      @update:show="toast.show = $event"
    />
  </NvAppShellMobile>
</template>
