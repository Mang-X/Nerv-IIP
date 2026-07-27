<script setup lang="ts">
import type { BusinessConsoleMaintenanceReliabilitySummaryItem } from '@nerv-iip/api-client'
import type {
  DateRange,
  LineSeries,
  NvDataTableColumn,
  NvMetricSegment,
  NvMetricStripCell,
} from '@nerv-iip/ui'
import {
  useMaintenanceInspections,
  useMaintenanceMeasurementTrend,
  useMaintenanceReliability,
  useMaintenanceReliabilitySummary,
} from '@/composables/useBusinessMaintenance'
import EquipmentScopeOverviewCard from '@/components/equipment/EquipmentScopeOverviewCard.vue'
import { useEquipmentScopeSelection } from '@/composables/useEquipmentScopeSelection'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { COMMON_INSPECTION_CHARACTERISTICS } from '@nerv-iip/business-core'
import {
  NvButton,
  NvCascadePicker,
  NvCombobox,
  NvDataTable,
  NvDateRangePicker,
  NvField,
  NvFieldLabel,
  NvLineChart,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvSectionCards,
} from '@nerv-iip/ui'
import { ActivityIcon, RefreshCwIcon } from '@lucide/vue'
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
// 车间 → 产线 → 设备 级联范围：MTBF/MTTR 等后端指标按单台设备计算，
// 未下钻时展示范围设备总览引导下钻。深链 query 初始化下钻设备。
const { scope, levels, devicesInScope, scopeLabel, scopePending } = useEquipmentScopeSelection({
  device: initialDeviceAssetId,
})
watch(
  () => scope.value.device,
  (device) => {
    if (filters.deviceAssetId !== device) filters.deviceAssetId = device
  },
)
// 趋势查询是筛选面，用输入联想框（NvCombobox）：允许输入**任意**特性查趋势（含分页外/较早的
// PDA 自定义特性），下方建议 = 常用特性 + 已加载点检历史里真实出现过的特性（后端同源）去重，仅作快填。
// （录入表单是"从已知项里选"，用 NvSearchSelect；查询面是"任意特性可查"，故此处放开自由输入。）
const { inspections } = useMaintenanceInspections()
const characteristicOptions = computed(() => {
  const seen = new Set<string>()
  const out: { value: string; label: string }[] = []
  const add = (code?: string | null) => {
    const c = (code ?? '').trim()
    if (c && !seen.has(c)) {
      seen.add(c)
      out.push({ value: c, label: c })
    }
  }
  for (const code of COMMON_INSPECTION_CHARACTERISTICS) add(code)
  for (const inspection of inspections.value) {
    for (const measurement of inspection.measurements ?? []) add(measurement.characteristicCode)
  }
  return out
})

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

// 日期控件用本地 YYYY-MM-DD，查询窗口用 ISO 瞬时：开始取当日 00:00，结束取次日 00:00（含尾日整天）。
const windowRange = computed<DateRange>({
  get: () => ({
    end: toDateInput(filters.windowEndUtc),
    start: toDateInput(filters.windowStartUtc),
  }),
  set: (range) => {
    if (range.start) filters.windowStartUtc = fromDateInput(range.start, 0)
    if (range.end) filters.windowEndUtc = fromDateInput(range.end, 1)
  },
})

// 无运行样本时后端仍会给出一个窗口兜底值（如 720 小时＝整窗口无故障的名义值）。
// 那不是实测 MTBF，读数与副行「当前窗口无运行样本」直接打架，所以两处口径统一：
// 没有运行样本就走无样本态，不显示任何小时数（MTTR 已是这个做法）。
const mtbfHasSamples = computed(() => reliability.value?.mtbfRuntimeHasSamples === true)
const reliabilityCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'mtbf',
    label: 'MTBF',
    value: mtbfHasSamples.value ? metricLabel(reliability.value?.mtbfHours, ' 小时') : '无样本',
    meta: mtbfHasSamples.value ? '按运行样本计算' : '当前窗口无运行样本',
  },
  {
    key: 'mttr',
    label: 'MTTR',
    value: metricLabel(reliability.value?.mttrMinutes, ' 分钟'),
    meta: '已完成维修的平均耗时',
  },
])
// 故障与完成维修是同一批维护事件的两个互斥去向，相加等于窗口内维护事件总数。
const maintenanceEventSegments = computed<NvMetricSegment[]>(() => [
  { key: 'failure', label: '故障', value: reliability.value?.failureCount ?? 0, tone: 'danger' },
  { key: 'repair', label: '完成维修', value: reliability.value?.repairCount ?? 0, tone: 'success' },
])
const maintenanceEventTotal = computed(
  () => (reliability.value?.failureCount ?? 0) + (reliability.value?.repairCount ?? 0),
)

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
function toDateInput(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}
function fromDateInput(value: string, dayOffset: number) {
  const [y, m, d] = value.split('-').map(Number)
  if (!y || !m || !d) return new Date().toISOString()
  return new Date(y, m - 1, d + dayOffset).toISOString()
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
      :count="hasDeviceScope ? filters.deviceAssetId : scopeLabel"
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

    <div class="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-[minmax(0,1fr)_auto]">
      <NvCascadePicker v-model="scope" :levels="levels" :aria-busy="scopePending" />
      <NvField>
        <NvFieldLabel for="rel-window">统计窗口</NvFieldLabel>
        <NvDateRangePicker id="rel-window" v-model="windowRange" placeholder="选择统计窗口" />
      </NvField>
    </div>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <EquipmentScopeOverviewCard
      v-if="!hasDeviceScope"
      :devices="devicesInScope"
      :scope-label="scopeLabel"
      :pending="scopePending"
      action-label="查看指标"
      description="MTBF、MTTR、测量趋势与工时费用按单台设备统计：选中一台设备即可查看它的可靠性指标。"
      @select="(code) => (scope = { ...scope, device: code })"
    />

    <template v-else>
      <NvSectionCards :columns="2">
        <NvMetricStrip :cells="reliabilityCells" />
        <NvMetricCard
          variant="breakdown"
          label="窗口内维护事件"
          :value="maintenanceEventTotal"
          unit=" 次"
          :segments="maintenanceEventSegments"
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
            <NvCombobox
              id="rel-characteristic"
              v-model="trend.filters.characteristicCode"
              :suggestions="characteristicOptions"
              placeholder="输入或选择特性查趋势，如 轴承温度"
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
