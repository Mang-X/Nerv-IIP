<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import type { ProductionStatisticsPresentationRow } from '@/features/mes-production-report/productionStatisticsPresentation'
import { NvBadge, NvDataTable } from '@nerv-iip/ui'
import { computed } from 'vue'
import { inlineErrorMessage } from '@/utils/notify'
import {
  describeProductionStatisticsDegradation,
  PRODUCTION_STATISTICS_DIMENSION_LABELS,
} from '@/features/mes-production-report/productionStatisticsCsv'

const props = defineProps<{
  rows: ProductionStatisticsPresentationRow[]
  total: number
  page: number
  pageSize: string
  pending: boolean
  error?: unknown
}>()
const emit = defineEmits<{
  'update:page': [value: number]
  'update:page-size': [value: string]
  retry: []
}>()

const columns: NvDataTableColumn<ProductionStatisticsPresentationRow>[] = [
  { key: 'dimensionValueLabel', header: '统计对象', cellClass: 'font-medium' },
  { key: 'businessDate', header: '业务日' },
  { key: 'shiftCode', header: '班次' },
  { key: 'workCenterLabel', header: '工作中心' },
  { key: 'skuLabel', header: '物料' },
  { key: 'totalOutputQuantity', header: '总产出' },
  { key: 'goodQuantity', header: '合格量 / 率' },
  { key: 'scrapQuantity', header: '报废量 / 率' },
  { key: 'reworkQuantity', header: '返修量 / 率' },
  { key: 'productionReportCount', header: '报工数' },
  { key: 'resolutionStatus', header: '数据状态' },
]
const errorMessage = computed(() =>
  inlineErrorMessage(props.error, '生产统计读取失败，请稍后重试。'),
)

function value(value: string | null | undefined) {
  return value || '—'
}
function quantity(value: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value)
}
function rate(value: number | null | undefined) {
  return value == null
    ? '—'
    : new Intl.NumberFormat('zh-CN', { style: 'percent', maximumFractionDigits: 1 }).format(value)
}
function quantityRate(quantityValue: number, rateValue: number | null | undefined) {
  return `${quantity(quantityValue)} / ${rate(rateValue)}`
}
function degradedReasons(row: ProductionStatisticsPresentationRow) {
  return row.degradedReasons.map(describeProductionStatisticsDegradation).join('；')
}
</script>

<template>
  <NvDataTable
    manual
    :page="page"
    :page-size="pageSize"
    :total-items="total"
    :columns="columns"
    :rows="rows"
    :row-key="
      (row) =>
        `${row.dimension}-${row.dimensionValueLabel}-${row.businessDate}-${row.shiftCode}-${row.workCenterLabel}-${row.skuLabel}`
    "
    :loading="pending"
    :error="error"
    :error-message="errorMessage"
    :searchable="false"
    :column-settings="false"
    empty-message="当前统计范围内暂无生产报工数据。"
    @update:page="emit('update:page', $event)"
    @update:page-size="emit('update:page-size', String($event))"
    @retry="emit('retry')"
  >
    <template #cell-dimensionValueLabel="{ row }">
      <div class="grid gap-0.5">
        <span>{{ row.dimensionValueLabel }}</span>
        <span class="text-xs font-normal text-muted-foreground">
          {{ PRODUCTION_STATISTICS_DIMENSION_LABELS[row.dimension] }}
        </span>
      </div>
    </template>
    <template #cell-businessDate="{ row }">{{ value(row.businessDate) }}</template>
    <template #cell-shiftCode="{ row }">{{ value(row.shiftCode) }}</template>
    <template #cell-workCenterLabel="{ row }">{{ row.workCenterLabel }}</template>
    <template #cell-skuLabel="{ row }">{{ row.skuLabel }}</template>
    <template #cell-totalOutputQuantity="{ row }">{{ quantity(row.totalOutputQuantity) }}</template>
    <template #cell-goodQuantity="{ row }">{{
      quantityRate(row.goodQuantity, row.goodRate)
    }}</template>
    <template #cell-scrapQuantity="{ row }">{{
      quantityRate(row.scrapQuantity, row.scrapRate)
    }}</template>
    <template #cell-reworkQuantity="{ row }">{{
      quantityRate(row.reworkQuantity, row.reworkRate)
    }}</template>
    <template #cell-resolutionStatus="{ row }">
      <div class="grid max-w-64 gap-1">
        <NvBadge
          class="w-fit"
          :variant="row.resolutionStatus === 'resolved' ? 'success' : 'warning'"
        >
          {{ row.resolutionStatus === 'resolved' ? '完整' : '数据不完整' }}
        </NvBadge>
        <span v-if="row.degradedReasons.length" class="text-xs text-muted-foreground">
          {{ degradedReasons(row) }}
        </span>
      </div>
    </template>
  </NvDataTable>
</template>
