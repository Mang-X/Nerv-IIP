<script setup lang="ts">
import type {
  SchedulingCandidate,
  SchedulingCandidateSet,
  SchedulingCandidateReasonCode,
} from '@nerv-iip/api-client'
import { NvButton, NvStatusBadge } from '@nerv-iip/ui'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{
  candidates?: SchedulingCandidateSet
  baselinePlanId?: string
  canManage: boolean
  pending?: boolean
  blockedReason?: string
}>()
const emit = defineEmits<{ preview: []; select: [candidate: SchedulingCandidate] }>()
const reasonLabels: Record<SchedulingCandidateReasonCode, string> = {
  resourceUnavailable: '设备停机或维护',
  operationDeviation: '工序执行发生偏差',
  newOperation: '新增工序',
  predecessorDependency: '前序工序影响后续工序',
  resourceCapacity: '同资源产能竞争导致顺延',
}
const explanationLabels: Record<string, string> = {
  'frozen-conflict': '冻结工序与当前约束冲突',
  'unquantified-delay': '执行偏差尚无确定时长，保留位置等待核实',
  'restore-prediction-expired': '预计恢复时间已过，设备仍未实际恢复',
  'new-operation': '新增工序未纳入本次局部右移，请在草稿中核实',
  duedate: '工序延期',
  capacity: '资源产能不足',
  calendar: '工作日历无可用时段',
  material: '物料尚未就绪',
  quality: '质量封锁',
  equipment: '设备不可用',
  noeligibleresource: '没有合格资源',
  outsidehorizon: '超出排程窗口',
  invalidlockedassignment: '锁定位置与当前约束冲突',
  predecessorunscheduled: '前序工序未排入',
  tooling: '工装不可用',
}
function explanation(code?: string) {
  return explanationLabels[code?.toLowerCase() ?? ''] ?? '工序需核实，请查看未排或冲突说明'
}
function percent(value?: number) {
  return value === undefined ? '—' : `${(value * 100).toFixed(1)}%`
}
function delta(value?: number, percentage = false) {
  return value === undefined
    ? '—'
    : `${value > 0 ? '+' : ''}${percentage ? (value * 100).toFixed(1) : value}${percentage ? ' 个百分点' : ''}`
}
</script>

