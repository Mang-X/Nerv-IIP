<script setup lang="ts">
import type { BusinessConsoleQualitySpcControlChartResponse } from '@nerv-iip/api-client'
import type { LineSeries } from '@nerv-iip/ui'
import { NvLineChart } from '@nerv-iip/ui'
import { AlertTriangleIcon, ChartNoAxesCombinedIcon, LoaderCircleIcon } from '@lucide/vue'
import { computed } from 'vue'
import { buildSpcChartPresentation } from '@/composables/useBusinessQualityAnalysis'

const props = defineProps<{
  chart: BusinessConsoleQualitySpcControlChartResponse | null
  pending: boolean
  warmup: boolean
  errorMessage: string
}>()

const presentation = computed(() =>
  props.chart
    ? buildSpcChartPresentation(props.chart)
    : { xbarRows: [], rangeRows: [], violationMarkers: [] },
)
const hasSubgroups = computed(() => (props.chart?.subgroups?.length ?? 0) > 0)
const hasControlLimits = computed(
  () => presentation.value.xbarRows.length > 0 && presentation.value.rangeRows.length > 0,
)

const xbarSeries: LineSeries[] = [
  { key: 'xbar', label: 'Xbar', color: 'var(--chart-1)' },
  { key: 'centerLine', label: '中心线', color: 'var(--chart-2)' },
  { key: 'ucl', label: 'UCL', color: 'var(--destructive)' },
  { key: 'lcl', label: 'LCL', color: 'var(--chart-4)' },
]
const rangeSeries: LineSeries[] = [
  { key: 'range', label: 'Range', color: 'var(--chart-1)' },
  { key: 'centerLine', label: '中心线', color: 'var(--chart-2)' },
  { key: 'ucl', label: 'UCL', color: 'var(--destructive)' },
  { key: 'lcl', label: 'LCL', color: 'var(--chart-4)' },
]
</script>

<template>
  <section aria-labelledby="spc-control-chart-title" class="grid gap-3">
    <div class="flex flex-wrap items-end justify-between gap-2">
      <div>
        <h2 id="spc-control-chart-title" class="text-base font-semibold">SPC Xbar / R 控制图</h2>
        <p class="text-sm text-muted-foreground">子组值与当前查询范围的控制限同图核对。</p>
      </div>
    </div>

    <div
      v-if="pending"
      class="flex min-h-48 items-center justify-center gap-2 rounded-xl border bg-card text-sm text-muted-foreground"
      role="status"
    >
      <LoaderCircleIcon class="size-4 animate-spin" aria-hidden="true" />
      正在加载 SPC 控制图
    </div>
    <div
      v-else-if="errorMessage"
      class="flex min-h-48 items-center justify-center rounded-xl border border-destructive/40 bg-destructive/5 p-6 text-sm text-destructive"
      role="alert"
    >
      {{ errorMessage }}
    </div>
    <div
      v-else-if="warmup"
      class="flex min-h-48 items-center justify-center gap-2 rounded-xl border bg-card p-6 text-sm text-muted-foreground"
    >
      <ChartNoAxesCombinedIcon class="size-5" aria-hidden="true" />
      至少形成一个完整子组后显示控制图。
    </div>
    <div
      v-else-if="!hasSubgroups"
      class="flex min-h-48 items-center justify-center rounded-xl border bg-card p-6 text-sm text-muted-foreground"
    >
      当前范围没有完整子组。
    </div>
    <div
      v-else-if="!hasControlLimits"
      class="flex min-h-48 items-center justify-center rounded-xl border bg-card p-6 text-sm text-muted-foreground"
    >
      当前范围尚无完整控制限，暂不绘制可能误导的控制图。
    </div>

    <div v-else class="grid gap-3 xl:grid-cols-2">
      <article class="rounded-xl border bg-card p-4 shadow-sm">
        <div class="mb-2 flex items-center justify-between gap-2">
          <h3 class="font-semibold">Xbar 控制图</h3>
          <span class="text-xs text-muted-foreground">均值与控制限</span>
        </div>
        <NvLineChart
          :data="presentation.xbarRows"
          x-key="subgroup"
          :series="xbarSeries"
          :height="210"
        />
        <div
          v-if="presentation.violationMarkers.length"
          class="mt-2 flex flex-wrap items-center gap-2 border-t pt-3"
          aria-label="判异子组"
        >
          <span class="text-xs font-medium text-muted-foreground">判异定位</span>
          <a
            v-for="marker in presentation.violationMarkers"
            :key="marker.key"
            data-testid="spc-violation-marker"
            :href="`#${marker.targetId}`"
            class="inline-flex items-center gap-1 rounded-full border border-destructive/40 bg-destructive/10 px-2 py-1 text-xs font-medium text-destructive"
            :title="marker.message"
          >
            <AlertTriangleIcon class="size-3.5" aria-hidden="true" />
            判异{{ marker.label }}
          </a>
        </div>
      </article>

      <article class="rounded-xl border bg-card p-4 shadow-sm">
        <div class="mb-2 flex items-center justify-between gap-2">
          <h3 class="font-semibold">R 控制图</h3>
          <span class="text-xs text-muted-foreground">极差与控制限</span>
        </div>
        <NvLineChart
          :data="presentation.rangeRows"
          x-key="subgroup"
          :series="rangeSeries"
          :height="210"
        />
      </article>
    </div>
  </section>
</template>
