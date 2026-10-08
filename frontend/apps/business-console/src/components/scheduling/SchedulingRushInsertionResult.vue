<script setup lang="ts">
import type {
  BusinessConsoleSchedulingInsertionPreviewJob,
  BusinessConsoleSchedulingInsertionOrderImpact,
} from '@nerv-iip/api-client'
import { ChangeSummaryPanel, toModel } from '@nerv-iip/scheduling'
import { NvButton, NvDataTable, type NvDataTableColumn } from '@nerv-iip/ui'
import { computed } from 'vue'
import { formatDateTime } from '@/utils/format'
import SchedulingPreviewResult from './SchedulingPreviewResult.vue'
const props = defineProps<{
  job?: BusinessConsoleSchedulingInsertionPreviewJob
  message?: string
  pending?: boolean
  saving?: boolean
  selected?: boolean
}>()
const emit = defineEmits<{ retry: []; select: [] }>()
const result = computed(() => props.job?.result)
const candidate = computed(() => result.value?.candidate)
const baseline = computed(
  () => result.value?.snapshot?.baseline ?? props.job?.acceptedBaseline?.baseline,
)
const model = computed(() => toModel(candidate.value ?? {}))
const baseModel = computed(() => toModel(baseline.value ?? {}))
const status = computed(
  () =>
    ({
      created: '已受理，等待计算',
      running: '计算中',
      failed: '计算失败',
      completed: '候选计算完成',
    })[props.job?.status ?? 'created'],
)
const reasons: Record<string, string> = {
  OperationDeviation: '工序偏差',
  NewOperation: '新增工序',
  ResourceUnavailable: '资源不可用',
  PredecessorDependency: '前后置依赖',
  ResourceCapacity: '产能竞争',
  'rush-insertion': '急单插入',
  unscheduled: '工序未排入',
  'frozen-conflict': '冻结冲突',
}
const kpis = computed(() => {
  const k = result.value?.kpis
  const rate = (value?: number) => (value == null ? '—' : `${(value * 100).toFixed(1)}%`)
  return [
    ['准时率', `${rate(k?.onTimeRate?.baseline)} → ${rate(k?.onTimeRate?.candidate)}`],
    [
      '破交期工单',
      `${k?.lateOrderCount?.baseline ?? '—'} → ${k?.lateOrderCount?.candidate ?? '—'}`,
    ],
    ['移动工序', k?.movedOperationCount ?? '—'],
    [
      '资源利用率',
      `${rate(k?.resourceUtilization?.baseline)} → ${rate(k?.resourceUtilization?.candidate)}`,
    ],
    [
      '未排工序',
      `${k?.unscheduledOperationCount?.baseline ?? '—'} → ${k?.unscheduledOperationCount?.candidate ?? '—'}`,
    ],
    ['锁定保留', `${k?.lockRetention?.preserved ?? '—'} / ${k?.lockRetention?.total ?? '—'}`],
  ]
})
const failureLabels = {
  unknownMaterialEta: '物料到齐时间未知',
  incompleteChain: '工艺链未完整排入',
  blockingConflict: '存在阻断冲突',
}
const columns: NvDataTableColumn<BusinessConsoleSchedulingInsertionOrderImpact>[] = [
  { key: 'orderId', header: '工单' },
  {
    key: 'status',
    header: '影响',
    accessor: (row) =>
      ({ delayed: '被推迟', unchanged: '保持不变', new: '新增工单', unscheduled: '未排完整' })[
        row.status ?? 'unchanged'
      ],
  },
  {
    key: 'baselineCompletionUtc',
    header: '原完工',
    accessor: (row) => formatDateTime(row.baselineCompletionUtc),
  },
  {
    key: 'candidateCompletionUtc',
    header: '新完工',
    accessor: (row) => formatDateTime(row.candidateCompletionUtc),
  },
  {
    key: 'delayDays',
    header: '推迟天数',
    accessor: (row) => (row.delayDays == null ? '—' : `${row.delayDays} 天`),
  },
  { key: 'baselineLate', header: '原破交期', accessor: (row) => (row.baselineLate ? '是' : '否') },
  {
    key: 'candidateLate',
    header: '新破交期',
    accessor: (row) => (row.newlyLate ? '新增破交期' : row.candidateLate ? '原已破交期' : '否'),
  },
]
</script>
<template>
  <section
    v-if="job || message"
    class="grid gap-4 rounded-lg border bg-card p-4"
    aria-label="急单插单候选"
  >
    <header class="flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-lg font-semibold">急单插单候选</h2>
      <span v-if="job" role="status">{{ status }}</span>
    </header>
    <p v-if="message" role="status" class="text-sm text-muted-foreground">{{ message }}</p>
    <p v-if="job" class="text-sm">
      受理基线 {{ job.input?.planId }} · 插入工单 {{ job.input?.workOrderId
      }}<template v-if="result"> · 候选 {{ result.candidatePlanId }}</template>
    </p>
    <div
      v-if="job?.status === 'failed' || (message && job?.status !== 'completed')"
      class="flex items-center gap-3 text-sm"
    >
      <span v-if="job?.failureReason">{{ job.failureReason }}；急单已保存。</span>
      <NvButton
        v-if="job || message?.includes('失败')"
        type="button"
        variant="outline"
        :disabled="pending"
        @click="emit('retry')"
        >手动重试</NvButton
      >
    </div>
    <template v-if="result">
      <div class="rounded-md border p-3">
        <p class="text-sm text-muted-foreground">
          {{ result.promiseUtc ? '可承诺交期' : '不可承诺' }}
        </p>
        <p v-if="result.promiseUtc" class="text-lg font-semibold">
          {{ formatDateTime(result.promiseUtc) }}
        </p>
        <p v-else class="font-medium">
          {{ result.failures?.map((f) => failureLabels[f]).join('、') }}
        </p>
      </div>
      <dl class="grid grid-cols-2 gap-3 lg:grid-cols-6">
        <div v-for="[label, value] in kpis" :key="label">
          <dt class="text-xs text-muted-foreground">{{ label }}</dt>
          <dd class="font-semibold tabular-nums">{{ value }}</dd>
        </div>
      </dl>
      <NvDataTable
        :columns="columns"
        :rows="result.orders ?? []"
        row-key="orderId"
        :searchable="false"
        :pagination="false"
        :column-settings="false"
        density="compact"
      />
      <ChangeSummaryPanel
        :changes="model.changes"
        :tasks="model.tasks"
        :base-tasks="baseModel.tasks"
      />
      <details
        v-for="operation in result.operations"
        :key="`${operation.orderId}:${operation.operationId}`"
        class="rounded-md border p-3 text-sm"
      >
        <summary class="cursor-pointer font-medium">
          {{ operation.orderId }} · {{ operation.operationId }} · 工序影响原因
        </summary>
        <p class="mt-2">
          {{ operation.reasonCodes?.map((r) => reasons[r] ?? '排程约束影响').join('、') }}
        </p>
        <ol v-for="(path, index) in operation.paths" :key="index" class="mt-2 grid gap-1">
          <li v-for="(step, i) in path" :key="i">
            {{ step.fromOrderId }} / {{ step.fromOperationId }} → {{ step.toOrderId }} /
            {{ step.toOperationId }}：{{ reasons[step.reasonCode ?? ''] ?? '排程约束影响'
            }}<template v-if="step.competitionWindow">
              · {{ formatDateTime(step.competitionWindow.startUtc) }} 至
              {{ formatDateTime(step.competitionWindow.endUtc) }}</template
            >
          </li>
        </ol>
      </details>
      <SchedulingPreviewResult v-if="candidate" :plan="candidate" />
      <div class="flex items-center justify-between gap-3">
        <p class="text-sm text-muted-foreground">计算完成尚未发布，选定后保存修订，再确认发布。</p>
        <NvButton
          type="button"
          :disabled="selected || saving || pending || !candidate"
          @click="emit('select')"
          >{{
            selected ? '候选修订已保存' : saving ? '正在保存修订…' : '选定候选并保存修订'
          }}</NvButton
        >
      </div>
    </template>
  </section>
</template>
