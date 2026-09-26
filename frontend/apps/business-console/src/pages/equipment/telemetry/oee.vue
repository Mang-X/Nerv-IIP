<script setup lang="ts">
import type { BusinessConsoleTelemetryOeeAggregateDimension } from '@nerv-iip/api-client'
import type { DateRange, NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import {
  describeTelemetryOeeDegradation,
  formatOeeRate,
  useBusinessTelemetryOeeAggregates,
  useBusinessTelemetryOeeTrend,
} from '@/composables/useBusinessTelemetry'
import { usePagedList } from '@/composables/usePagedList'
import { presentOeeReport, type OeeTableRow } from '@/pages/equipment/telemetry/oeePresentation'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvDateRangePicker,
  NvField,
  NvFieldLabel,
  NvInput,
  NvLineChart,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvToolbar,
} from '@nerv-iip/ui'
import { LineChartIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: 'OEE 趋势与横比',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const route = useRoute()
const DEFAULT_WINDOW_DAYS = 7
const defaultWindow = (() => {
  const end = new Date()
  const start = new Date(end)
  start.setDate(start.getDate() - DEFAULT_WINDOW_DAYS)
  return { endUtc: end.toISOString(), startUtc: start.toISOString() }
})()

const dimensions: Array<{ value: BusinessConsoleTelemetryOeeAggregateDimension; label: string }> = [
  { value: 'day', label: '业务日趋势' },
  { value: 'workCenter', label: '工作中心横比' },
  { value: 'line', label: '产线横比' },
  { value: 'workshop', label: '车间横比' },
  { value: 'shift', label: '班次横比' },
]

const {
  aggregateBuckets,
  aggregateError,
  aggregatePending,
  aggregateTotal,
  filters,
  refreshAggregates,
} = useBusinessTelemetryOeeAggregates({
  dimension: dimensionQuery(routeQuery('dimension')),
  deviceAssetId: routeQuery('deviceAssetId'),
  workCenterId: routeQuery('workCenterId'),
  shiftCode: routeQuery('shiftCode'),
  lineCode: routeQuery('lineCode'),
  workshopCode: routeQuery('workshopCode'),
  businessDate: routeQuery('businessDate'),
  windowEndUtc: routeQuery('windowEndUtc') || defaultWindow.endUtc,
  windowStartUtc: routeQuery('windowStartUtc') || defaultWindow.startUtc,
})
const { refreshTrend, trendBuckets, trendError, trendPending } =
  useBusinessTelemetryOeeTrend(filters)

const { page, pageSize } = usePagedList(filters, {
  initialPageSize: '20',
  resetOn: [
    () => filters.dimension,
    () => filters.windowStartUtc,
    () => filters.windowEndUtc,
    () => filters.deviceAssetId,
    () => filters.workCenterId,
    () => filters.shiftCode,
    () => filters.lineCode,
    () => filters.workshopCode,
    () => filters.businessDate,
  ],
})
watch(
  () => filters.dimension,
  (dimension) => {
    if (dimension !== 'day') filters.deviceAssetId = ''
  },
)

const dimensionLabel = computed(
  () => dimensions.find((item) => item.value === filters.dimension)?.label ?? 'OEE 聚合',
)
const errorMessage = computed(() => inlineErrorMessage(aggregateError.value))
const trendErrorMessage = computed(() => inlineErrorMessage(trendError.value))
const reportPresentation = computed(() =>
  presentOeeReport({
    dimension: filters.dimension,
    trendBuckets: trendBuckets.value,
    tableBuckets: aggregateBuckets.value,
    tableTotal: aggregateTotal.value,
  }),
)
const summaryCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'window',
    label: '数据窗口',
    value: formatWindow(filters.windowStartUtc, filters.windowEndUtc),
  },
  { key: 'timezone', label: '查询时区', value: 'UTC' },
  { key: 'dimension', label: '聚合维度', value: dimensionLabel.value },
  { key: 'count', label: '结果', value: aggregateTotal.value, unit: ' 个桶' },
])

