<script setup lang="ts">
import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingPlanSummaryResponse,
} from '@nerv-iip/api-client'
import {
  ResourceSchedulerBoard,
  toModel,
  type ScheduleModel,
  type TimeScale,
} from '@nerv-iip/scheduling'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import { NvButton, NvStatusBadge, Spinner } from '@nerv-iip/ui'
import {
  CalendarDaysIcon,
  EyeIcon,
  LockIcon,
  SendIcon,
  ShieldAlertIcon,
  TimerIcon,
  TriangleAlertIcon,
} from '@lucide/vue'
import { computed, shallowRef } from 'vue'

const props = defineProps<{
  plan?: BusinessConsoleSchedulePlan
  summary?: BusinessConsoleSchedulingPlanSummaryResponse
  loading?: boolean
  error?: unknown
  releasePending?: boolean
}>()

const emit = defineEmits<{
  openDetail: []
  release: []
}>()

const scale = shallowRef<TimeScale>('auto')

const assignments = computed(() => props.plan?.assignments ?? [])
const invalidTimeAssignments = computed(() => assignments.value.filter(hasInvalidTime))
const missingResourceAssignments = computed(() =>
  assignments.value.filter(
    (assignment) =>
      !hasInvalidTime(assignment) && !assignment.resourceId && !assignment.workCenterId,
  ),
)
const renderableAssignments = computed(() =>
  assignments.value.filter(
    (assignment) =>
      !hasInvalidTime(assignment) && Boolean(assignment.resourceId || assignment.workCenterId),
  ),
)

const model = computed<ScheduleModel | undefined>(() => {
  if (!props.plan) return undefined
  const mapped = toModel({ ...props.plan, assignments: renderableAssignments.value })
  return {
    ...mapped,
    tasks: mapped.tasks.map((task) => {
      if (task.type !== 'operation') return task
      const sequence = task.operationSequence > 0 ? `第 ${task.operationSequence} 道` : '工序'
      return {
        ...task,
        text: [task.orderId, sequence, task.operationId].filter(Boolean).join(' · '),
      }
    }),
  }
})

const resourceCount = computed(() => model.value?.resources.length ?? 0)
const planRange = computed(() => {
  const horizon = model.value?.horizon
  if (!horizon?.startUtc || !horizon.endUtc) return '暂无有效时间范围'
  return `${formatDateTime(horizon.startUtc)} 至 ${formatDateTime(horizon.endUtc)}`
})
const invalidationReason = computed(() =>
  props.summary?.isInvalidated
    ? describeScheduleInvalidationReason(props.summary.latestInvalidationReasonCode)
    : '',
)
const releaseDisabled = computed(
  () =>
    props.releasePending ||
    props.summary?.isInvalidated ||
    ['released', 'superseded', 'revoked'].includes(
      props.plan?.status ?? props.summary?.status ?? '',
    ),
)
const feedback = computed(() => {
  if (isForbidden(props.error))
    return '权限不足，无法查看该排程方案。请联系管理员确认排程读取权限。'
  if (props.error) return '排程甘特加载失败，请稍后重试。'
  if (!props.plan) return '请选择一个排程方案查看甘特。'
  return ''
})

function hasInvalidTime(assignment: BusinessConsoleSchedulingAssignment) {
  const start = Date.parse(assignment.startUtc ?? '')
  const end = Date.parse(assignment.endUtc ?? '')
  return !Number.isFinite(start) || !Number.isFinite(end) || end <= start
}

function isForbidden(error: unknown, visited = new Set<object>()): boolean {
  if (!error || typeof error !== 'object') return false
  if (visited.has(error)) return false
  visited.add(error)
  const record = error as Record<string, unknown>
  if (
    record.status === 401 ||
    record.status === 403 ||
    record.statusCode === 401 ||
    record.statusCode === 403
  ) {
    return true
  }
  return isForbidden(record.response, visited) || isForbidden(record.cause, visited)
}

function formatDateTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

function statusLabel(status?: string | null) {
  if (status === 'preview') return '预览'
  if (status === 'generated') return '已生成'
  if (status === 'released') return '已发布'
  if (status === 'superseded') return '已取代'
  if (status === 'revoked') return '已撤销'
  return status ?? '未知'
}

function statusTone(status?: string | null): 'success' | 'warning' | 'neutral' {
  if (status === 'released') return 'success'
  if (status === 'generated') return 'warning'
  return 'neutral'
}
</script>

