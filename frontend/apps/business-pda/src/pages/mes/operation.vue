<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { operationTaskStatusLabel } from '@nerv-iip/business-core'
import { useMesOperationTasks } from '@/composables/useBusinessMes'
import { AppShellMobile, BottomSheet, ListRow, Result, ScanBar } from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工序执行',
  },
})

type Task = BusinessConsoleMesOperationTaskRow

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
} = useMesOperationTasks()

const router = useRouter()

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

const ACTION_FNS: Record<ActionKind, (id: string) => Promise<unknown>> = {
  start: (id) => startTask(id),
  pause: (id) => pauseTask(id),
  resume: (id) => resumeTask(id),
  complete: (id) => completeTask(id),
}

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
type ResultState = { status: 'success' | 'error'; title: string; description?: string; action: ActionKind; taskId: string }
const result = ref<ResultState | null>(null)

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
  return parts.join(' · ')
}

const errorMessage = computed(() => {
  const e = error.value
  if (!e) return ''
  return e instanceof Error ? e.message : '加载工序任务失败，请下拉刷新或重试。'
})

function openSheet(task: Task) {
  result.value = null
  confirmingComplete.value = false
  selected.value = task
}
function closeSheet() {
  selected.value = null
  confirmingComplete.value = false
}

async function runAction(action: ActionKind) {
  const task = selected.value
  if (!task?.operationTaskId) return
  // 完成是终态动作，先进入二次确认
  if (action === 'complete' && !confirmingComplete.value) {
    confirmingComplete.value = true
    return
  }
  const id = task.operationTaskId
  closeSheet()
  try {
    await ACTION_FNS[action](id)
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
  result.value = null
  try {
    await ACTION_FNS[action](taskId)
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
}
function backToList() {
  result.value = null
  router.push('/').catch(() => {})
}

function onScan(value: string) {
  filters.keyword = value
}
</script>

<template>
  <AppShellMobile>
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
    <Result
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
    </Result>

    <div v-else class="space-y-4 p-4">
      <ScanBar placeholder="扫描工单 / 工序号" :active="scanActive" @scan="onScan" />

      <p class="text-sm text-muted-foreground">共 {{ total }} 个工序任务</p>

      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

      <div
        v-if="!pending && operationTasks.length === 0"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无工序任务
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <ListRow
          v-for="task in operationTasks"
          :key="task.operationTaskId ?? `${task.workOrderId}-${task.operationSequence}`"
          :title="rowTitle(task)"
          :subtitle="rowSubtitle(task)"
          @select="openSheet(task)"
        />
      </div>
    </div>

    <!-- 动作面板 -->
    <BottomSheet
      :open="sheetOpen"
      :title="selected ? rowTitle(selected) : ''"
      @update:open="sheetOpen = $event"
    >
      <div v-if="selected" class="space-y-3 pb-2">
        <p class="text-sm text-muted-foreground">
          当前状态：{{ statusLabel(selected.status) }}
        </p>

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
            :class="action === 'complete'
              ? 'bg-destructive text-destructive-foreground'
              : 'bg-primary text-primary-foreground'"
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
    </BottomSheet>
  </AppShellMobile>
</template>