const columns = computed<NvDataTableColumn<OeeTableRow>[]>(() => [
  { key: 'primaryLabel', header: '对比对象' },
  { key: 'hierarchyLabel', header: '站点 / 层级' },
  ...(filters.dimension === 'day' || filters.dimension === 'shift'
    ? [{ key: 'businessDateLabel', header: '业务日' }]
    : []),
  { key: 'windowLabel', header: '聚合窗口' },
  { key: 'oeeRate', header: 'OEE', accessor: (row) => rateCell(row.oeeRate) },
  {
    key: 'availabilityRate',
    header: '可用率',
    accessor: (row) => rateCell(row.availabilityRate),
  },
  {
    key: 'performanceRate',
    header: '性能率',
    accessor: (row) => rateCell(row.performanceRate),
  },
  { key: 'qualityRate', header: '质量率', accessor: (row) => rateCell(row.qualityRate) },
  { key: 'deviceCount', header: '设备', accessor: (row) => `${row.deviceCount ?? 0} 台` },
  { key: 'isDegraded', header: '数据状态', accessor: degradationSummary },
])

const windowRange = computed<DateRange>({
  get: () => ({
    end: toDateInput(filters.windowEndUtc, -1),
    start: toDateInput(filters.windowStartUtc),
  }),
  set: (range) => {
    if (range.start) filters.windowStartUtc = fromDateInput(range.start, 0)
    if (range.end) filters.windowEndUtc = fromDateInput(range.end, 1)
  },
})

