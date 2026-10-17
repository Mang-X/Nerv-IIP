<script setup lang="ts">
import { computed } from 'vue'
import type {
  BusinessConsoleSchedulingConflict,
  BusinessConsoleSchedulingUnscheduledOperation,
} from '@nerv-iip/api-client'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{
  conflicts: BusinessConsoleSchedulingConflict[]
  unscheduledOperations: BusinessConsoleSchedulingUnscheduledOperation[]
  generatedAtUtc?: string
}>()
const blocking = computed(() =>
  props.conflicts.filter(
    (conflict) => conflict.severity !== 'warning' && conflict.severity !== 'info',
  ),
)
const notices = computed(() => [
  ...Map.groupBy(
    props.conflicts.filter(
      (conflict) => conflict.severity === 'warning' || conflict.severity === 'info',
    ),
    (conflict) =>
      [severityLabel(conflict.severity), reasonLabel(conflict.reasonCode)]
        .filter(Boolean)
        .join(' · '),
  ),
])
function conflictIdentity(conflict: BusinessConsoleSchedulingConflict) {
  return [conflict.orderId, conflict.operationId, conflict.resourceId].filter(Boolean).join(' · ')
}
function conflictText(conflict: BusinessConsoleSchedulingConflict) {
  return [
    severityLabel(conflict.severity),
    reasonLabel(conflict.reasonCode),
    conflict.message ?? '',
  ]
    .filter(Boolean)
    .join(' · ')
}

function unscheduledText(item: BusinessConsoleSchedulingUnscheduledOperation) {
  return [
    item.orderId ?? '未关联工单',
    item.operationId ?? '工序',
    reasonLabel(item.reasonCode),
    item.message ?? '',
  ]
    .filter(Boolean)
    .join(' · ')
}

function severityLabel(severity?: string | null) {
  if (severity === 'info') return '提示'
  if (severity === 'warning') return '预警'
  if (severity === 'error') return '阻断'
  return ''
}

function reasonLabel(reason?: string | null) {
  const labels: Record<string, string> = {
    dueDate: '交期风险',
    capacity: '产能不足',
    calendar: '日历不可用',
    material: '物料约束',
    quality: '质量约束',
    equipment: '设备约束',
    tooling: '工装约束',
    noEligibleResource: '无可用资源',
    outsideHorizon: '超出排程窗口',
    invalidLockedAssignment: '锁定分配无效',
    predecessorUnscheduled: '前序未排',
  }

  return reason ? (labels[reason] ?? reason) : ''
}
</script>

<template>
  <section class="grid gap-3" aria-label="方案冻结冲突与不可排原因">
    <header>
      <h3 class="text-sm font-semibold text-foreground">方案冻结冲突与不可排原因</h3>
      <p class="text-sm text-muted-foreground">
        方案生成时的快照，不代表当前 MES 齐套或设备状态；开工前请核对当前状态。
        <span v-if="generatedAtUtc">生成于 {{ formatDateTime(generatedAtUtc) }}（本地时间）</span>
      </p>
    </header>
    <p
      v-if="!conflicts.length && !unscheduledOperations.length"
      class="text-sm text-muted-foreground"
    >
      方案快照未记录冲突或不可排原因。
    </p>
    <p
      v-for="(conflict, index) in blocking"
      :key="conflict.conflictId ?? index"
      class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm break-all"
    >
      {{ conflictText(conflict) }}
      <span v-if="conflictIdentity(conflict)" class="block">{{ conflictIdentity(conflict) }}</span>
    </p>
    <p
      v-for="(item, index) in unscheduledOperations"
      :key="index"
      class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm break-all"
    >
      {{ unscheduledText(item) }}
    </p>
    <details
      v-for="[label, conflictsInGroup] in notices"
      :key="label"
      class="rounded-md border border-warning/30 p-3 text-sm"
    >
      <summary class="cursor-pointer font-medium">
        {{ label }} · {{ conflictsInGroup.length }} 条（展开明细）
      </summary>
      <ul class="mt-3 grid gap-2">
        <li
          v-for="(conflict, index) in conflictsInGroup"
          :key="conflict.conflictId ?? index"
          class="rounded-md bg-warning/10 p-3 break-all"
        >
          <p v-if="conflictIdentity(conflict)">{{ conflictIdentity(conflict) }}</p>
          <p>{{ conflictText(conflict) }}</p>
        </li>
      </ul>
    </details>
  </section>
</template>
