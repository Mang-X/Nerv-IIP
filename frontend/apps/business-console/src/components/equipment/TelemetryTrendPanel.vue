<script setup lang="ts">
import type { LineSeries } from '@nerv-iip/ui'
import type { TelemetryHistoryProjection } from './telemetryHistoryPresentation'
import { formatTelemetryDateTime } from './telemetryHistoryPresentation'
import { NvLineChart, NvSectionCard } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  projection: TelemetryHistoryProjection
  tagKey: string
}>()

const hasSelectedTag = computed(() => props.tagKey.trim().length > 0)
const basisLabels = computed(() => {
  const basis = props.projection.statistics?.basis ?? 'sample'
  if (basis === 'hourly') {
    return {
      chart: '小时均值',
      count: '汇总点数',
      last: '最后汇总时间',
      latest: '最新小时均值',
      maximum: '最高小时均值',
      minimum: '最低小时均值',
    }
  }
  if (basis === 'daily') {
    return {
      chart: '日均值',
      count: '汇总点数',
      last: '最后汇总时间',
      latest: '最新日均值',
      maximum: '最高日均值',
      minimum: '最低日均值',
    }
  }
  return {
    chart: '遥测值',
    count: '样本数',
    last: '最后采样时间',
    latest: '最新值',
    maximum: '最大值',
    minimum: '最小值',
  }
})
const chartSeries = computed<LineSeries[]>(() => [{ key: 'value', label: basisLabels.value.chart }])

function metricValue(value: number | undefined) {
  return value === undefined
    ? '无样本'
    : value.toLocaleString(undefined, { maximumFractionDigits: 6 })
}
</script>

<template>
  <section class="grid gap-3 rounded-lg border bg-card p-4" aria-labelledby="telemetry-trend-title">
    <div>
      <h2 id="telemetry-trend-title" class="text-base font-semibold text-foreground">遥测趋势</h2>
      <p class="mt-1 text-sm text-muted-foreground">
        数值点与下方原始明细来自同一设备、采集标签和时间窗口。
      </p>
    </div>

    <div
      v-if="!hasSelectedTag"
      class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
    >
      输入采集标签后查看单一时间序列；原始明细仍可用于核对设备窗口内的全部记录。
    </div>
    <div
      v-else-if="!projection.statistics"
      class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
      role="status"
    >
      当前采集标签没有可绘制的数值样本。状态和报警请在事件上下文中查看，原始值保留在明细表中。
    </div>
    <template v-else>
      <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <NvSectionCard
          :description="basisLabels.latest"
          :value="metricValue(projection.statistics.latest)"
        />
        <NvSectionCard
          :description="basisLabels.minimum"
          :value="metricValue(projection.statistics.minimum)"
        />
        <NvSectionCard
          :description="basisLabels.maximum"
          :value="metricValue(projection.statistics.maximum)"
        />
        <NvSectionCard :description="basisLabels.count" :value="projection.statistics.count" />
        <NvSectionCard
          :description="basisLabels.last"
          :value="formatTelemetryDateTime(projection.statistics.lastSampleAtUtc)"
        />
      </div>

      <p
        v-if="projection.nonNumericMeasurementCount"
        class="text-sm text-muted-foreground"
        role="status"
      >
        {{ projection.nonNumericMeasurementCount }}
        条非数值采样未进入图表，已保留在事件上下文和原始明细中。
      </p>
      <p
        v-if="projection.excludedAggregateCount"
        class="text-sm text-muted-foreground"
        role="status"
      >
        已排除
        {{ projection.excludedAggregateCount }} 条不同粒度的汇总记录，避免与当前序列重复统计。
      </p>
      <p
        v-if="projection.invalidTimestampCount"
        class="text-sm text-muted-foreground"
        role="status"
      >
        {{ projection.invalidTimestampCount }} 条时间无效的数值记录未进入趋势与统计。
      </p>
      <NvLineChart :data="projection.chartData" x-key="time" :series="chartSeries" :height="300" />
    </template>
  </section>
</template>
