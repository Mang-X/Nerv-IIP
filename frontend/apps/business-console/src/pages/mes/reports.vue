<script setup lang="ts">
import type { BusinessConsoleTelemetryOeeAggregateDimension } from '@nerv-iip/api-client'
import { DownloadIcon, RefreshCwIcon } from '@lucide/vue'
import { NvButton, NvPageHeader } from '@nerv-iip/ui'
import { computed, ref, watch } from 'vue'
import MesProductionContextPanel from '@/components/mes/production-report/MesProductionContextPanel.vue'
import MesProductionReportFilters from '@/components/mes/production-report/MesProductionReportFilters.vue'
import MesProductionStatisticsTable from '@/components/mes/production-report/MesProductionStatisticsTable.vue'
import { useBusinessTelemetryOeeAggregates } from '@/composables/useBusinessTelemetry'
import { useMesWipSummary } from '@/composables/useBusinessMes'
import { useMesProductionStatistics } from '@/composables/useMesProductionStatistics'
import { usePagedList } from '@/composables/usePagedList'
import {
  createProductionStatisticsCsv,
  productionStatisticsCsvFilename,
  PRODUCTION_STATISTICS_DIMENSION_LABELS,
} from '@/features/mes-production-report/productionStatisticsCsv'
import { defaultProductionStatisticsWindow } from '@/features/mes-production-report/productionStatisticsWindow'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '生产日报',
    requiredPermissions: ['business.mes.reporting.read'],
  },
})

const defaultWindow = defaultProductionStatisticsWindow()

const auth = useAuthStore()
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canReadWip = computed(() => permissionCodes.value.includes(P.mesOperationsRead))
const canReadOee = computed(() => permissionCodes.value.includes(P.iiotTelemetryRead))
const report = useMesProductionStatistics({
  windowStartUtc: defaultWindow.startUtc,
  windowEndUtc: defaultWindow.endUtc,
})
const { page, pageSize } = usePagedList(report.filters, {
  initialPageSize: '20',
  resetOn: [
    () => report.filters.dimension,
    () => report.filters.windowStartUtc,
    () => report.filters.windowEndUtc,
    () => report.filters.businessDate,
    () => report.filters.shiftCode,
    () => report.filters.workCenterId,
    () => report.filters.skuId,
  ],
})

const wip = useMesWipSummary(() => canReadWip.value)
const oee = useBusinessTelemetryOeeAggregates(
  {
    dimension: 'day',
    windowStartUtc: report.filters.windowStartUtc,
    windowEndUtc: report.filters.windowEndUtc,
    take: 5,
  },
  () => canReadOee.value && report.filters.dimension !== 'sku',
)
const exporting = ref(false)
const countLabel = computed(() =>
  report.state.value === 'ready'
    ? `${report.total.value} 个${PRODUCTION_STATISTICS_DIMENSION_LABELS[report.filters.dimension]}聚合`
    : undefined,
)

watch(
  () =>
    [
      report.filters.dimension,
      report.filters.windowStartUtc,
      report.filters.windowEndUtc,
      report.filters.businessDate,
      report.filters.shiftCode,
      report.filters.workCenterId,
    ] as const,
  ([dimension, windowStartUtc, windowEndUtc, businessDate, shiftCode, workCenterId]) => {
    wip.filters.workCenterId = workCenterId
    wip.filters.skip = 0
    wip.filters.take = 5
    oee.filters.dimension = oeeDimension(dimension)
    oee.filters.windowStartUtc = windowStartUtc
    oee.filters.windowEndUtc = windowEndUtc
    oee.filters.businessDate = businessDate
    oee.filters.shiftCode = shiftCode
    oee.filters.workCenterId = workCenterId
    oee.filters.skip = 0
    oee.filters.take = 5
  },
  { immediate: true },
)

function oeeDimension(
  dimension: typeof report.filters.dimension,
): BusinessConsoleTelemetryOeeAggregateDimension {
  return dimension === 'sku' ? 'workCenter' : dimension
}

function updateFilters(patch: Partial<typeof report.filters>) {
  Object.assign(report.filters, patch)
}

function refreshPage() {
  const requests: Promise<unknown>[] = [report.refresh()]
  if (canReadWip.value) requests.push(wip.refreshWip())
  if (canReadOee.value && report.filters.dimension !== 'sku') requests.push(oee.refreshAggregates())
  return Promise.all(requests)
}

async function exportCsv() {
  exporting.value = true
  const filename = productionStatisticsCsvFilename({ ...report.filters })
  try {
    const rows = await report.loadAll()
    const blob = new Blob([createProductionStatisticsCsv(rows)], {
      type: 'text/csv;charset=utf-8',
    })
    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = filename
    link.click()
    URL.revokeObjectURL(url)
    notifySuccess(`已导出 ${rows.length} 行生产统计`)
  } catch (error) {
    notifyError(error, '生产统计导出失败，请稍后重试。')
  } finally {
    exporting.value = false
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="生产日报" :breadcrumbs="[{ label: '制造执行' }]" :count="countLabel">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="exporting || report.pending.value"
          @click="exportCsv"
        >
          <DownloadIcon aria-hidden="true" />{{ exporting ? '导出中…' : '导出 CSV' }}
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="report.pending.value"
          @click="refreshPage"
        >
          <RefreshCwIcon aria-hidden="true" />刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <MesProductionReportFilters :filters="report.filters" @update="updateFilters" />

    <MesProductionContextPanel
      :can-read-wip="canReadWip"
      :wip-state="wip.wipState.value"
      :wip-total="wip.wipTotal.value"
      :wip-rows="wip.wipRows.value"
      :can-read-oee="canReadOee"
      :is-sku-dimension="report.filters.dimension === 'sku'"
      :oee-pending="oee.aggregatePending.value"
      :oee-error="oee.aggregateError.value"
      :oee-buckets="oee.aggregateBuckets.value"
    />

    <MesProductionStatisticsTable
      :rows="report.items.value"
      :total="report.total.value"
      :page="page"
      :page-size="pageSize"
      :pending="report.pending.value"
      :error="report.error.value"
      @update:page="page = $event"
      @update:page-size="pageSize = $event"
      @retry="report.refresh"
    />
  </BusinessLayout>
</template>
