<script setup lang="ts">
import type {
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { BarSeries } from '@nerv-iip/ui'
import { NvBarChart } from '@nerv-iip/ui'
import { ChartColumnIcon, LoaderCircleIcon } from '@lucide/vue'
import { computed } from 'vue'
import { buildRunSuggestionRows } from './planningAggregation'

/**
 * 单次 MRP 运行的建议分布小图：按目标期间（周）× 建议类型计条数，
 * 与需求追溯 pegging 表并排，回答“这次运行的建议落在哪里、是什么类型”。
 */
const props = defineProps<{
  run: BusinessConsoleMrpRunItem | null
  suggestions: readonly BusinessConsolePlanningSuggestionItem[]
  pending: boolean
}>()

const rows = computed(() =>
  props.run?.runId ? buildRunSuggestionRows(props.suggestions, props.run.runId, 'week') : [],
)
const totalCount = computed(() =>
  rows.value.reduce((sum, row) => sum + row.production + row.purchase + row.adjustment, 0),
)

const series: BarSeries[] = [
  { key: 'production', label: '生产建议', color: 'var(--chart-1)' },
  { key: 'purchase', label: '采购建议', color: 'var(--chart-2)' },
  { key: 'adjustment', label: '调整与异常', color: 'var(--chart-4)' },
]
</script>

<template>
  <article class="rounded-xl border bg-card p-4 shadow-sm">
    <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
      <h3 class="font-semibold">建议分布</h3>
      <span v-if="run && totalCount > 0" class="text-xs text-muted-foreground"
        >按目标周 × 类型 · 共 {{ totalCount }} 条</span
      >
    </div>

    <div
      v-if="!run"
      class="flex min-h-40 items-center justify-center gap-2 text-sm text-muted-foreground"
    >
      <ChartColumnIcon class="size-5" aria-hidden="true" />
      选择一次 MRP 运行后展示建议分布。
    </div>
    <div
      v-else-if="pending"
      class="flex min-h-40 items-center justify-center gap-2 text-sm text-muted-foreground"
      role="status"
    >
      <LoaderCircleIcon class="size-4 animate-spin" aria-hidden="true" />
      正在加载计划建议
    </div>
    <div
      v-else-if="!rows.length"
      class="flex min-h-40 items-center justify-center text-sm text-muted-foreground"
    >
      当前状态筛选下，这次运行没有可统计的建议。
    </div>
    <template v-else>
      <NvBarChart :data="rows" x-key="period" :series="series" :height="180" value-suffix=" 条" />
      <p class="mt-2 text-xs text-muted-foreground">
        统计范围＝建议 Tab 当前的状态筛选（待评审 / 已接受）。
      </p>
    </template>
  </article>
</template>
