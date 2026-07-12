<script setup lang="ts">
import type { BusinessConsoleMaintenanceReliabilitySummaryItem } from '@nerv-iip/api-client'
import type { LineSeries, NvDataTableColumn } from '@nerv-iip/ui'
import {
  useMaintenanceMeasurementTrend,
  useMaintenanceReliability,
  useMaintenanceReliabilitySummary,
} from '@/composables/useBusinessMaintenance'
import {
  useBusinessWorkers,
  useBusinessMasterDataResources,
} from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { COMMON_INSPECTION_CHARACTERISTICS } from '@nerv-iip/business-core'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvLineChart,
  NvPageHeader,
  NvSearchSelect,
  NvSectionCard,
  NvSectionCards,
} from '@nerv-iip/ui'
import { ActivityIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '可靠性指标',
    requiredPermissions: ['business.maintenance.work-orders.read'],
  },
})

const route = useRoute()
const initialDeviceAssetId =
  typeof route.query.deviceAssetId === 'string' ? route.query.deviceAssetId : ''
const { filters, reliability, reliabilityError, reliabilityPending, refreshReliability } =
  useMaintenanceReliability({
    deviceAssetId: initialDeviceAssetId,
  })

// 趋势小图（同设备同特性测量值时序）与技师聚合汇总，与主查询共享设备/窗口范围。
const trend = useMaintenanceMeasurementTrend({ deviceAssetId: initialDeviceAssetId })
const summary = useMaintenanceReliabilitySummary({ deviceAssetId: initialDeviceAssetId })
const { workers } = useBusinessWorkers()
// 设备编号联想建议（master-data device-asset）。
const { resources: deviceResources } = useBusinessMasterDataResources('device-asset')
const deviceSuggestions = computed(() =>
  deviceResources.value
    .map((r) => ({ value: (r.code ?? '').trim(), label: r.displayName ?? r.code ?? '' }))
    .filter((s) => s.value.length > 0),
)
// 测量特性下拉候选（常用特性，从已知项里选）。
const characteristicOptions = COMMON_INSPECTION_CHARACTERISTICS.map((value) => ({
  value,
  label: value,
}))

// 设备/窗口是唯一事实源（reliability filters）；同步到趋势与汇总子查询。
watch(
  () => [filters.deviceAssetId, filters.windowStartUtc, filters.windowEndUtc] as const,
  ([deviceAssetId, windowStartUtc, windowEndUtc]) => {
    trend.filters.deviceAssetId = deviceAssetId
    trend.filters.windowStartUtc = windowStartUtc
    trend.filters.windowEndUtc = windowEndUtc
    summary.filters.deviceAssetId = deviceAssetId
    summary.filters.windowStartUtc = windowStartUtc
    summary.filters.windowEndUtc = windowEndUtc
  },
  { immediate: true },
)

const errorMessage = computed(() => formatError(reliabilityError.value))
const trendErrorMessage = computed(() => formatError(trend.trendError.value))
const summaryErrorMessage = computed(() => formatError(summary.summaryError.value))
const hasDeviceScope = computed(() => filters.deviceAssetId.trim().length > 0)
const hasCharacteristic = computed(() => trend.filters.characteristicCode.trim().length > 0)

const windowStartLocal = computed({
  get: () => toLocalDateTime(filters.windowStartUtc),
  set: (value: string) => {
    filters.windowStartUtc = toIsoDateTime(value)
  },
})
const windowEndLocal = computed({
  get: () => toLocalDateTime(filters.windowEndUtc),
  set: (value: string) => {
    filters.windowEndUtc = toIsoDateTime(value)
  },
})

