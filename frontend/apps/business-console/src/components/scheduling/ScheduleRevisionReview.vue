<script setup lang="ts">
import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingPlanRevision,
} from '@nerv-iip/api-client'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import { ChangeSummaryPanel, toModel } from '@nerv-iip/scheduling'
import { computed } from 'vue'
import { NvStatusBadge } from '@nerv-iip/ui'

const props = defineProps<{
  revision?: BusinessConsoleSchedulingPlanRevision
  basePlan?: BusinessConsoleSchedulePlan
}>()
const candidateModel = computed(() => toModel(props.revision?.candidate ?? {}))
const baseModel = computed(() => toModel(props.basePlan ?? {}))

const comparisonChanges = computed(() =>
  candidateModel.value.changes.map((change) => {
    const before = baseModel.value.tasks.find(
      (task) => task.orderId === change.orderId && task.operationId === change.operationId,
    )
    const after = candidateModel.value.tasks.find(
      (task) => task.orderId === change.orderId && task.operationId === change.operationId,
    )
    if (!props.basePlan || !after) return change
    const sameTime = (left: string, right: string) => Date.parse(left) === Date.parse(right)
    const unchanged =
      before &&
      before.resourceId === after.resourceId &&
      sameTime(before.startUtc, after.startUtc) &&
      sameTime(before.endUtc, after.endUtc) &&
      (before.segments ?? []).length === (after.segments ?? []).length &&
      (before.segments ?? []).every((segment, index) => {
        const next = after.segments![index]!
        return sameTime(segment.startUtc, next.startUtc) && sameTime(segment.endUtc, next.endUtc)
      })
    const changeType = !before ? 'added' : unchanged ? 'preserved' : 'moved'
    const message = !before
      ? change.message
      : unchanged
        ? '资源、起止时间与分段保持不变'
        : change.changeType === 'moved'
          ? change.message
          : after.locked
            ? '相对基线已调整资源或排程时间，按调整后的位置锁定'
            : '资源或排程时间已调整'
    return { ...change, changeType: changeType as typeof change.changeType, message }
  }),
)

function percent(value?: number) {
  return value === undefined ? '—' : `${Math.round(value * 100)}%`
}
</script>

<template>
  <section
    v-if="revision"
    class="grid gap-4 rounded-lg border bg-card p-4"
    data-testid="schedule-revision-review"
  >
    <header class="flex flex-wrap items-center gap-2">
      <h2 class="font-semibold">影响分析与方案对比</h2>
      <NvStatusBadge v-if="revision.impact?.isInvalidated" label="基线已失效" tone="warning" />
      <NvStatusBadge v-else label="基线有效" tone="success" />
    </header>
    <p v-if="revision.impact?.isInvalidated" class="text-sm text-muted-foreground">
      {{ describeScheduleInvalidationReason(revision.impact.reasonCode) }} · 影响
      {{ revision.impact.affectedWorkOrderIds?.length ?? 0 }} 个工单、{{
        revision.impact.affectedOperationIds?.length ?? 0
      }}
      道工序
    </p>
    <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
      <div class="rounded-md border p-3">
        <p class="text-xs text-muted-foreground">准时率</p>
        <p class="font-semibold">
          {{ percent(revision.comparison?.baseMetrics?.onTimeRate) }} →
          {{ percent(revision.comparison?.candidateMetrics?.onTimeRate) }}
        </p>
      </div>
      <div class="rounded-md border p-3">
        <p class="text-xs text-muted-foreground">总延期</p>
        <p class="font-semibold">
          {{ revision.comparison?.baseMetrics?.totalTardinessMinutes ?? 0 }} →
          {{ revision.comparison?.candidateMetrics?.totalTardinessMinutes ?? 0 }} 分钟
        </p>
      </div>
      <div class="rounded-md border p-3">
        <p class="text-xs text-muted-foreground">平均利用率</p>
        <p class="font-semibold">
          {{ percent(revision.comparison?.baseMetrics?.averageResourceUtilization) }} →
          {{ percent(revision.comparison?.candidateMetrics?.averageResourceUtilization) }}
        </p>
      </div>
      <div class="rounded-md border p-3">
        <p class="text-xs text-muted-foreground">移动 / 锁定</p>
        <p class="font-semibold">
          {{ revision.comparison?.movedOperationCount ?? 0 }} /
          {{ revision.comparison?.lockedOperationCount ?? 0 }}
        </p>
      </div>
      <div class="rounded-md border p-3">
        <p class="text-xs text-muted-foreground">未排工序</p>
        <p class="font-semibold">{{ revision.comparison?.unscheduledOperationCount ?? 0 }}</p>
      </div>
    </div>
    <ChangeSummaryPanel
      :key="revision.candidate?.planId"
      :changes="comparisonChanges"
      :tasks="candidateModel.tasks"
      :base-tasks="baseModel.tasks"
    />
  </section>
</template>
