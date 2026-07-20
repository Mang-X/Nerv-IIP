<script setup lang="ts">
import type { BarSeries, NvDataTableColumn } from '@nerv-iip/ui'
import { NvBarChart, NvDataTable } from '@nerv-iip/ui'
import { LoaderCircleIcon } from '@lucide/vue'
import { computed } from 'vue'
import {
  buildParetoChartRows,
  formatQualityQuantity,
  type QualityAnalysisBucket,
} from '@/composables/useBusinessQualityAnalysis'

const props = withDefaults(
  defineProps<{
    rows: QualityAnalysisBucket[]
    pending: boolean
    errorMessage?: string
  }>(),
  { errorMessage: '' },
)

const chartRows = computed(() => buildParetoChartRows(props.rows))
const series: BarSeries[] = [{ key: 'defectQuantity', label: '缺陷数量', color: 'var(--chart-1)' }]
const columns: NvDataTableColumn<QualityAnalysisBucket>[] = [
  { key: 'label', header: '缺陷原因', cellClass: 'font-medium' },
  { key: 'count', header: 'NCR 数', align: 'end', width: 'w-24' },
  { key: 'defectQuantity', header: '缺陷数量', align: 'end', width: 'w-28' },
  { key: 'sharePercent', header: '缺陷占比', align: 'end', width: 'w-24' },
]
</script>

<template>
  <section aria-labelledby="quality-pareto-title" class="grid gap-3">
    <div>
      <h2 id="quality-pareto-title" class="text-base font-semibold">当前返回窗口缺陷 Pareto</h2>
      <p class="text-sm text-muted-foreground">
        按缺陷数量降序展示；数据仅覆盖当前后端返回窗口，不是全量历史趋势。
      </p>
    </div>

    <p
      v-if="errorMessage"
      class="rounded-lg border border-destructive/40 bg-destructive/5 p-4 text-sm text-destructive"
      role="alert"
    >
      {{ errorMessage }}
    </p>
    <div
      v-else-if="pending"
      class="flex min-h-40 items-center justify-center gap-2 rounded-xl border bg-card p-6 text-sm text-muted-foreground"
      role="status"
    >
      <LoaderCircleIcon class="size-4 animate-spin" aria-hidden="true" />
      正在加载当前返回窗口缺陷数据
    </div>
    <div
      v-else-if="rows.length === 0"
      class="flex min-h-40 items-center justify-center rounded-xl border bg-card p-6 text-sm text-muted-foreground"
    >
      当前返回窗口没有 NCR，暂无可汇总的缺陷原因。
    </div>
    <div v-else class="rounded-xl border bg-card p-4 shadow-sm">
      <NvBarChart
        v-if="rows.length"
        :data="chartRows"
        x-key="reason"
        :series="series"
        :height="220"
      />
    </div>

    <NvDataTable
      v-if="!errorMessage"
      :columns="columns"
      :rows="rows"
      row-key="label"
      :loading="pending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前返回窗口没有 NCR，暂无可汇总的缺陷原因。"
    >
      <template #cell-defectQuantity="{ row }">{{
        formatQualityQuantity(row.defectQuantity)
      }}</template>
      <template #cell-sharePercent="{ row }">{{ row.sharePercent }}%</template>
    </NvDataTable>
  </section>
</template>