// 趋势图数据：测量值时序；上下限齐备时叠加为参考线（缺失则不画，避免 0 误导）。
const trendChartData = computed(() =>
  trend.trendItems.value.map((item) => ({
    time: shortDateTime(item.inspectedAtUtc),
    value: Number(item.measuredValue ?? 0),
    lower: Number(item.lowerSpecLimit ?? 0),
    upper: Number(item.upperSpecLimit ?? 0),
  })),
)
const hasLowerLimits = computed(
  () =>
    trend.trendItems.value.length > 0 &&
    trend.trendItems.value.every(
      (i) => i.lowerSpecLimit !== null && i.lowerSpecLimit !== undefined,
    ),
)
const hasUpperLimits = computed(
  () =>
    trend.trendItems.value.length > 0 &&
    trend.trendItems.value.every(
      (i) => i.upperSpecLimit !== null && i.upperSpecLimit !== undefined,
    ),
)
const trendSeries = computed<LineSeries[]>(() => {
  const series: LineSeries[] = [{ key: 'value', label: '测量值' }]
  if (hasLowerLimits.value) series.push({ key: 'lower', label: '下限' })
  if (hasUpperLimits.value) series.push({ key: 'upper', label: '上限' })
  return series
})
const outOfSpecTrendCount = computed(
  () => trend.trendItems.value.filter((i) => i.isWithinSpec === false).length,
)

type SummaryRow = BusinessConsoleMaintenanceReliabilitySummaryItem
const summaryColumns: NvDataTableColumn<SummaryRow>[] = [
  {
    key: 'assignedTechnicianUserId',
    header: '技师',
    accessor: (r) => technicianLabel(r.assignedTechnicianUserId),
  },
  {
    key: 'workOrderCount',
    header: '工单数',
    align: 'end',
    accessor: (r) => String(r.workOrderCount ?? 0),
  },
  {
    key: 'estimatedLaborMinutes',
    header: '预估工时',
    align: 'end',
    accessor: (r) => minutesLabel(r.estimatedLaborMinutes),
  },
  {
    key: 'actualLaborMinutes',
    header: '实际工时',
    align: 'end',
    accessor: (r) => minutesLabel(r.actualLaborMinutes),
  },
  {
    key: 'sparePartCostAmount',
    header: '备件成本',
    align: 'end',
    accessor: (r) => moneyLabel(r.sparePartCostAmount, r.costCurrencyCode),
  },
  {
    key: 'externalServiceCostAmount',
    header: '外委费用',
    align: 'end',
    accessor: (r) => moneyLabel(r.externalServiceCostAmount, r.costCurrencyCode),
  },
  {
    key: 'totalCostAmount',
    header: '成本合计',
    align: 'end',
    cellClass: 'font-medium',
    accessor: (r) => moneyLabel(r.totalCostAmount, r.costCurrencyCode),
  },
]
function summaryRowKey(row: SummaryRow) {
  return `${row.deviceAssetId ?? ''}-${row.assignedTechnicianUserId ?? 'unassigned'}`
}

