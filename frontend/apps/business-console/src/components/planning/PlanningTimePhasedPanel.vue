<script setup lang="ts">
import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleMpsBucketItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { BarSeries } from '@nerv-iip/ui'
import {
  NvBarChart,
  NvButton,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
} from '@nerv-iip/ui'
import { ChartNoAxesCombinedIcon, LoaderCircleIcon } from '@lucide/vue'
import { computed, shallowRef } from 'vue'
import {
  buildCoverageRows,
  buildTimePhasedRows,
  phasedUomCodes,
  topDemandSkuCodes,
  type PlanningGranularity,
} from './planningAggregation'

/**
 * MRP 时段视图：毛需求 / MPS 主计划 / 建议补充三条序列按期间（周/月）对齐，
 * 外加需求覆盖的时段展开。默认聚合到 Top 5 需求物料，可切到全部或单一物料。
 * 建议序列只统计 suggestionRunId 指定的那一次运行（跨运行求和会重复计数）。
 */
const TOP_SKU_LIMIT = 5

const props = defineProps<{
  demands: readonly BusinessConsoleDemandSourceItem[]
  mpsBuckets: readonly BusinessConsoleMpsBucketItem[]
  suggestions: readonly BusinessConsolePlanningSuggestionItem[]
  /** 建议序列统计的运行；空串＝尚无运行，建议序列为 0。 */
  suggestionRunId: string
  /** 该运行的人读锚点（计划范围），用于脚注说明统计口径。 */
  suggestionRunLabel: string
  pending: boolean
  errorMessage: string
  skuLabel: (code?: string | null) => string
}>()

const granularity = shallowRef<PlanningGranularity>('week')
const skuScope = shallowRef<string>('top')

const topSkuCodes = computed(() => topDemandSkuCodes(props.demands, TOP_SKU_LIMIT))
const skuScopeOptions = computed(() => [
  { value: 'top', label: `Top ${TOP_SKU_LIMIT} 需求物料汇总` },
  { value: 'all', label: '全部物料汇总' },
  ...topSkuCodes.value.map((code) => ({
    value: code,
    label: `${props.skuLabel(code)} · ${code}`,
  })),
])
const scopeSet = computed<ReadonlySet<string> | null>(() => {
  if (skuScope.value === 'all') return null
  if (skuScope.value === 'top') return new Set(topSkuCodes.value)
  return new Set([skuScope.value])
})

const timePhasedRows = computed(() =>
  buildTimePhasedRows(
    props.demands,
    props.mpsBuckets,
    props.suggestions,
    granularity.value,
    scopeSet.value,
    props.suggestionRunId,
  ),
)
const coverageRows = computed(() =>
  buildCoverageRows(props.demands, props.suggestions, granularity.value, props.suggestionRunId),
)

// 混合计量单位提示：扫的是图上真正相加的三个来源（需求 + MPS + 该运行的供给建议），
// 只看需求池会漏掉组件采购建议（kg 等）混入 pcs 的情况。
const mixedUom = computed(
  () =>
    phasedUomCodes(
      props.demands,
      props.mpsBuckets,
      props.suggestions,
      scopeSet.value,
      props.suggestionRunId,
    ).size > 1,
)

const hasAnyData = computed(
  () => props.demands.length > 0 || props.mpsBuckets.length > 0 || props.suggestions.length > 0,
)

const timePhasedSeries: BarSeries[] = [
  { key: 'demand', label: '毛需求', color: 'var(--chart-1)' },
  { key: 'mps', label: 'MPS 主计划', color: 'var(--chart-2)' },
  { key: 'suggestion', label: '建议补充', color: 'var(--chart-3)' },
]
const coverageSeries: BarSeries[] = [
  { key: 'demandSkuCount', label: '需求物料数', color: 'var(--chart-2)' },
  { key: 'coveredSkuCount', label: '已生成建议', color: 'var(--chart-1)' },
]
</script>

