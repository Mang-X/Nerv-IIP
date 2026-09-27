<script setup lang="ts">
import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesTelemetryCandidateRow,
} from '@nerv-iip/api-client'
import { NvListRow, NvSearchBar } from '@nerv-iip/ui-mobile'
import { computed, ref, watch } from 'vue'
import RetryableListError from '@/components/RetryableListError.vue'
import {
  type MesReportExecutionContext,
  useMesTelemetryCandidateTargetTasks,
} from '@/composables/useBusinessMes'
import { withReworkLabel } from './operationPresentation'

/**
 * 遥测候选转正时选「报到哪道工序」。车间惯例是选设备当前在制的工单工序，不输入编号：
 * 候选自带工单 / 工序就直接带出；设备上只有一道执行中的工序也直接带出；
 * 否则列出这台设备执行中的工序供点选，设备上没有时按工单号搜索。
 */
type Task = BusinessConsoleMesOperationTaskRow

const props = defineProps<{
  candidate: BusinessConsoleMesTelemetryCandidateRow
  context?: MesReportExecutionContext
}>()
const target = defineModel<Task | null>({ default: null })

const keyword = ref('')
const choosing = ref(false)
const searching = computed(() => keyword.value.trim() !== '')
const { tasks, pending, error, refresh } = useMesTelemetryCandidateTargetTasks(
  computed(() => props.context),
  computed(() => props.candidate.deviceAssetId ?? ''),
  keyword,
)

const linkedWorkOrderId = props.candidate.workOrderId?.trim()
const linkedOperationTaskId = props.candidate.operationTaskId?.trim()
const linked: Task | null =
  linkedWorkOrderId && linkedOperationTaskId
    ? { workOrderId: linkedWorkOrderId, operationTaskId: linkedOperationTaskId }
    : null
target.value = linked

// 立即执行：同一台设备的列表常已在缓存里，重开候选时不会再「到达」一次。
watch(
  tasks,
  (rows) => {
    // v-model 回写要等父组件下一次渲染才回到 props，首轮先认候选自带的那条。
    const current = target.value ?? linked
    if (current) {
      // 候选自带的只有编号，从列表里换成带工序序号的整行，好让上屏称呼完整。
      const full = rows.find((row) => row.operationTaskId === current.operationTaskId)
      if (full && full !== current) target.value = full
      return
    }
    if (!searching.value && rows.length === 1) target.value = rows[0]
  },
  { immediate: true },
)

function sequenceLabel(task: Task) {
  return task.operationSequence === undefined || task.operationSequence === null
    ? ''
    : `工序 ${task.operationSequence}`
}
function targetLabel(task: Task) {
  return withReworkLabel([task.workOrderId, sequenceLabel(task)].filter(Boolean).join(' · '), task)
}
// 手持屏窄：列表行标题只放工单号，工序与设备放副标题，免得工序号被截掉。
function rowTitle(task: Task) {
  return withReworkLabel(task.workOrderId ?? '', task)
}
function rowSubtitle(task: Task) {
  return [sequenceLabel(task), task.deviceAssetName, task.workCenterName]
    .filter(Boolean)
    .join(' · ')
}

function choose(task: Task) {
  target.value = task
  choosing.value = false
}
</script>

<template>
  <div class="space-y-2">
    <div
      v-if="target && !choosing"
      class="flex items-center justify-between gap-3 rounded-lg border border-border bg-background px-3 py-2"
    >
      <div class="min-w-0">
        <p class="text-xs text-muted-foreground">报工到</p>
        <p data-testid="telemetry-target" class="font-medium break-words text-foreground">
          {{ targetLabel(target) }}
        </p>
      </div>
      <button
        type="button"
        data-testid="telemetry-change-target"
        class="shrink-0 text-sm text-primary"
        @click="choosing = true"
      >
        改选
      </button>
    </div>
    <template v-else>
      <NvSearchBar v-model="keyword" placeholder="按工单号搜索执行中的工序" />
      <p v-if="!context" class="text-sm text-muted-foreground">
        报工范围未就绪，暂时无法选择工序。
      </p>
      <RetryableListError
        v-else-if="error"
        :error="error"
        :pending="pending"
        fallback="执行中工序读取失败，请重试。"
        test-id="telemetry-target-error"
        @retry="refresh"
      />
      <p v-else-if="pending" class="text-sm text-muted-foreground">正在加载执行中的工序…</p>
      <p
        v-else-if="tasks.length === 0"
        data-testid="telemetry-target-empty"
        class="text-sm text-muted-foreground"
      >
        {{ searching ? '没有匹配的执行中工序' : '这台设备当前没有执行中的工序，请按工单号搜索' }}
      </p>
      <template v-else>
        <p class="text-xs text-muted-foreground">
          {{ searching ? '选择要报工的工序' : '这台设备当前执行中的工序' }}
        </p>
        <div class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="task in tasks"
            :key="task.operationTaskId"
            :data-testid="`telemetry-target-option-${task.operationTaskId}`"
            :title="rowTitle(task)"
            :subtitle="rowSubtitle(task)"
            @select="choose(task)"
          />
        </div>
      </template>
    </template>
  </div>
</template>
