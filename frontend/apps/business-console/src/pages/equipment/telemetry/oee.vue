<script setup lang="ts">
import type { EquipmentRuntimeAvailabilityWindow } from '@nerv-iip/api-client'
import type { DateRange, NvDataTableColumn, NvMetricFacet, NvMetricStripCell } from '@nerv-iip/ui'
import EquipmentScopeOverviewCard from '@/components/equipment/EquipmentScopeOverviewCard.vue'
import { describeEquipmentReason } from '@/composables/useBusinessEquipment'
import { useEquipmentScopeSelection } from '@/composables/useEquipmentScopeSelection'
import {
  describeTelemetryOeeDegradation,
  describeTelemetryOeeLimitations,
  formatOeeQuantity,
  formatOeeRate,
  useBusinessTelemetryOee,
} from '@/composables/useBusinessTelemetry'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvCascadePicker,
  NvDataTable,
  NvDateRangePicker,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvSectionCards,
  NvToolbar,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
} from '@nerv-iip/ui'
import { InfoIcon, LineChartIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: 'OEE 与可用性',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const route = useRoute()
// 默认查询窗口 = 最近 7 天（演示与日常巡检的常用口径）；路由带了显式窗口时以路由为准。
const DEFAULT_WINDOW_DAYS = 7
const defaultWindow = (() => {
  const end = new Date()
  const start = new Date(end)
  start.setDate(start.getDate() - DEFAULT_WINDOW_DAYS)
  return { endUtc: end.toISOString(), startUtc: start.toISOString() }
})()
const {
  availabilityWindows,
  filters,
  oee,
  oeeError,
  oeePending,
  refreshOee,
  runtimeAvailabilityError,
} = useBusinessTelemetryOee({
  // 设备只从路由/上下文带入，不预填演示设备号：没有设备时页面给出选择引导而不是伪造范围。
  deviceAssetId: routeQuery('deviceAssetId'),
  windowEndUtc: routeQuery('windowEndUtc') || defaultWindow.endUtc,
  windowStartUtc: routeQuery('windowStartUtc') || defaultWindow.startUtc,
})

// 车间 → 产线 → 设备 级联范围：OEE 后端按单台设备计算，未下钻时展示范围设备总览
// 引导下钻，不为每台设备并发打接口伪造「全厂 OEE」。深链 query 初始化下钻设备。
const { scope, levels, devicesInScope, scopeLabel, scopePending } = useEquipmentScopeSelection({
  device: routeQuery('deviceAssetId'),
})
watch(
  () => scope.value.device,
  (device) => {
    if (filters.deviceAssetId !== device) filters.deviceAssetId = device
  },
)

const errorMessage = computed(() => formatError(oeeError.value || runtimeAvailabilityError.value))
// 口径说明收进页头的问号 tooltip；它解释指标怎么算，不是每次都要读的正文。
const limitation = describeTelemetryOeeLimitations()
const oeeDegradedReasons = computed(() =>
  (oee.value?.degradedReasons ?? []).map(describeTelemetryOeeDegradation),
)
const blockedWindowCount = computed(
  () =>
    availabilityWindows.value.filter((w) => w.availabilityStatus?.toLowerCase() === 'unavailable')
      .length,
)
const hasDeviceScope = computed(() => filters.deviceAssetId.trim().length > 0)

// 无数据时不喊「无数据」大字：值位收敛成一个安静的破折号，缺口原因交给脚注。
function rateValue(rate?: number | null) {
  return rate === null || rate === undefined ? '—' : formatOeeRate(rate)
}
function rateProgress(rate?: number | null) {
  return rate === null || rate === undefined ? 0 : Math.max(0, Math.min(100, rate * 100))
}
function rateFoot(rate?: number | null) {
  return rate === null || rate === undefined ? '当前窗口暂无可计算样本' : undefined
}

const oeeFacets = computed<NvMetricFacet[]>(() => [
  { key: 'availability', label: '可用率', value: rateValue(oee.value?.availabilityRate) },
  { key: 'performance', label: '性能率', value: rateValue(oee.value?.performanceRate) },
  { key: 'quality', label: '质量率', value: rateValue(oee.value?.qualityRate) },
])

// 计算依据里真正独有的业务事实（不与上方各率重复）留成一行摘要，重复的系数面板删除。
const basisCells = computed<NvMetricStripCell[]>(() => [
  { key: 'state', label: '状态采样', value: oee.value?.stateSampleCount ?? 0, unit: ' 条' },
  { key: 'production', label: 'MES 报工', value: oee.value?.productionFactCount ?? 0, unit: ' 条' },
  {
    key: 'expected',
    label: '理论产出',
    value: formatOeeQuantity(oee.value?.expectedOutputQuantity, oee.value?.outputUomCode),
  },
  {
    key: 'blocked',
    label: '不可用窗口',
    value: blockedWindowCount.value,
    unit: ' 段',
    valueTone: blockedWindowCount.value > 0 ? 'danger' : undefined,
  },
])

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

const columns: NvDataTableColumn<EquipmentRuntimeAvailabilityWindow>[] = [
  { key: 'availabilityStatus', header: '状态', width: 'w-24' },
  {
    key: 'reason',
    header: '原因',
    accessor: (r) => describeEquipmentReason(r.reasonCode ?? '').label,
  },
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'startUtc', header: '开始', width: 'w-44' },
  { key: 'endUtc', header: '结束', width: 'w-44' },
  { key: 'sourceReferenceId', header: '关联业务', accessor: (r) => r.sourceReferenceId ?? '无' },
]