<template>
  <section aria-labelledby="planning-time-phased-title" class="grid gap-3">
    <div class="flex flex-wrap items-end justify-between gap-2">
      <div>
        <h2 id="planning-time-phased-title" class="text-base font-semibold">MRP 时段视图</h2>
        <p class="text-sm text-muted-foreground">
          毛需求、MPS 主计划与建议补充按期间对齐核对，缺口一眼可见。
        </p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <NvSelect v-model="skuScope">
          <NvSelectTrigger class="h-9 w-56" aria-label="物料范围">
            <NvSelectValue placeholder="物料范围" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem v-for="o in skuScopeOptions" :key="o.value" :value="o.value">{{
              o.label
            }}</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <div class="flex items-center gap-1" role="group" aria-label="期间粒度">
          <NvButton
            size="sm"
            type="button"
            :variant="granularity === 'week' ? 'secondary' : 'ghost'"
            @click="granularity = 'week'"
          >
            按周
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            :variant="granularity === 'month' ? 'secondary' : 'ghost'"
            @click="granularity = 'month'"
          >
            按月
          </NvButton>
        </div>
      </div>
    </div>

    <div
      v-if="pending"
      class="flex min-h-48 items-center justify-center gap-2 rounded-xl border bg-card text-sm text-muted-foreground"
      role="status"
    >
      <LoaderCircleIcon class="size-4 animate-spin" aria-hidden="true" />
      正在加载计划数据
    </div>
    <div
      v-else-if="errorMessage"
      class="flex min-h-48 items-center justify-center rounded-xl border border-destructive/40 bg-destructive/5 p-6 text-sm text-destructive"
      role="alert"
    >
      {{ errorMessage }}
    </div>
    <div
      v-else-if="!hasAnyData"
      class="flex min-h-48 items-center justify-center gap-2 rounded-xl border bg-card p-6 text-sm text-muted-foreground"
    >
      <ChartNoAxesCombinedIcon class="size-5" aria-hidden="true" />
      当前范围还没有需求、主计划或建议，录入需求并运行 MRP 后展示时段对比。
    </div>

    <div v-else class="grid gap-3 xl:grid-cols-2">
      <article class="rounded-xl border bg-card p-4 shadow-sm">
        <div class="mb-2 flex items-center justify-between gap-2">
          <h3 class="font-semibold">需求 · 主计划 · 建议对比</h3>
          <span class="text-xs text-muted-foreground">数量按期间汇总</span>
        </div>
        <NvBarChart
          v-if="timePhasedRows.length"
          :data="timePhasedRows"
          x-key="period"
          :series="timePhasedSeries"
          :height="220"
        />
        <div v-else class="flex min-h-52 items-center justify-center text-sm text-muted-foreground">
          所选物料范围内没有带日期的计划数据。
        </div>
        <p v-if="timePhasedRows.length" class="mt-2 text-xs text-muted-foreground">
          建议序列＝{{ suggestionRunId ? suggestionRunLabel : '尚未运行 MRP，建议为 0' }} ·
          当前建议状态筛选（待评审 / 已接受），不跨运行求和。
        </p>
        <p v-if="mixedUom && timePhasedRows.length" class="mt-2 text-xs text-muted-foreground">
          汇总范围含多种计量单位，数量为直接相加，仅作趋势参考；切到单一物料查看精确数量。
        </p>
      </article>

      <article class="rounded-xl border bg-card p-4 shadow-sm">
        <div class="mb-2 flex items-center justify-between gap-2">
          <h3 class="font-semibold">需求覆盖时段展开</h3>
          <span class="text-xs text-muted-foreground">物料计数（全部物料）</span>
        </div>
        <!-- 覆盖口径与左图、与顶部 KPI 同源：同一次运行 + 有效供给建议（见 countsTowardCoverage）。 -->
        <NvBarChart
          v-if="coverageRows.length"
          :data="coverageRows"
          x-key="period"
          :series="coverageSeries"
          :height="220"
          value-suffix=" 个"
        />
        <div v-else class="flex min-h-52 items-center justify-center text-sm text-muted-foreground">
          需求池还没有带需求日期的物料。
        </div>
        <p v-if="coverageRows.length" class="mt-2 text-xs text-muted-foreground">
          覆盖口径与顶部覆盖率一致：{{
            suggestionRunId ? suggestionRunLabel : '尚未运行 MRP，覆盖为 0'
          }}，物料在任意期间出现未被拒绝的供给建议即视为已覆盖。
        </p>
      </article>
    </div>
  </section>
</template>
