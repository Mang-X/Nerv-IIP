<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { openDownloadGrantBlob, operationTaskStatusLabel } from '@nerv-iip/business-core'
import { createTimeoutFetch } from '@/api/request-timeout'
import RetryableListError from '@/components/RetryableListError.vue'
import { useMesCurrentOperationSops, useMesOperationTasks } from '@/composables/useBusinessMes'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

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
  pending,
  error,
  startTask,
  pauseTask,
  resumeTask,
  completeTask,
  actionPending,
  refresh,
} = useMesOperationTasks()
const {
  filters: sopFilters,
  currentSops,
  pending: sopsPending,
  error: sopsError,
  refresh: refreshSops,
  createSopFileDownloadGrant,
} = useMesCurrentOperationSops()

const router = useRouter()

// SOP 文件下载走 PDA 全局超时 fetch —— 弱网/离线有界失败，不无限挂起（#814）。
const downloadFetch = createTimeoutFetch()

// 可读中文状态标签来自 @nerv-iip/business-core（不暴露原始状态码）。
const statusLabel = operationTaskStatusLabel

type ActionKind = 'start' | 'pause' | 'resume' | 'complete'

// 按当前状态决定可用动作
function actionsFor(status?: string): ActionKind[] {
  switch (status) {
    case 'Ready':
      return ['start']
    case 'Running':
    case 'Started':
    case 'InProgress':
      return ['pause', 'complete']
    case 'Paused':
    case 'Held':
      return ['resume', 'complete']
    default:
      return []
  }
}

const ACTION_LABELS: Record<ActionKind, string> = {
  start: '开始',
  pause: '暂停',
  resume: '恢复',
  complete: '完成',
}

// 成功后的可读标题
const SUCCESS_TITLES: Record<ActionKind, string> = {
  start: '工序已开始',
  pause: '工序已暂停',
  resume: '工序已恢复',
  complete: '工序已完成',
}

const ACTION_FNS: Record<ActionKind, (id: string, idempotencyKey: string) => Promise<unknown>> = {
  start: (id, idempotencyKey) => startTask(id, { idempotencyKey }),
  pause: (id, idempotencyKey) => pauseTask(id, { idempotencyKey }),
  resume: (id, idempotencyKey) => resumeTask(id, { idempotencyKey }),
  complete: (id, idempotencyKey) => completeTask(id, { idempotencyKey }),
}

// 稳定的逐动作幂等键：用户发起某动作时铸造一次，重试该动作复用同键；
// 换动作或重新打开面板 → 新键。
const operationKey = ref('')

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
type ResultState = {
  status: 'success' | 'error'
  title: string
  description?: string
  action: ActionKind
  taskId: string
}
const result = ref<ResultState | null>(null)
const openingSopFileId = ref<string | null>(null)
const sopFileError = ref('')

const availableActions = computed(() => actionsFor(selected.value?.status))

const scanActive = computed(() => selected.value === null && result.value === null)

function rowTitle(task: Task) {
  const seq = task.operationSequence === undefined ? '' : `工序 ${task.operationSequence}`
  const wo = task.workOrderId ?? '无工单'
  return seq ? `${wo} · ${seq}` : wo
}
function rowSubtitle(task: Task) {
  const parts = [statusLabel(task.status)]
  if (task.workCenterId) parts.push(`工作中心 ${task.workCenterId}`)
  if (task.operationCode) parts.push(`工序 ${task.operationCode}`)
  return parts.join(' · ')
}

function openSheet(task: Task) {
  result.value = null
  sopFileError.value = ''
  confirmingComplete.value = false
  // 重新打开面板 → 新一轮操作，作废上一个幂等键
  operationKey.value = ''
  selected.value = task
  sopFilters.operationCode = task.operationCode?.trim() ?? ''
  sopFilters.workCenterCode = (task.workCenterCode ?? task.workCenterId)?.trim() ?? ''
  sopFilters.routingCode = ''
  sopFilters.routingRevision = ''
  sopFilters.asOfDate = ''
}
function closeSheet() {
  selected.value = null
  confirmingComplete.value = false
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
    await openDownloadGrantBlob(grant, { fetch: downloadFetch })
  } catch (error) {
    sopFileError.value = error instanceof Error ? error.message : '无法打开SOP。'
  } finally {
    openingSopFileId.value = null
  }
}