function routeQuery(key: string) {
  const value = route.query[key]
  return Array.isArray(value) ? (value[0] ?? '') : (value?.toString() ?? '')
}
function dimensionQuery(value: string): BusinessConsoleTelemetryOeeAggregateDimension {
  return dimensions.some((item) => item.value === value)
    ? (value as BusinessConsoleTelemetryOeeAggregateDimension)
    : 'day'
}
function toDateInput(value: string, dayOffset = 0) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  if (dayOffset) date.setDate(date.getDate() + dayOffset)
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 10)
}
function fromDateInput(value: string, dayOffset: number) {
  const [year, month, day] = value.split('-').map(Number)
  if (!year || !month || !day) return new Date().toISOString()
  return new Date(year, month - 1, day + dayOffset).toISOString()
}
function rateCell(value: number | null | undefined) {
  return value == null ? '—' : formatOeeRate(value)
}
function formatWindow(start: string, end: string) {
  return `${formatDateTime(start, false)} – ${formatDateTime(end, false)}`
}
function formatDateTime(value?: string | null, includeTime = true) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  return includeTime
    ? date.toLocaleString('zh-CN', { timeZone: 'UTC', hour12: false })
    : date.toLocaleDateString('zh-CN', { timeZone: 'UTC' })
}
function degradationSummary(row: OeeTableRow) {
  if (!row.isDegraded) return '完整'
  const reasons = row.degradedReasons ?? []
  return reasons.length > 0 ? reasons.map(describeTelemetryOeeDegradation).join('；') : '数据不完整'
}
function refreshReport() {
  return Promise.all([refreshAggregates(), refreshTrend()])
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="OEE 趋势与横比"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="dimensionLabel"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/history">
            <LineChartIcon aria-hidden="true" />遥测历史
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/alarm-rules">
            <Settings2Icon aria-hidden="true" />报警规则
          </RouterLink>
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="aggregatePending || trendPending"
          @click="refreshReport"
        >
          <RefreshCwIcon aria-hidden="true" />刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvField class="min-w-44">
          <NvFieldLabel>报表视角</NvFieldLabel>
          <NvSelect v-model="filters.dimension">
            <NvSelectTrigger aria-label="报表视角"><NvSelectValue /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem v-for="item in dimensions" :key="item.value" :value="item.value">
                {{ item.label }}
              </NvSelectItem>
            </NvSelectContent>
          </NvSelect>
        </NvField>
        <NvField class="min-w-64">
          <NvFieldLabel>数据窗口（最多 31 天）</NvFieldLabel>
          <NvDateRangePicker v-model="windowRange" placeholder="选择数据窗口" />
        </NvField>
      </template>
    </NvToolbar>

    <section class="grid gap-3 rounded-lg border bg-card p-4" aria-label="范围筛选">
      <div>
        <h2 class="text-sm font-semibold text-foreground">范围筛选</h2>
        <p class="text-sm text-muted-foreground">留空表示全部。</p>
      </div>
      <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-6">
        <NvField>
          <NvFieldLabel>设备</NvFieldLabel>
          <DirectoryPicker
            v-model="filters.deviceAssetId"
            directory-type="equipment"
            placeholder="全部设备"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel>工作中心</NvFieldLabel>
          <DirectoryPicker
            v-model="filters.workCenterId"
            directory-type="work-center"
            placeholder="全部工作中心"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel>产线</NvFieldLabel>
          <DirectoryPicker
            v-model="filters.lineCode"
            directory-type="production-line"
            placeholder="全部产线"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel>车间</NvFieldLabel>
          <DirectoryPicker
            v-model="filters.workshopCode"
            directory-type="workshop"
            placeholder="全部车间"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel>班次</NvFieldLabel>
          <DirectoryPicker
            v-model="filters.shiftCode"
            directory-type="shift"
            placeholder="全部班次"
            clearable
          />
        </NvField>
        <NvField>
          <NvFieldLabel>业务日</NvFieldLabel>
          <NvInput v-model="filters.businessDate" type="date" />
        </NvField>
      </div>
    </section>

    <NvMetricStrip :cells="summaryCells" />

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <section
      v-if="filters.dimension === 'day' && !errorMessage"
      class="grid gap-3 rounded-lg border bg-card p-4"
    >
      <div>
        <h2 class="text-sm font-semibold text-foreground">OEE 与 A/P/Q 业务日趋势</h2>
        <p class="text-sm text-muted-foreground">
          业务日按历史站点时区与日界线聚合；趋势独立读取完整窗口，不随下方核查表翻页改变。
        </p>
        <p class="text-sm text-muted-foreground">横轴使用业务日“月/日”短标签。</p>
      </div>
      <p v-if="trendErrorMessage" class="text-sm text-destructive" role="alert">
        {{ trendErrorMessage }}
      </p>
      <div
        v-if="trendPending && reportPresentation.trendBucketCount === 0"
        class="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground"
      >
        正在加载趋势…
      </div>
      <div
        v-else-if="!trendErrorMessage && reportPresentation.trendBucketCount === 0"
        class="rounded-lg border border-dashed p-8 text-center text-sm text-muted-foreground"
      >
        当前窗口没有可绘制的完整率值；缺失事实仍会保留在下方核查表中。
      </div>
      <template v-else-if="!trendErrorMessage">
        <p class="text-sm text-muted-foreground" role="status">
          完整窗口共 {{ reportPresentation.trendBucketCount }} 个业务日聚合桶，按
          {{ reportPresentation.trendGroups.length }} 个站点分别呈现。
        </p>
        <p
          v-if="reportPresentation.omittedTrendBucketCount > 0"
          class="text-sm text-warning"
          role="status"
        >
          {{ reportPresentation.omittedTrendBucketCount }} 个桶缺少率值，未画成
          0%；请在下方查看缺失原因。
        </p>
        <div class="grid gap-4">
          <section
            v-for="group in reportPresentation.trendGroups"
            :key="group.key"
            class="grid gap-3 rounded-lg border p-3"
            :data-oee-site="group.siteCode ?? ''"
          >
            <div>
              <h3 class="text-sm font-semibold text-foreground">站点 {{ group.siteLabel }}</h3>
              <p class="text-xs text-muted-foreground">
                {{ group.bucketCount }} 个桶，{{ group.pointCount }} 个完整率值点<span
                  v-if="group.omittedCount > 0"
                  >，{{ group.omittedCount }} 个缺失点保留在核查表</span
                >。
              </p>
            </div>
            <div class="grid gap-3">
              <section
                v-for="segment in group.segments"
                :key="segment.key"
                class="grid gap-3 rounded-lg border bg-muted/20 p-3"
                :data-oee-segment="segment.key"
              >
                <div class="grid gap-1">
                  <h4 class="text-sm font-medium text-foreground">
                    历史窗口段 {{ segment.ordinal }}
                  </h4>
                  <p class="text-xs text-muted-foreground">
                    业务日 {{ segment.businessDateStartLabel }} 至
                    {{ segment.businessDateEndLabel }}；{{ segment.bucketCount }} 个桶，{{
                      segment.pointCount
                    }}
                    个完整率值点<span v-if="segment.omittedCount > 0"
                      >，{{ segment.omittedCount }} 个缺失点</span
                    >。
                  </p>
                </div>
                <div
                  v-if="segment.runs.length === 0"
                  class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
                >
                  本历史窗口段没有可绘制的完整率值；全部缺失事实仍保留在下方核查表中。
                </div>
                <template v-else>
                  <div
                    v-for="(run, runIndex) in segment.runs"
                    :key="run.key"
                    class="grid gap-2"
                    :data-oee-run="run.key"
                  >
                    <p v-if="segment.runs.length > 1" class="text-xs text-muted-foreground">
                      连续趋势 {{ runIndex + 1 }} / {{ segment.runs.length }}
                    </p>
                    <NvLineChart
                      v-if="run.displayMode === 'line'"
                      :data="run.chartData"
                      x-key="time"
                      :series="group.series"
                      :height="280"
                      value-suffix="%"
                    />
                    <div
                      v-else
                      class="grid gap-2 rounded-lg border bg-card p-3"
                      data-oee-discrete-point
                    >
                      <h5 class="text-sm font-medium text-foreground">
                        离散桶 · {{ run.points[0]?.businessDateLabel }}
                      </h5>
                      <dl class="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
                        <div>
                          <dt class="text-muted-foreground">OEE</dt>
                          <dd>{{ run.points[0]?.oee }}%</dd>
                        </div>
                        <div>
                          <dt class="text-muted-foreground">可用率</dt>
                          <dd>{{ run.points[0]?.availability }}%</dd>
                        </div>
                        <div>
                          <dt class="text-muted-foreground">性能率</dt>
                          <dd>{{ run.points[0]?.performance }}%</dd>
                        </div>
                        <div>
                          <dt class="text-muted-foreground">质量率</dt>
                          <dd>{{ run.points[0]?.quality }}%</dd>
                        </div>
                      </dl>
                    </div>
                  </div>
                </template>
              </section>
            </div>
          </section>
        </div>
      </template>
    </section>

    <section class="grid gap-3">
      <div>
        <h2 class="text-sm font-semibold text-foreground">{{ dimensionLabel }}核查表</h2>
        <p class="text-sm text-muted-foreground">“—”表示没有数据，不代表 0% 或 100%。</p>
      </div>
      <NvDataTable
        v-model:page="page"
        v-model:page-size="pageSize"
        :columns="columns"
        :rows="reportPresentation.tableRows"
        row-key="key"
        :loading="aggregatePending"
        :error="aggregateError"
        :error-message="errorMessage"
        :manual="true"
        :total-items="reportPresentation.tableTotal"
        :page-size-options="[10, 20, 50, 100]"
        :searchable="false"
        :column-settings="false"
        empty-message="当前窗口和筛选范围内没有 OEE 聚合事实。"
      >
        <template #cell-isDegraded="{ row }">
          <div class="grid max-w-md gap-1">
            <NvBadge class="w-fit rounded-sm" :variant="row.isDegraded ? 'warning' : 'success'">
              {{ row.isDegraded ? '数据不完整' : '完整' }}
            </NvBadge>
            <span v-if="row.isDegraded" class="text-xs text-muted-foreground">
              {{ degradationSummary(row) }}
            </span>
          </div>
        </template>
        <template #cell-oeeRate="{ row }">{{ rateCell(row.oeeRate) }}</template>
        <template #cell-availabilityRate="{ row }">{{ rateCell(row.availabilityRate) }}</template>
        <template #cell-performanceRate="{ row }">{{ rateCell(row.performanceRate) }}</template>
        <template #cell-qualityRate="{ row }">{{ rateCell(row.qualityRate) }}</template>
      </NvDataTable>
    </section>
  </BusinessLayout>
</template>
