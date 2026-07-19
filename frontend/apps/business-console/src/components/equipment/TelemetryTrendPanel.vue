<script setup lang="ts">
import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import type { LineSeries } from '@nerv-iip/ui'
import { projectTelemetryHistory } from '@/pages/equipment/telemetry/telemetryHistoryPresentation'
import { NvLineChart, NvSectionCard } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  items: BusinessConsoleTelemetryHistoryItem[]
  tagKey: string
}>()

const projection = computed(() => projectTelemetryHistory(props.items))
const chartSeries: LineSeries[] = [{ key: 'value', label: '遥测值' }]
const hasSelectedTag = computed(() => props.tagKey.trim().length > 0)

function metricValue(value: number | undefined) {
  return value === undefined
    ? '无样本'
    : value.toLocaleString(undefined, { maximumFractionDigits: 6 })
}

function formatDateTime(value?: string) {
  if (!value) return '无样本'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN')
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
        <NvSectionCard description="最新值" :value="metricValue(projection.statistics.latest)" />
        <NvSectionCard description="最小值" :value="metricValue(projection.statistics.minimum)" />
        <NvSectionCard description="最大值" :value="metricValue(projection.statistics.maximum)" />
        <NvSectionCard description="样本数" :value="projection.statistics.count" />
        <NvSectionCard
          description="最后采样时间"
          :value="formatDateTime(projection.statistics.lastSampleAtUtc)"
        />
      </div>

      <p
        v-if="projection.nonNumericMeasurementCount"
        class="text-sm text-muted-foreground"
        role="status"
      >
        {{ projection.nonNumericMeasurementCount }} 条非数值采样未进入图表，原始值仍保留在明细表中。
      </p>
      <NvLineChart :data="projection.chartData" x-key="time" :series="chartSeries" :height="300" />
    </template>
  </section>
</template>