<template>
  <section class="grid gap-4" data-testid="scheduling-plan-gantt">
    <div
      v-if="loading"
      class="flex min-h-80 items-center justify-center gap-2 rounded-lg border bg-card text-sm text-muted-foreground"
    >
      <Spinner aria-hidden="true" />
      正在读取方案时间轴
    </div>

    <div
      v-else-if="feedback"
      class="flex min-h-64 flex-col items-center justify-center gap-3 rounded-lg border border-dashed bg-card p-8 text-center"
      :class="isForbidden(error) ? 'border-warning/40 bg-warning/5' : ''"
      role="status"
    >
      <ShieldAlertIcon
        v-if="isForbidden(error)"
        class="size-9 text-warning-strong"
        aria-hidden="true"
      />
      <CalendarDaysIcon v-else class="size-9 text-muted-foreground" aria-hidden="true" />
      <p class="max-w-xl text-sm text-muted-foreground">{{ feedback }}</p>
    </div>

    <template v-else-if="plan && model">
      <div
        v-if="summary?.isInvalidated"
        class="flex items-start gap-3 rounded-lg border border-warning/40 bg-warning/10 p-4 text-sm"
        role="alert"
      >
        <TriangleAlertIcon class="mt-0.5 size-4 flex-none text-warning-strong" aria-hidden="true" />
        <div>
          <p class="font-semibold text-foreground">方案已失效，不能从甘特发布</p>
          <p class="mt-1 text-muted-foreground">
            {{ invalidationReason }}。请重新排程并生成新方案后再发布。
          </p>
        </div>
      </div>

      <div
        class="grid gap-3 rounded-lg border bg-card p-4 xl:grid-cols-[minmax(0,1fr)_auto] xl:items-center"
      >
        <div class="min-w-0">
          <div class="flex flex-wrap items-center gap-2">
            <h2 class="truncate text-base font-semibold text-foreground">
              {{ plan.planId || '未命名方案' }}
            </h2>
            <NvStatusBadge :label="statusLabel(plan.status)" :tone="statusTone(plan.status)" />
            <NvStatusBadge v-if="summary?.isInvalidated" label="已失效" tone="warning" />
          </div>
          <p class="mt-1 text-sm text-muted-foreground">{{ planRange }}</p>
        </div>
        <div class="flex flex-wrap items-center gap-2">
          <div class="inline-flex rounded-md border bg-background p-1" aria-label="时间缩放">
            <NvButton
              size="sm"
              :variant="scale === 'auto' ? 'secondary' : 'ghost'"
              type="button"
              @click="scale = 'auto'"
            >
              自动适配
            </NvButton>
            <NvButton
              size="sm"
              :variant="scale === 'hour' ? 'secondary' : 'ghost'"
              type="button"
              @click="scale = 'hour'"
            >
              <TimerIcon aria-hidden="true" />班次级
            </NvButton>
            <NvButton
              size="sm"
              :variant="scale === 'day' ? 'secondary' : 'ghost'"
              type="button"
              @click="scale = 'day'"
            >
              <CalendarDaysIcon aria-hidden="true" />日级
            </NvButton>
          </div>
          <NvButton size="sm" variant="outline" type="button" @click="emit('openDetail')">
            <EyeIcon aria-hidden="true" />方案明细
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            :disabled="releaseDisabled"
            :title="summary?.isInvalidated ? `方案已失效（${invalidationReason}）` : '发布当前方案'"
            @click="emit('release')"
          >
            <Spinner v-if="releasePending" aria-hidden="true" />
            <SendIcon v-else aria-hidden="true" />
            发布当前方案
          </NvButton>
        </div>
      </div>

      <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">可视工序</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ renderableAssignments.length }}
          </p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">资源</p>
          <p class="mt-1 text-lg font-semibold text-foreground">{{ resourceCount }}</p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">冲突</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ plan.conflicts?.length ?? 0 }}
          </p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">未排工序</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ plan.unscheduledOperations?.length ?? 0 }}
          </p>
        </div>
        <div class="rounded-lg border bg-card p-3">
          <p class="text-xs text-muted-foreground">锁定分配</p>
          <p class="mt-1 text-lg font-semibold text-foreground">
            {{ renderableAssignments.filter((item) => item.isLocked).length }}
          </p>
        </div>
      </div>

      <div
        v-if="invalidTimeAssignments.length || missingResourceAssignments.length"
        class="flex flex-wrap gap-2 rounded-lg border border-warning/30 bg-warning/5 p-3"
        role="status"
      >
        <NvStatusBadge
          v-if="invalidTimeAssignments.length"
          :label="`${invalidTimeAssignments.length} 项时间异常`"
          tone="warning"
        />
        <NvStatusBadge
          v-if="missingResourceAssignments.length"
          :label="`${missingResourceAssignments.length} 项缺少资源`"
          tone="warning"
        />
        <span class="text-sm text-muted-foreground"
          >异常分配未绘制，请在方案明细核查后端返回值。</span
        >
      </div>

      <div class="h-[34rem] min-h-[28rem] overflow-hidden rounded-lg border bg-card p-2">
        <ResourceSchedulerBoard
          :model="model"
          :scale="scale"
          :read-only="true"
          @task-select="emit('openDetail')"
        />
      </div>

      <div
        class="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-lg border bg-card px-4 py-3 text-xs text-muted-foreground"
      >
        <span class="font-semibold text-foreground">状态说明</span>
        <span class="inline-flex items-center gap-1.5"
          ><span class="h-2.5 w-6 rounded-sm border border-primary bg-primary/15" />正常工序</span
        >
        <span class="inline-flex items-center gap-1.5"
          ><TriangleAlertIcon
            class="size-3.5 text-destructive"
            aria-hidden="true"
          />冲突（红色实线边框）</span
        >
        <span class="inline-flex items-center gap-1.5"
          ><LockIcon class="size-3.5" aria-hidden="true" />锁定（虚线边框）</span
        >
        <span>点击工序块打开方案明细；只读视图不支持拖拽或改派。</span>
      </div>

      <div
        v-if="plan.unscheduledOperations?.length"
        class="grid gap-2 rounded-lg border bg-card p-4"
      >
        <h3 class="text-sm font-semibold text-foreground">未排工序</h3>
        <p
          v-for="item in plan.unscheduledOperations"
          :key="`${item.orderId}:${item.operationId}`"
          class="rounded-md border border-warning/30 bg-warning/5 p-3 text-sm text-foreground"
        >
          {{ item.orderId || '未关联工单' }} · {{ item.operationId || '工序' }} ·
          {{ item.message || '未返回不可排说明' }}
        </p>
      </div>
    </template>
  </section>
</template>
