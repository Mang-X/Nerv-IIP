<script setup lang="ts">
import type {
  BusinessConsoleEquipmentHealthLevel,
  BusinessConsoleEquipmentHealthResponse,
  BusinessConsoleEquipmentHealthRuleEvaluation,
  BusinessConsoleEquipmentHealthRuleStatus,
  BusinessConsoleEquipmentHealthFreshness,
} from '@nerv-iip/api-client'
import { NvBadge } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  health?: BusinessConsoleEquipmentHealthResponse
  pending: boolean
  error?: string | null
}>()

const ruleOrder = [
  'threshold-proximity',
  'runtime-hours-24h',
  'alarm-frequency-24h',
  'sustained-exceedance',
  'trend-growth',
] as const

const ruleLabels: Record<(typeof ruleOrder)[number], string> = {
  'threshold-proximity': '阈值接近度',
  'runtime-hours-24h': '近24小时生产运行时长',
  'alarm-frequency-24h': '近24小时报警频次',
  'sustained-exceedance': '持续超限',
  'trend-growth': '趋势恶化',
}

const levelPresentation: Record<
  BusinessConsoleEquipmentHealthLevel,
  { label: string; variant: 'success' | 'warning' | 'danger' }
> = {
  healthy: { label: '健康', variant: 'success' },
  watch: { label: '关注', variant: 'warning' },
  warning: { label: '预警', variant: 'warning' },
  critical: { label: '严重', variant: 'danger' },
}

const freshnessPresentation: Record<
  BusinessConsoleEquipmentHealthFreshness,
  { label: string; variant: 'success' | 'warning' | 'danger' | 'neutral' }
> = {
  fresh: { label: '实时', variant: 'success' },
  delayed: { label: '延迟', variant: 'warning' },
  stale: { label: '陈旧', variant: 'danger' },
  unavailable: { label: '暂无数据', variant: 'neutral' },
}

const statusPresentation: Record<
  BusinessConsoleEquipmentHealthRuleStatus,
  { label: string; variant: 'success' | 'warning' | 'danger' }
> = {
  normal: { label: '正常', variant: 'success' },
  risk: { label: '风险', variant: 'danger' },
  accumulating: { label: '历史数据积累中', variant: 'warning' },
}

const orderedEvaluations = computed(() => {
  const evaluations = props.health?.ruleEvaluations ?? []
  const byCode = new Map(evaluations.map((evaluation) => [evaluation.ruleCode, evaluation]))
  return ruleOrder
    .map((ruleCode) => byCode.get(ruleCode))
    .filter((evaluation): evaluation is BusinessConsoleEquipmentHealthRuleEvaluation =>
      Boolean(evaluation),
    )
})

const level = computed(() => (props.health ? levelPresentation[props.health.level] : undefined))
const freshness = computed(() =>
  props.health ? freshnessPresentation[props.health.dataFreshness.status] : undefined,
)
const triggeredCount = computed(() => props.health?.riskFactors.length ?? 0)

function formatDateTime(value?: string | null) {
  if (!value) return '暂无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? '暂无' : date.toLocaleString('zh-CN', { hour12: false })
}

function ruleLabel(evaluation: BusinessConsoleEquipmentHealthRuleEvaluation) {
  return ruleLabels[evaluation.ruleCode as keyof typeof ruleLabels] ?? evaluation.ruleName
}

function statusFor(status: BusinessConsoleEquipmentHealthRuleStatus) {
  return statusPresentation[status]
}
</script>