async function runAction(action: ActionKind) {
  const task = selected.value
  if (!task?.operationTaskId) return
  // 完成是终态动作，先进入二次确认；在用户发起该动作（点动作按钮）时铸造稳定键
  if (action === 'complete' && !confirmingComplete.value) {
    confirmingComplete.value = true
    operationKey.value = makeIdempotencyKey()
    return
  }
  // 非完成动作点击即发起；完成动作此处为确认（沿用进入确认时铸造的键）
  if (action !== 'complete') {
    operationKey.value = makeIdempotencyKey()
  }
  const id = task.operationTaskId
  const key = operationKey.value
  closeSheet()
  try {
    await ACTION_FNS[action](id, key)
    result.value = { status: 'success', title: SUCCESS_TITLES[action], action, taskId: id }
  } catch (e) {
    result.value = {
      status: 'error',
      title: '操作失败',
      description: e instanceof Error ? e.message : '请检查网络后重试。',
      action,
      taskId: id,
    }
  }
}

async function retry() {
  const state = result.value
  if (!state) return
  const { action, taskId } = state
  // 重试同一动作：复用发起时铸造的稳定幂等键，不重新铸造。
  const key = operationKey.value
  result.value = null
  try {
    await ACTION_FNS[action](taskId, key)
    result.value = { status: 'success', title: SUCCESS_TITLES[action], action, taskId }
  } catch (e) {
    result.value = {
      status: 'error',
      title: '操作失败',
      description: e instanceof Error ? e.message : '请检查网络后重试。',
      action,
      taskId,
    }
  }
}

function continueWork() {
  result.value = null
  // 成功后回到列表态，作废本次操作幂等键 → 下次发起铸造新键
  operationKey.value = ''
}
function backToList() {
  result.value = null
  operationKey.value = ''
  router.push('/').catch(() => {})
}

