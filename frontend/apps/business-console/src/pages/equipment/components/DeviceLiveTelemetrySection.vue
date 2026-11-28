<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NvLineChart } from '@nerv-iip/ui'
import { formatTelemetryUnit } from '@/data/businessLabels'
import {
  useBusinessTelemetryAlarmRules,
  useBusinessTelemetryHistory,
  useBusinessTelemetryTags,
} from '@/composables/equipment/useBusinessTelemetry'
import {
  formatTelemetryDateTime,
  projectTelemetryHistory,
} from '@/components/equipment/telemetryHistoryPresentation'
import {
  exceedsTelemetryThreshold,
  telemetryReferenceLines,
} from '@/components/equipment/telemetryThresholds'

const props = defineProps<{ deviceAssetId: string }>()
const points = useBusinessTelemetryTags(
  { deviceAssetId: props.deviceAssetId, take: 500 },
  3_000,
  () => !!props.deviceAssetId,
)
const rules = useBusinessTelemetryAlarmRules(
  { deviceAssetId: props.deviceAssetId, isEnabled: 'enabled', take: 500 },
  () => !!props.deviceAssetId,
)
const { filters, historyError, historyPending, visibleHistoryItems } = useBusinessTelemetryHistory(
  { deviceAssetId: props.deviceAssetId },
  3_000,
  15,
)
watch(
  () => props.deviceAssetId,
  (id) => {
    points.filters.deviceAssetId = id
    rules.filters.deviceAssetId = id
    filters.deviceAssetId = id
    filters.tagKey = ''
  },
)

const chosen = computed(() => points.tags.value.find((tag) => tag.tagKey === filters.tagKey))
watch(
  points.tags,
  (tags) => {
    if (!chosen.value)
      filters.tagKey = tags.find((tag) => tag.latestValue != null)?.tagKey ?? tags[0]?.tagKey ?? ''
  },
  { immediate: true },
)
const projection = computed(() => projectTelemetryHistory(visibleHistoryItems.value))
const referenceLines = computed(() =>
  telemetryReferenceLines(rules.alarmRules.value, filters.tagKey),
)
const series = computed(() => [
  { key: 'value', label: chosen.value?.displayName || filters.tagKey },
])
const seriesUnit = computed(() => formatTelemetryUnit(chosen.value?.unitCode, ''))
const expanded = ref(false)
const shownPoints = computed(() =>
  expanded.value ? points.tags.value : points.tags.value.slice(0, 12),
)
</script>

<template>
  <section class="grid gap-4 rounded-lg border bg-card p-4" aria-labelledby="device-live-title">
    <h2 id="device-live-title" class="text-base font-semibold">实时测点与曲线</h2>
    <p v-if="points.tagsError.value" role="alert" class="text-sm text-destructive">
      实时测点读取失败。
    </p>
    <p v-else-if="points.tagsPending.value" role="status" class="text-sm text-muted-foreground">
      正在读取实时测点…
    </p>
    <p v-else-if="!points.tags.value.length" class="text-sm text-muted-foreground">
      该设备尚未配置测点。
    </p>
    <div v-else class="grid gap-4 lg:grid-cols-[minmax(240px,1fr)_minmax(0,2fr)]">
      <div class="grid content-start grid-cols-2 gap-2">
        <button
          v-for="point in shownPoints"
          :key="point.tagKey"
          type="button"
          :aria-pressed="filters.tagKey === point.tagKey"
          class="grid gap-1 rounded-md border p-2 text-left text-sm hover:bg-muted aria-pressed:border-brand"
          @click="filters.tagKey = point.tagKey ?? ''"
        >
          <span>{{ point.displayName || point.tagKey }}</span>
          <strong
            class="tabular-nums"
            :class="{
              'text-destructive':
                point.latestValue != null &&
                rules.alarmRules.value.some(
                  (rule) =>
                    rule.tagKey === point.tagKey &&
                    exceedsTelemetryThreshold(point.latestValue!, rule),
                ),
            }"
            >{{
              point.latestValue == null
                ? '无采样'
                : `${point.latestValue.toLocaleString(undefined, { maximumFractionDigits: 3 })} ${formatTelemetryUnit(point.unitCode, '')}`
            }}</strong
          >
          <span class="text-xs text-muted-foreground">{{
            formatTelemetryDateTime(point.latestSampledAtUtc)
          }}</span>
        </button>
        <button
          v-if="points.tags.value.length > 12"
          type="button"
          class="col-span-2 text-sm text-brand"
          @click="expanded = !expanded"
        >
          {{ expanded ? '收起测点' : '查看全部测点' }}
        </button>
      </div>
      <div class="min-w-0">
        <h3 class="mb-2 text-sm font-semibold">
          {{ chosen?.displayName || filters.tagKey }} · 近 15 分钟
        </h3>
        <p v-if="rules.alarmRulesError.value" role="alert" class="text-sm text-destructive">
          告警阈值读取失败。
        </p>
        <p v-if="historyError" role="alert" class="text-sm text-destructive">实时曲线读取失败。</p>
        <NvLineChart
          v-else-if="projection.chartData.length"
          :data="projection.chartData"
          x-key="time"
          :series="series"
          :reference-lines="referenceLines"
          :value-suffix="seriesUnit"
        />
        <p v-else class="text-sm text-muted-foreground">
          {{ historyPending ? '正在读取曲线…' : '近 15 分钟没有可绘制的数值样本。' }}
        </p>
      </div>
    </div>
  </section>
</template>
