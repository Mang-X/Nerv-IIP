<script setup lang="ts">
import type { BusinessConsoleTelemetryOeeAggregateDimension } from '@nerv-iip/api-client'
import type { DateRange, NvDataTableColumn } from '@nerv-iip/ui'
import {
  describeTelemetryOeeDegradation,
  formatOeeRate,
  useBusinessTelemetryOeeAggregates,
  useBusinessTelemetryOeeTrend,
} from '@/composables/useBusinessTelemetry'
import { usePagedList } from '@/composables/usePagedList'
import {
  localDatesFromOeeWindow,
  oeeWindowFromLocalDates,
  presentOeeReport,
  recentOeeWindow,
  type OeeTableRow,
} from '@/pages/equipment/telemetry/oeePresentation'
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
const defaultWindow = recentOeeWindow(7)

const dimensions: Array<{ value: BusinessConsoleTelemetryOeeAggregateDimension; label: string }> = [
  { value: 'day', label: '按天趋势' },
  { value: 'shift', label: '按班次对比' },
  { value: 'workCenter', label: '按工作中心对比' },
  { value: 'line', label: '按产线对比' },
  { value: 'workshop', label: '按车间对比' },
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
  windowEndUtc: routeQuery('windowEndUtc') || defaultWindow.windowEndUtc,
  windowStartUtc: routeQuery('windowStartUtc') || defaultWindow.windowStartUtc,
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
  () => dimensions.find((item) => item.value === filters.dimension)?.label ?? '',
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

const columns = computed<NvDataTableColumn<OeeTableRow>[]>(() => [
  { key: 'primaryLabel', header: '对比对象' },
  { key: 'hierarchyLabel', header: '站点 / 层级' },
  ...(filters.dimension === 'day' || filters.dimension === 'shift'
    ? [{ key: 'businessDateLabel', header: '业务日' }]
    : []),
  { key: 'windowLabel', header: '统计时段' },
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
  get: () => localDatesFromOeeWindow(filters.windowStartUtc, filters.windowEndUtc),
  set: (range) => {
    if (!range.start || !range.end) return
    Object.assign(filters, oeeWindowFromLocalDates(range.start, range.end))
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
function rateCell(value: number | null | undefined) {
  return value == null ? '—' : formatOeeRate(value)
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
          <NvFieldLabel>统计时段（最多 31 天）</NvFieldLabel>
          <NvDateRangePicker v-model="windowRange" placeholder="选择统计时段" />
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

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <section
      v-if="filters.dimension === 'day' && !errorMessage"
      class="grid gap-3 rounded-lg border bg-card p-4"
    >
      <div>
        <h2 class="text-sm font-semibold text-foreground">OEE 与可用率、性能率、质量率按天趋势</h2>
        <p class="text-sm text-muted-foreground">
          每天按工厂当地时间和换日时刻划分；趋势覆盖所选时段的全部日期，不随下方明细翻页变化。
        </p>
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
        所选时段没有完整的 OEE 数据，无法绘制趋势；缺数原因见下方明细。
      </div>
      <template v-else-if="!trendErrorMessage">
        <p class="text-sm text-muted-foreground" role="status">
          所选时段共 {{ reportPresentation.trendBucketCount }} 条日统计，按
          {{ reportPresentation.trendGroups.length }} 个站点分别展示。
        </p>
        <p
          v-if="reportPresentation.omittedTrendBucketCount > 0"
          class="text-sm text-warning"
          role="status"
        >
          {{ reportPresentation.omittedTrendBucketCount }} 条日统计缺少数据，图中未按 0%
          绘制；原因见下方明细。
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
                {{ group.bucketCount }} 条日统计，{{ group.pointCount }} 条数据完整<span
                  v-if="group.omittedCount > 0"
                  >，{{ group.omittedCount }} 条缺数、原因见下方明细</span
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
                  <h4 v-if="group.segments.length > 1" class="text-sm font-medium text-foreground">
                    第 {{ segment.ordinal }} 段
                  </h4>
                  <p class="text-xs text-muted-foreground">
                    业务日 {{ segment.businessDateStartLabel }} 至
                    {{ segment.businessDateEndLabel }}；{{ segment.bucketCount }} 条日统计，{{
                      segment.pointCount
                    }}
                    条数据完整<span v-if="segment.omittedCount > 0"
                      >，{{ segment.omittedCount }} 条缺数</span
                    >。
                  </p>
                </div>
                <div
                  v-if="segment.runs.length === 0"
                  class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
                >
                  这一段没有完整数据，无法绘制趋势；原因见下方明细。
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
                        单日 · {{ run.points[0]?.businessDateLabel }}
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
        <h2 class="text-sm font-semibold text-foreground">{{ dimensionLabel }}明细</h2>
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
        empty-message="所选时段和筛选范围内没有 OEE 数据。"
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