function routeQuery(key: string) {
  const value = route.query[key]
  return Array.isArray(value) ? (value[0] ?? '') : (value?.toString() ?? '')
}
function availabilityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    available: '可用',
    unavailable: '不可用',
    unknown: '未知',
  }
  // 词表漏了就说「未知」，绝不把后端英文码回吐到界面上。
  return value ? (labels[value.toLowerCase()] ?? '未知') : '未知'
}
function availabilityVariant(value?: string | null) {
  if (value === 'available') return 'success'
  if (value === 'unavailable') return 'danger'
  return 'neutral'
}
function severityLabel(value?: string | null) {
  const labels: Record<string, string> = {
    blocked: '阻塞',
    critical: '严重',
    info: '信息',
    warning: '预警',
    major: '重要',
    minor: '次要',
  }
  // 词表漏了就说「未知级别」，绝不把后端英文码回吐到界面上。
  return value ? (labels[value.toLowerCase()] ?? '未知级别') : '未知'
}
function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'danger'
  if (severity === 'warning') return 'warning'
  return 'neutral'
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="OEE 与可用性"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="hasDeviceScope ? filters.deviceAssetId : scopeLabel"
    >
      <template #actions>
        <NvTooltipProvider>
          <NvTooltip>
            <NvTooltipTrigger as-child>
              <NvButton size="sm" type="button" variant="ghost" aria-label="查看 OEE 计算口径">
                <InfoIcon aria-hidden="true" />
                计算口径
              </NvButton>
            </NvTooltipTrigger>
            <NvTooltipContent class="max-w-sm">
              <span class="whitespace-pre-line">{{ limitation }}</span>
            </NvTooltipContent>
          </NvTooltip>
        </NvTooltipProvider>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/equipment/telemetry/history',
              query: {
                deviceAssetId: filters.deviceAssetId,
                windowEndUtc: filters.windowEndUtc,
                windowStartUtc: filters.windowStartUtc,
              },
            }"
          >
            <LineChartIcon aria-hidden="true" />
            历史趋势
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/alarm-rules"
            ><Settings2Icon aria-hidden="true" />报警规则</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="oeePending || !filters.deviceAssetId.trim()"
          @click="refreshOee"
        >
          <RefreshCwIcon aria-hidden="true" />
          查询
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvCascadePicker
          v-model="scope"
          :levels="levels"
          class="min-w-0 flex-1"
          :aria-busy="scopePending"
        />
        <NvDateRangePicker v-model="windowRange" placeholder="选择统计窗口" />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <EquipmentScopeOverviewCard
      v-if="!hasDeviceScope"
      :devices="devicesInScope"
      :scope-label="scopeLabel"
      :pending="scopePending"
      action-label="查看 OEE"
      description="OEE 按单台设备的运行状态与报工事实计算：选中一台设备即可查看它的 OEE 各率与可用性窗口。"
      @select="(code) => (scope = { ...scope, device: code })"
    />

    <template v-else>
      <NvSectionCards :columns="3">
        <NvMetricCard
          variant="target"
          label="可用率"
          :value="rateValue(oee?.availabilityRate)"
          :progress="rateProgress(oee?.availabilityRate)"
          target-label="目标 100%"
          :foot-start="rateFoot(oee?.availabilityRate) ?? '按设备运行状态时长计算'"
        />
        <NvMetricCard
          variant="target"
          label="加载率"
          :value="rateValue(oee?.loadingRate)"
          :progress="rateProgress(oee?.loadingRate)"
          target-label="目标 100%"
          :foot-start="rateFoot(oee?.loadingRate) ?? '已排除计划停机窗口'"
        />
        <NvMetricCard
          variant="target"
          label="性能率"
          :value="rateValue(oee?.performanceRate)"
          :progress="rateProgress(oee?.performanceRate)"
          target-label="目标 100%"
          :foot-start="rateFoot(oee?.performanceRate) ?? '实际产出 ÷ 理论产出'"
        />
        <NvMetricCard
          variant="target"
          label="质量率"
          :value="rateValue(oee?.qualityRate)"
          :progress="rateProgress(oee?.qualityRate)"
          target-label="目标 100%"
          :foot-start="rateFoot(oee?.qualityRate) ?? '良品 ÷ 总产出'"
        />
        <!-- OEE 是三项因子相乘的结果，不是各因子之和：用 facets 并列三个因子，禁止画成环。 -->
        <NvMetricCard
          variant="facets"
          label="OEE"
          :value="rateValue(oee?.oeeRate)"
          :facets="oeeFacets"
          class="sm:col-span-2 lg:col-span-2"
        />
      </NvSectionCards>

      <NvMetricStrip :cells="basisCells" />

      <div
        v-if="oee?.isDegraded"
        class="rounded-lg border border-warning/40 bg-warning/[0.06] p-4 text-sm"
        role="status"
      >
        <p class="font-medium text-foreground">部分指标暂缺可计算的数据</p>
        <ul class="mt-1 list-disc pl-4 text-muted-foreground">
          <li v-for="reason in oeeDegradedReasons" :key="reason">{{ reason }}</li>
        </ul>
      </div>

      <NvDataTable
        :columns="columns"
        :rows="availabilityWindows"
        :row-key="(r) => `${r.deviceAssetId}-${r.reasonCode}-${r.startUtc}`"
        :loading="oeePending"
        :searchable="false"
        :column-settings="false"
        empty-message="所选时间范围内没有设备可用性窗口记录；可换个时间范围再看。"
      >
        <template #cell-availabilityStatus="{ row }">
          <NvBadge class="rounded-sm" :variant="availabilityVariant(row.availabilityStatus)">{{
            availabilityLabel(row.availabilityStatus)
          }}</NvBadge>
        </template>
        <template #cell-reason="{ row }">
          <div class="grid gap-1">
            <span class="font-medium text-foreground">{{
              describeEquipmentReason(row.reasonCode ?? '').label
            }}</span>
            <span class="text-xs text-muted-foreground">{{
              describeEquipmentReason(row.reasonCode ?? '').nextStep
            }}</span>
          </div>
        </template>
        <template #cell-severity="{ row }">
          <NvBadge class="rounded-sm" :variant="severityVariant(row.severity)">{{
            severityLabel(row.severity)
          }}</NvBadge>
        </template>
        <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
        <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
      </NvDataTable>
    </template>
  </BusinessLayout>
</template>
