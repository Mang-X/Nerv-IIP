<script setup lang="ts">
import QualityExecuteStep from '@/components/quality/QualityExecuteStep.vue'
import QualityResultStep from '@/components/quality/QualityResultStep.vue'
import QualityTaskListStep from '@/components/quality/QualityTaskListStep.vue'
import type {
  AuthoritativeInspectionResult,
  QualityResultState,
} from '@/composables/useInspectionExecution'
import {
  useBusinessQualityInspectionTasks,
  useInspectionPlanCharacteristics,
} from '@/composables/useBusinessQualityInspectionTasks'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { NvAppShellMobile, NvMobileButton } from '@nerv-iip/ui-mobile'
import { computed, ref, useTemplateRef } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '检验任务',
  },
})

type Task = BusinessConsoleQualityInspectionTaskItem

const router = useRouter()

// 列表 / 提交 / 原因码目录（单一 composable 实例，执行表单复用其 submit）。
const {
  tasks,
  total,
  loaded,
  hasMore,
  loadMore,
  ensureAllLoaded,
  pending,
  error,
  refresh,
  reasonCodes,
  submitInspection,
  submitPending,
} = useBusinessQualityInspectionTasks()

// 选中任务的检验计划特性（可选可搜数据源；单位/公差/类别直接匹配）。
const selectedTask = ref<Task | null>(null)
const {
  characteristics: planCharacteristics,
  planCode,
  pending: planCharacteristicsPending,
  error: planCharacteristicsError,
  refresh: refreshPlanCharacteristics,
} = useInspectionPlanCharacteristics(computed(() => selectedTask.value?.inspectionPlanId))
const planLabel = computed(() => planCode.value || selectedTask.value?.inspectionPlanId || '')

const result = ref<QualityResultState | null>(null)
const executeRef = useTemplateRef<InstanceType<typeof QualityExecuteStep>>('executeRef')

const inListStep = computed(() => selectedTask.value === null && result.value === null)
// StepFlow 头部指示：选任务(1) → 执行(2) → 结果(3)。
const stepNumber = computed(() => (result.value ? 3 : selectedTask.value ? 2 : 1))

function selectTask(task: Task) {
  selectedTask.value = task
}
function backToList() {
  selectedTask.value = null
  result.value = null
}

function onSubmitted(authoritative: AuthoritativeInspectionResult) {
  result.value = { phase: 'submitted', authoritative }
}
function onFailed(message: string) {
  result.value = { phase: 'error', message }
}
function nextTask() {
  // 成功后回到列表继续下一个（列表已因提交失效自动刷新，已检任务回落）。
  backToList()
}
function retrySubmit() {
  // 保留执行态直接重提（执行表单以 v-show 常驻，提交端天然幂等）。
  result.value = null
  void executeRef.value?.submit()
}
function goBack() {
  router.push('/').catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileButton
          v-if="!inListStep && !result"
          variant="text"
          size="sm"
          aria-label="返回列表"
          @click="backToList"
        >
          返回
        </NvMobileButton>
        <h1 class="text-lg font-semibold text-foreground">检验任务</h1>
        <span v-if="!result" class="ml-auto text-xs text-muted-foreground">
          第 {{ stepNumber }}/3 步
        </span>
      </div>
    </template>

    <!-- 步骤 1：待检任务列表（无选中任务时）-->
    <QualityTaskListStep
      v-if="!selectedTask"
      :tasks="tasks"
      :total="total"
      :loaded="loaded"
      :has-more="hasMore"
      :pending="pending"
      :error="error"
      :load-all="ensureAllLoaded"
      @select="selectTask"
      @load-more="loadMore"
      @refresh="() => refresh()"
    />

    <!-- 步骤 2/3：执行（v-show 常驻，结果页在其上覆盖，便于失败后保留态重试）-->
    <template v-else>
      <QualityExecuteStep
        v-show="!result"
        ref="executeRef"
        :task="selectedTask"
        :plan-characteristics="planCharacteristics"
        :plan-characteristics-pending="planCharacteristicsPending"
        :plan-characteristics-error="planCharacteristicsError"
        :plan-label="planLabel"
        :reason-codes="reasonCodes"
        :submit-inspection="submitInspection"
        :submit-pending="submitPending"
        @back="backToList"
        @submitted="onSubmitted"
        @failed="onFailed"
        @refresh-characteristics="() => refreshPlanCharacteristics()"
      />
      <QualityResultStep
        v-if="result"
        :state="result"
        @next="nextTask"
        @back="goBack"
        @retry="retrySubmit"
      />
    </template>
  </NvAppShellMobile>
</template>