function technicianLabel(userId?: string | null) {
  if (!userId) return '未指派'
  return workers.value.find((w) => w.userId === userId)?.displayName ?? userId
}
function minutesLabel(value?: number | null) {
  if (value === null || value === undefined) return '—'
  return `${Number(value)} 分`
}
function moneyLabel(value?: number | null, currency?: string | null) {
  if (value === null || value === undefined) return '—'
  return `${currency ? `${currency} ` : ''}${Number(value).toFixed(2)}`
}
function metricLabel(value?: number | null, suffix = '') {
  if (value === null || value === undefined) return '无样本'
  return `${Number(value).toFixed(1)}${suffix}`
}
function shortDateTime(value?: string | null) {
  if (!value) return ''
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleDateString('zh-CN', { month: '2-digit', day: '2-digit' })
}
function toLocalDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function toIsoDateTime(value: string) {
  const date = value ? new Date(value) : new Date()
  return Number.isNaN(date.getTime()) ? new Date().toISOString() : date.toISOString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

function refreshAll() {
  void refreshReliability()
  void trend.refreshTrend()
  void summary.refreshSummary()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="可靠性指标"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="hasDeviceScope ? filters.deviceAssetId : '选择设备后查询'"
    >
      <template #actions>
        <NvButton v-if="hasDeviceScope" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/equipment/${filters.deviceAssetId}`">
            <ActivityIcon aria-hidden="true" />
            设备详情
          </RouterLink>
        </NvButton>
        <NvButton v-else size="sm" type="button" variant="outline" :disabled="true">
          <ActivityIcon aria-hidden="true" />
          设备详情
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!hasDeviceScope || reliabilityPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvFieldGroup
      class="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-[minmax(220px,1fr)_220px_220px]"
    >
      <NvField>
        <NvFieldLabel for="rel-device">设备</NvFieldLabel>
        <NvCombobox
          id="rel-device"
          v-model="filters.deviceAssetId"
          :suggestions="deviceSuggestions"
          placeholder="搜索设备台账或直接输入，如 DEV-PRESS-01"
        />
      </NvField>
      <NvField>
        <NvFieldLabel for="rel-start">窗口开始</NvFieldLabel>
        <NvInput id="rel-start" v-model="windowStartLocal" type="datetime-local" />
      </NvField>
      <NvField>
        <NvFieldLabel for="rel-end">窗口结束</NvFieldLabel>
        <NvInput id="rel-end" v-model="windowEndLocal" type="datetime-local" />
      </NvField>
    </NvFieldGroup>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div
      v-if="!hasDeviceScope"
      class="rounded-lg border border-dashed p-6 text-sm text-muted-foreground"
    >
      请选择设备后查看 MTBF、MTTR、故障次数、测量趋势与工时费用聚合。
    </div>

    <template v-else>
      <NvSectionCards :columns="4">
        <NvSectionCard
          description="MTBF"
          :value="metricLabel(reliability?.mtbfHours, ' 小时')"
          :hint="reliability?.mtbfRuntimeHasSamples ? '按运行样本计算' : '当前窗口无运行样本'"
        />
        <NvSectionCard
          description="MTTR"
          :value="metricLabel(reliability?.mttrMinutes, ' 分钟')"
          hint="维修完成样本均值"
        />
        <NvSectionCard
          description="故障次数"
          :value="reliability?.failureCount ?? 0"
          hint="窗口内维护故障"
        />
        <NvSectionCard
          description="修复次数"
          :value="reliability?.repairCount ?? 0"
          hint="窗口内完成维修"
        />
      </NvSectionCards>

      <!-- 测量值趋势小图（同设备同特性时间序列） -->
      <section class="grid gap-3 rounded-lg border bg-card p-4">
        <div class="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 class="text-sm font-medium text-foreground">测量值趋势</h2>
            <p class="text-sm text-muted-foreground">同设备同特性的历次点检测量值时序。</p>
          </div>
          <NvField class="w-full sm:w-64">
            <NvFieldLabel for="rel-characteristic">测量特性</NvFieldLabel>
            <NvSearchSelect
              id="rel-characteristic"
              v-model="trend.filters.characteristicCode"
              :options="characteristicOptions"
              aria-label="测量特性"
              placeholder="选择特性"
              search-placeholder="搜索特性…"
            />
          </NvField>
        </div>

        <p v-if="trendErrorMessage" class="text-sm text-destructive" role="alert">
          {{ trendErrorMessage }}
        </p>

        <div
          v-if="!hasCharacteristic"
          class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
        >
          输入测量特性后查看该特性的测量值趋势。
        </div>
        <div
          v-else-if="trend.trendPending.value"
          class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
        >
          加载中…
        </div>
        <div
          v-else-if="trendChartData.length === 0"
          class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
        >
          该特性在当前窗口内暂无测量样本。
        </div>
        <div v-else class="grid gap-2">
          <p
            v-if="outOfSpecTrendCount > 0"
            class="text-sm font-medium text-destructive"
            role="status"
          >
            窗口内 {{ outOfSpecTrendCount }} 次测量超差。
          </p>
          <NvLineChart :data="trendChartData" x-key="time" :series="trendSeries" :height="220" />
        </div>
      </section>

      <!-- 按技师聚合的工时与费用 -->
      <section class="grid gap-3">
        <div>
          <h2 class="text-sm font-medium text-foreground">工时与费用（按技师聚合）</h2>
          <p class="text-sm text-muted-foreground">
            当前设备窗口内，完工工单按指派技师汇总的工时与成本。
          </p>
        </div>
        <p v-if="summaryErrorMessage" class="text-sm text-destructive" role="alert">
          {{ summaryErrorMessage }}
        </p>
        <NvDataTable
          :columns="summaryColumns"
          :rows="summary.summaryItems.value"
          :row-key="summaryRowKey"
          :loading="summary.summaryPending.value"
          :searchable="false"
          :column-settings="false"
          :pagination="false"
          empty-message="当前设备窗口内暂无已完工工单的工时/费用数据。"
        />
      </section>
    </template>
  </BusinessLayout>
</template>