<template>
  <section class="grid gap-4 rounded-lg border bg-card p-4" data-testid="scheduling-candidates">
    <header class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h2 class="text-base font-semibold">局部重排候选</h2>
        <p class="mt-1 text-sm text-muted-foreground">
          比较当前事实下的预览，选定后进入草稿确认发布。
        </p>
      </div>
      <NvButton
        data-testid="preview-candidates"
        variant="outline"
        :disabled="!canManage || pending || !baselinePlanId || Boolean(blockedReason)"
        :title="
          blockedReason ||
          (!canManage ? '当前账号没有排产管理权限' : !baselinePlanId ? '先生成或恢复基线方案' : '')
        "
        @click="emit('preview')"
      >
        {{ pending ? '正在核实候选…' : candidates ? '重预览候选' : '生成右移候选' }}
      </NvButton>
    </header>
    <p v-if="!candidates" class="text-sm text-muted-foreground">
      {{
        blockedReason ||
        (baselinePlanId
          ? '生成候选后，在这里比较六项指标和每道移动工序的原因。'
          : '先生成或恢复基线方案，再查看局部重排候选。')
      }}
    </p>
    <div
      v-else
      class="grid items-start gap-4"
      :class="candidates.candidates?.length === 1 ? '' : 'xl:grid-cols-2'"
    >
      <article
        v-for="candidate in candidates.candidates"
        :key="candidate.strategy"
        class="grid gap-4 rounded-md border p-4"
      >
        <header class="flex flex-wrap items-center justify-between gap-2">
          <div class="flex items-center gap-2">
            <h3 class="font-semibold">
              {{ candidate.strategy === 'rightShift' ? '原资源右移' : '合格资源转移' }}
            </h3>
            <NvStatusBadge label="预览" tone="neutral" />
          </div>
          <NvButton
            data-testid="select-candidate"
            :disabled="
              !canManage ||
              pending ||
              Boolean(blockedReason) ||
              candidates.baselinePlanId !== baselinePlanId
            "
            @click="emit('select', candidate)"
            >选定并进入草稿</NvButton
          >
        </header>
        <dl class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <div class="rounded-md bg-muted/40 p-3">
            <dt class="text-xs text-muted-foreground">可优化已排工序准交率</dt>
            <dd class="mt-1 font-semibold">
              {{ percent(candidate.kpis?.baselineOnTimeRate) }} →
              {{ percent(candidate.kpis?.candidateOnTimeRate) }}
            </dd>
            <dd class="text-xs text-muted-foreground">
              分母 {{ candidate.kpis?.baselineOnTimeDenominator }} →
              {{ candidate.kpis?.candidateOnTimeDenominator }} ·
              {{ delta(candidate.kpis?.onTimeRateChange, true) }}
            </dd>
          </div>
          <div class="rounded-md bg-muted/40 p-3">
            <dt class="text-xs text-muted-foreground">延期订单</dt>
            <dd class="mt-1 font-semibold">
              {{ candidate.kpis?.baselineLateOrderCount }} →
              {{ candidate.kpis?.candidateLateOrderCount }}
            </dd>
            <dd class="text-xs text-muted-foreground">
              变化 {{ delta(candidate.kpis?.lateOrderCountChange) }} 单
            </dd>
          </div>
          <div class="rounded-md bg-muted/40 p-3">
            <dt class="text-xs text-muted-foreground">移动工序</dt>
            <dd class="mt-1 font-semibold">{{ candidate.kpis?.movedOperationCount }} 道</dd>
          </div>
          <div class="rounded-md bg-muted/40 p-3">
            <dt class="text-xs text-muted-foreground">资源利用率</dt>
            <dd class="mt-1 font-semibold">
              {{ percent(candidate.kpis?.baselineResourceUtilization) }} →
              {{ percent(candidate.kpis?.candidateResourceUtilization) }}
            </dd>
            <dd class="text-xs text-muted-foreground">
              {{ delta(candidate.kpis?.resourceUtilizationChange, true) }}
            </dd>
          </div>
          <div class="rounded-md bg-muted/40 p-3">
            <dt class="text-xs text-muted-foreground">未排工序</dt>
            <dd class="mt-1 font-semibold">
              {{ candidate.kpis?.baselineUnscheduledCount }} →
              {{ candidate.kpis?.candidateUnscheduledCount }}
            </dd>
            <dd class="text-xs text-muted-foreground">
              变化 {{ delta(candidate.kpis?.unscheduledCountChange) }} 道
            </dd>
          </div>
          <div class="rounded-md bg-muted/40 p-3">
            <dt class="text-xs text-muted-foreground">锁定保持</dt>
            <dd class="mt-1 font-semibold">
              {{ candidate.kpis?.preservedLockedCount }} /
              {{ candidate.kpis?.totalLockedCount }} 道保持
            </dd>
          </div>
        </dl>
        <details v-if="candidate.kpis?.lockedAssignments?.length" class="text-sm">
          <summary class="cursor-pointer font-medium">逐项锁定保持</summary>
          <p
            v-for="item in candidate.kpis.lockedAssignments"
            :key="`${item.original?.orderId}:${item.original?.operationId}`"
            class="mt-2"
            :class="item.preserved ? 'text-muted-foreground' : 'text-destructive'"
          >
            {{ item.original?.orderId }} · {{ item.original?.operationId }} ·
            {{ item.preserved ? '原资源、时间与分段保持' : '未保持，需核实' }}
            <span v-if="!item.preserved">
              · {{ item.original?.resourceId }} → {{ item.candidate?.resourceId ?? '未排' }} ·
              {{ formatDateTime(item.original?.startUtc) }} →
              {{ formatDateTime(item.candidate?.startUtc) }}</span
            >
          </p>
        </details>
        <section class="grid gap-3">
          <h4 class="text-sm font-semibold">移动原因与传播路径</h4>
          <p v-if="!candidate.movements?.length" class="text-sm text-muted-foreground">
            当前候选没有移动工序。
          </p>
          <article
            v-for="movement in candidate.movements"
            :key="`${movement.original?.orderId}:${movement.original?.operationId}`"
            class="grid gap-2 rounded-md border p-3 text-sm"
          >
            <h5 class="font-medium">
              {{ movement.original?.orderId }} · {{ movement.original?.operationId }}
            </h5>
            <div class="grid gap-2 sm:grid-cols-2">
              <div>
                <p class="text-xs text-muted-foreground">
                  原安排 · {{ movement.original?.resourceId }}
                </p>
                <p>
                  {{ formatDateTime(movement.original?.startUtc) }} —
                  {{ formatDateTime(movement.original?.endUtc) }}
                </p>
                <p
                  v-for="segment in movement.original?.segments"
                  :key="segment.startUtc"
                  class="text-xs text-muted-foreground"
                >
                  分段 {{ formatDateTime(segment.startUtc) }} — {{ formatDateTime(segment.endUtc) }}
                </p>
              </div>
              <div>
                <p class="text-xs text-muted-foreground">
                  候选安排 · {{ movement.candidate?.resourceId }}
                </p>
                <p>
                  {{ formatDateTime(movement.candidate?.startUtc) }} —
                  {{ formatDateTime(movement.candidate?.endUtc) }}
                </p>
                <p
                  v-for="segment in movement.candidate?.segments"
                  :key="segment.startUtc"
                  class="text-xs text-muted-foreground"
                >
                  分段 {{ formatDateTime(segment.startUtc) }} — {{ formatDateTime(segment.endUtc) }}
                </p>
              </div>
            </div>
            <p v-for="(reason, index) in movement.reasons" :key="index">
              {{ reason.code ? reasonLabels[reason.code] : '' }} · 来源
              {{ reason.source?.sourceReference }} ·
              {{ formatDateTime(reason.source?.occurredAtUtc) }}
            </p>
            <div
              v-for="(path, index) in movement.paths"
              :key="index"
              class="text-xs text-muted-foreground"
            >
              <p>从 {{ path.root?.orderId }} · {{ path.root?.operationId }} 传播</p>
              <p v-for="(step, i) in path.steps" :key="i">
                {{ step.from?.orderId }} · {{ step.from?.operationId }} → {{ step.to?.orderId }} ·
                {{ step.to?.operationId }}：{{ step.code ? reasonLabels[step.code] : '' }}
              </p>
            </div>
          </article>
        </section>
        <section
          v-if="candidate.explanations?.length || candidate.plan?.unscheduledOperations?.length"
          class="grid gap-2 text-sm"
        >
          <h4 class="font-semibold">未排与约束说明</h4>
          <p v-for="(item, index) in candidate.explanations" :key="index">
            {{ item.orderId }} · {{ item.operationId }}：{{ explanation(item.code) }}
          </p>
          <p
            v-for="item in candidate.plan?.unscheduledOperations"
            :key="`${item.orderId}:${item.operationId}`"
          >
            {{ item.orderId }} · {{ item.operationId }}：{{ item.message }}
          </p>
        </section>
      </article>
    </div>
  </section>
</template>