<template>
  <section class="rounded-lg border bg-card" aria-labelledby="equipment-health-title">
    <div class="flex flex-wrap items-start justify-between gap-3 border-b px-4 py-3">
      <div>
        <h2 id="equipment-health-title" class="text-sm font-semibold text-foreground">设备健康</h2>
        <p class="mt-1 text-xs text-muted-foreground">五项规则共同解释当前评分</p>
      </div>
      <span v-if="pending && health" class="text-xs text-muted-foreground" aria-live="polite">
        正在刷新
      </span>
    </div>

    <p v-if="error" class="mx-4 mt-4 text-sm text-destructive" role="alert">{{ error }}</p>

    <div v-if="!health && pending" class="p-6 text-sm text-muted-foreground" aria-live="polite">
      正在读取设备健康…
    </div>
    <div v-else-if="!health" class="p-6 text-sm text-muted-foreground">暂无设备健康数据。</div>

    <template v-else>
      <div class="grid gap-4 p-4 sm:grid-cols-[minmax(0,160px)_minmax(0,1fr)]">
        <div class="rounded-lg bg-muted/40 p-4">
          <p class="text-xs text-muted-foreground">健康评分</p>
          <p class="mt-1 text-4xl font-semibold tabular-nums text-foreground">
            {{ health.healthScore }}
          </p>
          <div class="mt-3 flex flex-wrap gap-2">
            <NvBadge v-if="level" class="rounded-sm" :variant="level.variant">{{
              level.label
            }}</NvBadge>
            <NvBadge v-if="freshness" class="rounded-sm" :variant="freshness.variant">{{
              freshness.label
            }}</NvBadge>
          </div>
          <p class="mt-3 text-xs text-muted-foreground">命中 {{ triggeredCount }} 项风险</p>
        </div>

        <dl class="grid content-start gap-2 text-sm">
          <div class="grid grid-cols-[6rem_minmax(0,1fr)] gap-2">
            <dt class="text-muted-foreground">计算时间</dt>
            <dd class="text-foreground">{{ formatDateTime(health.calculatedAtUtc) }}</dd>
          </div>
          <div class="grid grid-cols-[6rem_minmax(0,1fr)] gap-2">
            <dt class="text-muted-foreground">最新依据</dt>
            <dd class="text-foreground">
              {{ health.dataFreshness.sourceFactLabel || '暂无可追溯依据' }}
            </dd>
          </div>
          <div class="grid grid-cols-[6rem_minmax(0,1fr)] gap-2">
            <dt class="text-muted-foreground">依据时间</dt>
            <dd class="text-foreground">
              {{ formatDateTime(health.dataFreshness.latestFactAtUtc) }}
            </dd>
          </div>
        </dl>
      </div>

      <div class="grid gap-3 border-t p-4">
        <article
          v-for="evaluation in orderedEvaluations"
          :key="evaluation.ruleCode"
          class="grid gap-3 rounded-lg border p-3"
        >
          <div class="flex flex-wrap items-center justify-between gap-2">
            <h3 class="text-sm font-semibold text-foreground">{{ ruleLabel(evaluation) }}</h3>
            <NvBadge class="rounded-sm" :variant="statusFor(evaluation.status).variant">
              {{ statusFor(evaluation.status).label }}
            </NvBadge>
          </div>

          <dl class="grid gap-2 text-xs sm:grid-cols-2">
            <div>
              <dt class="text-muted-foreground">当前值</dt>
              <dd class="mt-0.5 font-medium text-foreground">
                {{ evaluation.currentValue }} {{ evaluation.unit }}
              </dd>
            </div>
            <div>
              <dt class="text-muted-foreground">判定阈值</dt>
              <dd class="mt-0.5 font-medium text-foreground">
                {{ evaluation.threshold }} {{ evaluation.unit }}
              </dd>
            </div>
          </dl>

          <p class="text-xs text-foreground">{{ evaluation.evidence }}</p>
          <p
            v-if="evaluation.sourceFactLabel || evaluation.sourceFactOccurredAtUtc"
            class="text-xs text-muted-foreground"
          >
            {{ evaluation.sourceFactLabel || '来源事实' }} ·
            {{ formatDateTime(evaluation.sourceFactOccurredAtUtc) }}
          </p>
        </article>
      </div>
    </template>
  </section>
</template>