function onScan(value: string) {
  filters.keyword = value
}
function formatDate(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString()
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <button
          type="button"
          aria-label="返回"
          class="text-sm text-muted-foreground"
          @click="router.push('/').catch(() => {})"
        >
          返回
        </button>
        <h1 class="text-lg font-semibold text-foreground">工序执行</h1>
      </div>
    </template>

    <!-- 动作结果反馈 -->
    <NvMobileResult
      v-if="result"
      :status="result.status"
      :title="result.title"
      :description="result.description"
    >
      <template #actions>
        <button
          v-if="result.status === 'success'"
          type="button"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="continueWork"
        >
          继续
        </button>
        <button
          v-else
          type="button"
          data-testid="retry-action"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="retry"
        >
          重试
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="backToList"
        >
          返回列表
        </button>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <NvScanBar placeholder="扫描工单 / 工序号" :active="scanActive" @scan="onScan" />

      <p class="text-sm text-muted-foreground">共 {{ total }} 个工序任务</p>

      <RetryableListError
        v-if="error"
        :error="error"
        :pending="pending"
        fallback="加载工序任务失败，请下拉刷新或重试。"
        test-id="operation-tasks-error"
        @retry="() => refresh()"
      />

      <div
        v-else-if="!pending && operationTasks.length === 0"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无工序任务
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          v-for="task in operationTasks"
          :key="task.operationTaskId ?? `${task.workOrderId}-${task.operationSequence}`"
          :title="rowTitle(task)"
          :subtitle="rowSubtitle(task)"
          @select="openSheet(task)"
        />
      </div>
    </div>

    <!-- 动作面板 -->
    <NvBottomSheet
      :open="sheetOpen"
      :title="selected ? rowTitle(selected) : ''"
      @update:open="sheetOpen = $event"
    >
      <div v-if="selected" class="space-y-3 pb-2">
        <p class="text-sm text-muted-foreground">当前状态：{{ statusLabel(selected.status) }}</p>

        <section class="space-y-2 rounded-lg border border-border px-3 py-3">
          <div class="flex items-center justify-between gap-3">
            <h2 class="text-sm font-semibold text-foreground">当前SOP</h2>
            <span v-if="selected.operationCode" class="font-mono text-xs text-muted-foreground">{{
              selected.operationCode
            }}</span>
          </div>
          <p v-if="!selected.operationCode" class="text-sm text-muted-foreground">
            当前任务未绑定标准工序。
          </p>
          <RetryableListError
            v-else-if="sopsError"
            :error="sopsError"
            :pending="sopsPending"
            fallback="加载SOP失败，请稍后重试。"
            test-id="sops-error"
            @retry="() => refreshSops()"
          />
          <template v-else>
            <p v-if="sopsPending" class="text-sm text-muted-foreground">正在加载SOP...</p>
            <div v-else-if="currentSops.length" class="space-y-2">
              <div
                v-for="sop in currentSops"
                :key="`${sop.documentNumber}-${sop.revision}-${sop.fileId}`"
                class="rounded-md bg-muted px-3 py-2 text-sm"
              >
                <p class="font-medium text-foreground">{{ sop.fileName || sop.documentNumber }}</p>
                <p class="text-xs text-muted-foreground">
                  {{ sop.documentNumber }} · rev {{ sop.revision }} · 生效
                  {{ formatDate(sop.effectiveDate) }}
                </p>
                <button
                  type="button"
                  class="mt-2 min-h-touch rounded-md border border-border bg-card px-3 text-sm font-medium text-foreground disabled:opacity-60"
                  :disabled="openingSopFileId === sop.fileId"
                  @click="openSopFile(sop)"
                >
                  查看SOP
                </button>
              </div>
            </div>
            <p v-else class="text-sm text-muted-foreground">当前没有已生效SOP。</p>
            <!-- 打开文件失败（含超时/离线）：独立展示，保留 SOP 列表与“查看SOP”按钮以便再次尝试。 -->
            <p
              v-if="sopFileError"
              data-testid="sop-file-error"
              class="text-sm text-destructive"
              role="alert"
            >
              {{ sopFileError }}
            </p>
          </template>
        </section>

        <!-- 完成的二次确认 -->
        <div v-if="confirmingComplete" class="space-y-3">
          <p class="text-sm text-foreground">完成后该工序将进入终态，确认完成？</p>
          <button
            type="button"
            data-testid="confirm-complete"
            :disabled="actionPending"
            class="min-h-touch w-full rounded-lg bg-destructive text-base font-medium text-destructive-foreground disabled:opacity-60"
            @click="runAction('complete')"
          >
            确认完成
          </button>
          <button
            type="button"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
            @click="confirmingComplete = false"
          >
            取消
          </button>
        </div>

        <!-- 动作列表 -->
        <div v-else class="space-y-2">
          <button
            v-for="action in availableActions"
            :key="action"
            type="button"
            :data-testid="`action-${action}`"
            :disabled="actionPending"
            class="min-h-touch w-full rounded-lg text-base font-medium disabled:opacity-60"
            :class="
              action === 'complete'
                ? 'bg-destructive text-destructive-foreground'
                : 'bg-primary text-primary-foreground'
            "
            @click="runAction(action)"
          >
            {{ ACTION_LABELS[action] }}
          </button>
          <p
            v-if="availableActions.length === 0"
            class="rounded-lg border border-dashed border-border px-4 py-4 text-center text-sm text-muted-foreground"
          >
            当前状态无可执行动作
          </p>
        </div>
      </div>
    </NvBottomSheet>
  </NvAppShellMobile>
</template>
