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
import { useMesCurrentOperationSops, useMesOperationTasks } from '@/composables/useBusinessMes'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import {
  isLifecycleActionUpdated,
  LIFECYCLE_ACTION_UPDATED_MESSAGE,
} from '@/composables/lifecycleActionRecovery'
import { usePendingWriteLeaveGuard } from '@/composables/usePendingWriteLeaveGuard'
import {
  NvAppShellMobile,
  NvListRow,
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
  operationListScopeMessage,
  operationListScopeReady,
  operationScopeMessage,
  operationScopeReady,
  captureOperationActionContextIdentity,
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
    `${operationListContextIdentity.value}\u0000${requestedWorkOrderId.value}\u0000${requestedOperationTaskId.value}`,
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
    contextIdentity: string,
  ) => Promise<unknown>
> = {
  start: (workOrderId, operationTaskId, idempotencyKey, contextIdentity) =>
    startTask(workOrderId, operationTaskId, { idempotencyKey, contextIdentity }),
  pause: (workOrderId, operationTaskId, idempotencyKey, contextIdentity) =>
    pauseTask(workOrderId, operationTaskId, { idempotencyKey, contextIdentity }),
  resume: (workOrderId, operationTaskId, idempotencyKey, contextIdentity) =>
    resumeTask(workOrderId, operationTaskId, { idempotencyKey, contextIdentity }),
  complete: (workOrderId, operationTaskId, idempotencyKey, contextIdentity) =>
    completeTask(workOrderId, operationTaskId, { idempotencyKey, contextIdentity }),
}

// 稳定的逐动作幂等键：用户发起某动作时铸造一次，重试该动作复用同键；
// 换动作或重新打开面板 → 新键。
const operationKey = ref('')
const operationContextIdentity = ref('')
const operationResultUnknown = ref(false)
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

function restoreTaskListState(state: { filters: Record<string, unknown> }) {
  if (hasAnyTaskDeepLink.value) return
  const status = state.filters.status
  const keyword = state.filters.keyword
  filters.status = typeof status === 'string' && status ? status : undefined
  filters.keyword = typeof keyword === 'string' && keyword ? keyword : undefined
}

function openSheet(task: Task) {
  if (operationResultUnknown.value) return
  result.value = null
  sopFileError.value = ''
  confirmingComplete.value = false
  // 重新打开面板 → 新一轮操作，作废上一个幂等键
  if (!operationResultUnknown.value) {
    operationKey.value = ''
    operationContextIdentity.value = ''
  }
  selected.value = task
  sopFilters.operationCode = task.operationCode?.trim() ?? ''
  sopFilters.workCenterCode = (task.workCenterCode ?? task.workCenterId)?.trim() ?? ''
  sopFilters.routingCode = ''
  sopFilters.routingRevision = ''
  sopFilters.asOfDate = ''
}

const deepLinkOpenedIdentity = ref('')
watch(
  [requestedWorkOrderId, requestedOperationTaskId, operationListContextIdentity],
  ([workOrderId, operationTaskId]) => {
    closeSheet()
    if (!operationResultUnknown.value) result.value = null
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
  [visibleOperationTasks, pending, hasSuccessfulResponse, deepLinkIdentity],
  ([tasks, isPending, successful, identity]) => {
    if (
      !hasCompleteTaskDeepLink.value ||
      deepLinkOpenedIdentity.value === identity ||
      isPending ||
      !successful
    )
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
  { immediate: true },
)

function closeSheet() {
  selected.value = null
  confirmingComplete.value = false
}

async function recoverLifecycleUpdate() {
  closeSheet()
  result.value = null
  operationKey.value = ''
  operationContextIdentity.value = ''
  operationResultUnknown.value = false
  try {
    await refresh()
  } catch {
    // 刷新失败不阻断固定冲突提示，用户可随后手动刷新列表。
  }
  toast.message = LIFECYCLE_ACTION_UPDATED_MESSAGE
  toast.show = true
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
      operationContextIdentity.value = captureOperationActionContextIdentity(
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
      operationContextIdentity.value = captureOperationActionContextIdentity(
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
  const contextIdentity = operationContextIdentity.value
  closeSheet()
  try {
    await ACTION_FNS[action](workOrderId, id, key, contextIdentity)
    operationResultUnknown.value = false
    result.value = {
      status: 'success',
      title: SUCCESS_TITLES[action],
      description: displayReference,
      action,
      displayReference,
      workOrderId,
      taskId: id,
      contextIdentity,
    }
  } catch (e) {
    if (isLifecycleActionUpdated(e)) {
      await recoverLifecycleUpdate()
      return
    }
    operationResultUnknown.value = isIndeterminateError(e)
    result.value = {
      status: 'error',
      title: '操作失败',
      description: `${displayReference}\n${describeRequestError(e, '请检查网络后重试。').message}`,
      action,
      displayReference,
      workOrderId,
      taskId: id,
      contextIdentity,
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
  const { action, displayReference, workOrderId, taskId, contextIdentity } = state
  // 重试同一动作：复用发起时铸造的稳定幂等键，不重新铸造。
  const key = operationKey.value
  result.value = null
  try {
    await ACTION_FNS[action](workOrderId, taskId, key, contextIdentity)
    operationResultUnknown.value = false
    result.value = {
      status: 'success',
      title: SUCCESS_TITLES[action],
      description: displayReference,
      action,
      displayReference,
      workOrderId,
      taskId,
      contextIdentity,
    }
  } catch (e) {
    if (isLifecycleActionUpdated(e)) {
      await recoverLifecycleUpdate()
      return
    }
    operationResultUnknown.value = isIndeterminateError(e)
    result.value = {
      status: 'error',
      title: '操作失败',
      description: `${displayReference}\n${describeRequestError(e, '请检查网络后重试。').message}`,
      action,
      displayReference,
      workOrderId,
      taskId,
      contextIdentity,
    }
  }
}

function continueWork() {
  if (operationResultUnknown.value) return
  result.value = null
  // 成功后回到列表态，作废本次操作幂等键 → 下次发起铸造新键
  operationKey.value = ''
  operationContextIdentity.value = ''
}
function backToList() {
  if (operationResultUnknown.value) return
  result.value = null
  operationKey.value = ''
  operationContextIdentity.value = ''
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
        <button
          type="button"
          aria-label="返回"
          :disabled="operationResultUnknown"
          class="text-sm text-muted-foreground"
          @click="backToList"
        >
          返回
        </button>
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
