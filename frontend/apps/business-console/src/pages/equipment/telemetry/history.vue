<script setup lang="ts">
import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import type { DateRange, EntityPickerOption, NvDataTableColumn } from '@nerv-iip/ui'
import EquipmentScopeOverviewCard from '@/components/equipment/EquipmentScopeOverviewCard.vue'
import TelemetryEventTimeline from '@/components/equipment/TelemetryEventTimeline.vue'
import TelemetryTrendPanel from '@/components/equipment/TelemetryTrendPanel.vue'
import {
  formatTelemetryDateTime,
  projectTelemetryHistory,
} from '@/components/equipment/telemetryHistoryPresentation'
import { useBusinessTelemetryHistory } from '@/composables/useBusinessTelemetry'
import { telemetryTagLabel, useTelemetryTagCatalog } from '@/composables/useEquipmentPickerCatalog'
import { useEquipmentScopeSelection } from '@/composables/useEquipmentScopeSelection'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { friendlyErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvCascadePicker,
  NvDataTable,
  NvDateRangePicker,
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvPageHeader,
} from '@nerv-iip/ui'
import { GaugeIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '历史趋势',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const route = useRoute()
const router = useRouter()
// 默认近 7 天：一台设备的趋势要看出班次/昼夜规律，8 小时窗口太窄，一进页面就像"没数据"。
const DEFAULT_WINDOW_DAYS = 7
const { filters, historyError, historyPending, refreshHistory, visibleHistoryItems } =
  useBusinessTelemetryHistory({
    deviceAssetId: routeQuery('deviceAssetId'),
    tagKey: routeQuery('tagKey'),
    windowEndUtc: routeQuery('windowEndUtc') || defaultWindowEnd(),
    windowStartUtc: routeQuery('windowStartUtc') || defaultWindowStart(),
  })
const defaultWindowStartUtc = filters.windowStartUtc
const defaultWindowEndUtc = filters.windowEndUtc

// 车间 → 产线 → 设备 级联范围：设备层是查询范围的唯一事实源；
// 深链 query 里带了设备号时以它初始化下钻。
const { scope, levels, devicesInScope, scopeLabel, scopePending } = useEquipmentScopeSelection({
  device: routeQuery('deviceAssetId'),
})
watch(
  () => scope.value.device,
  (device) => {
    if (filters.deviceAssetId !== device) filters.deviceAssetId = device
  },
)
// 浏览器历史回退等路由驱动的设备变化，反向同步回级联选择。
watch(
  () => filters.deviceAssetId,
  (deviceAssetId) => {
    const normalized = deviceAssetId.trim()
    if (scope.value.device !== normalized) scope.value = { ...scope.value, device: normalized }
  },
)

/**
 * 采集标签跟随当前设备：选中设备后目录只列该设备已配置的测点，未选设备时列全部。
 * 换设备后旧测点若不在新设备的目录里就清空，避免筛选条件与选择器显示对不上。
 */
const { tagOptions, tagsPending } = useTelemetryTagCatalog(() => filters.deviceAssetId)
const selectedTagInCatalog = computed(() =>
  tagOptions.value.some((option) => option.value === filters.tagKey.trim()),
)
const tagPickerOptions = computed<EntityPickerOption[]>(() => {
  const tagKey = filters.tagKey.trim()
  // 目录尚未回来 / 该设备没登记标签时，仍把当前生效的筛选值显示出来，不让选择器"看着是空的"。
  if (!tagKey || selectedTagInCatalog.value) return tagOptions.value
  return [
    ...tagOptions.value,
    { value: tagKey, label: telemetryTagLabel(tagKey), hint: '当前筛选' },
  ]
})
watch([() => filters.deviceAssetId, tagOptions, tagsPending], () => {
  if (tagsPending.value || !filters.tagKey.trim()) return
  if (tagOptions.value.length > 0 && !selectedTagInCatalog.value) filters.tagKey = ''
})

const errorMessage = computed(() =>
  historyError.value
    ? friendlyErrorMessage(historyError.value, '历史遥测加载失败，请稍后重试。')
    : '',
)
const hasDeviceScope = computed(() => filters.deviceAssetId.trim().length > 0)
const projection = computed(() => projectTelemetryHistory(visibleHistoryItems.value))
/**
 * 时间范围用与可靠性指标同一套 NvUI 日期区间控件（原生 datetime-local 在各浏览器
 * 长相不一，也无法给"近 7 天"这种整段选择）。控件说本地日历日，查询要 ISO 瞬时：
 * 开始取当日 00:00，结束取次日 00:00，把尾日整天包进窗口。
 */
const windowRange = computed<DateRange>({
  get: () => ({
    // 存的是次日 00:00（尾日整天的排他上界）；回显要退回用户选的那一天本身。
    end: toDateInput(filters.windowEndUtc, -1),
    start: toDateInput(filters.windowStartUtc),
  }),
  set: (range) => {
    if (range.start) filters.windowStartUtc = fromDateInput(range.start, 0)
    if (range.end) filters.windowEndUtc = fromDateInput(range.end, 1)
  },
})

watch(
  () =>
    [
      routeQuery('deviceAssetId'),
      routeQuery('tagKey'),
      routeQuery('windowStartUtc'),
      routeQuery('windowEndUtc'),
    ] as const,
  ([deviceAssetId, tagKey, windowStartUtc, windowEndUtc]) => {
    const nextWindowStartUtc = windowStartUtc || defaultWindowStartUtc
    const nextWindowEndUtc = windowEndUtc || defaultWindowEndUtc
    if (filters.deviceAssetId !== deviceAssetId) filters.deviceAssetId = deviceAssetId
    if (filters.tagKey !== tagKey) filters.tagKey = tagKey
    if (filters.windowStartUtc !== nextWindowStartUtc) filters.windowStartUtc = nextWindowStartUtc
    if (filters.windowEndUtc !== nextWindowEndUtc) filters.windowEndUtc = nextWindowEndUtc
  },
  { immediate: true },
)

watch(
  () =>
    [filters.deviceAssetId, filters.tagKey, filters.windowStartUtc, filters.windowEndUtc] as const,
  ([deviceAssetId, tagKey, windowStartUtc, windowEndUtc]) => {
    if (
      routeQuery('deviceAssetId') === deviceAssetId.trim() &&
      routeQuery('tagKey') === tagKey.trim() &&
      routeQuery('windowStartUtc') === windowStartUtc &&
      routeQuery('windowEndUtc') === windowEndUtc
    ) {
      return
    }
    void router.replace({
      query: {
        ...route.query,
        deviceAssetId: deviceAssetId.trim() || undefined,
        tagKey: tagKey.trim() || undefined,
        windowEndUtc: windowEndUtc || undefined,
        windowStartUtc: windowStartUtc || undefined,
      },
    })
  },
  { immediate: true },
)

// 历史读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })
/** 设备展示串：名称优先，名录查不到就只显编号，不编名字。 */
function deviceLabel(code?: string | null, fallback = '无设备') {
  if (!code) return fallback
  return resolveDevice(code) ?? code
}

const columns: NvDataTableColumn<BusinessConsoleTelemetryHistoryItem>[] = [
  { key: 'occurredAtUtc', header: '时间', width: 'w-44' },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) =>
      resolveDevice(r.deviceAssetId)
        ? `${resolveDevice(r.deviceAssetId)} ${r.deviceAssetId}`
        : (r.deviceAssetId ?? '无设备'),
  },
  { key: 'tagKey', header: '采集标签', accessor: (r) => r.tagKey ?? '设备状态' },
  { key: 'value', header: '值', cellClass: 'font-medium', accessor: (r) => r.value ?? '无' },
  { key: 'itemType', header: '类型', width: 'w-24', accessor: (r) => itemTypeLabel(r.itemType) },
]

function routeQuery(key: string) {
  const value = route.query[key]
  return Array.isArray(value) ? (value[0] ?? '') : (value?.toString() ?? '')
}
function itemTypeLabel(value?: string | null) {
  const labels: Record<string, string> = {
    alarm: '报警',
    daily: '日汇总',
    hourly: '小时汇总',
    sample: '采样',
    state: '状态',
  }
  // 词表漏了就说「其他记录」，绝不把后端英文码回吐到界面上。
  return value ? (labels[value.toLowerCase()] ?? '其他记录') : '未知'
}
function rowKey(row: BusinessConsoleTelemetryHistoryItem) {
  return `${row.deviceAssetId}-${row.tagKey ?? 'state'}-${row.occurredAtUtc}-${row.value}`
}
function defaultWindowEnd() {
  const end = new Date()
  return new Date(end.getFullYear(), end.getMonth(), end.getDate() + 1).toISOString()
}
function defaultWindowStart() {
  const start = new Date()
  return new Date(
    start.getFullYear(),
    start.getMonth(),
    start.getDate() - (DEFAULT_WINDOW_DAYS - 1),
  ).toISOString()
}
function toDateInput(value: string, dayOffset = 0) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
  local.setUTCDate(local.getUTCDate() + dayOffset)
  return local.toISOString().slice(0, 10)
}
function fromDateInput(value: string, dayOffset: number) {
  const [y, m, d] = value.split('-').map(Number)
  if (!y || !m || !d) return new Date().toISOString()
  return new Date(y, m - 1, d + dayOffset).toISOString()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="历史趋势"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="hasDeviceScope ? `${visibleHistoryItems.length} 条记录` : scopeLabel"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink
            :to="{
              path: '/equipment/telemetry/oee',
              query: {
                deviceAssetId: filters.deviceAssetId,
                windowEndUtc: filters.windowEndUtc,
                windowStartUtc: filters.windowStartUtc,
              },
            }"
          >
            <GaugeIcon aria-hidden="true" />
            OEE 与可用性
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
          :disabled="historyPending || !filters.deviceAssetId.trim()"
          @click="refreshHistory"
        >
          <RefreshCwIcon aria-hidden="true" />
          查询
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-3 rounded-lg border bg-card p-4">
      <NvCascadePicker v-model="scope" :levels="levels" :aria-busy="scopePending" />
      <NvFieldGroup
        class="grid gap-3 md:grid-cols-2 xl:grid-cols-[minmax(200px,1fr)_minmax(240px,1fr)]"
      >
        <NvField>
          <NvFieldLabel for="history-tag">采集标签</NvFieldLabel>
          <NvEntityPicker
            id="history-tag"
            v-model="filters.tagKey"
            :options="tagPickerOptions"
            title="选择采集标签"
            placeholder="全部采集标签"
            :source-text="
              hasDeviceScope ? '数据来自该设备已配置的采集标签' : '数据来自设备采集标签配置'
            "
            :empty-text="
              hasDeviceScope
                ? '该设备还没有配置采集标签，请先在「采集标签」完成采集映射'
                : '暂无采集标签，请先完成设备采集映射'
            "
            :loading="tagsPending"
            clearable
            aria-label="采集标签"
          />
        </NvField>
        <NvField>
          <NvFieldLabel for="history-window">时间范围</NvFieldLabel>
          <NvDateRangePicker
            id="history-window"
            v-model="windowRange"
            placeholder="选择时间范围"
            class="w-full"
          />
        </NvField>
      </NvFieldGroup>
    </div>

    <EquipmentScopeOverviewCard
      v-if="!hasDeviceScope"
      :devices="devicesInScope"
      :scope-label="scopeLabel"
      :pending="scopePending"
      action-label="查看趋势"
      description="选中一台设备即可查看它在当前时间范围内的历史遥测趋势与原始明细。"
      @select="(code) => (scope = { ...scope, device: code })"
    />
    <div
      v-else-if="errorMessage"
      class="flex items-center justify-between gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-4"
      role="alert"
    >
      <span class="text-sm text-destructive">{{ errorMessage }}</span>
      <NvButton size="sm" type="button" variant="outline" @click="refreshHistory">重试</NvButton>
    </div>
    <div
      v-else-if="historyPending && !visibleHistoryItems.length"
      class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
      role="status"
    >
      正在加载历史遥测…
    </div>

    <template v-else>
      <TelemetryTrendPanel :projection="projection" :tag-key="filters.tagKey" />
      <TelemetryEventTimeline :timeline-items="projection.timelineItems" />

      <section class="grid gap-3" aria-labelledby="telemetry-detail-title">
        <div>
          <h2 id="telemetry-detail-title" class="text-base font-semibold text-foreground">
            原始明细
          </h2>
          <p class="mt-1 text-sm text-muted-foreground">逐条核对当前范围内的时间、值与记录类型。</p>
        </div>
        <NvDataTable
          :columns="columns"
          :rows="visibleHistoryItems"
          :row-key="rowKey"
          :loading="historyPending"
          :searchable="false"
          :column-settings="false"
          empty-message="当前设备、采集标签和时间范围内没有历史记录。"
        >
          <template #cell-occurredAtUtc="{ row }">
            {{ formatTelemetryDateTime(row.occurredAtUtc) }}
          </template>
          <template #cell-deviceAssetId="{ row }">
            <RouterLink
              :to="`/equipment/${row.deviceAssetId}`"
              class="grid leading-tight text-brand underline-offset-4 hover:underline"
            >
              <span>{{ deviceLabel(row.deviceAssetId) }}</span>
              <span v-if="resolveDevice(row.deviceAssetId)" class="text-xs text-muted-foreground">{{
                row.deviceAssetId
              }}</span>
            </RouterLink>
          </template>
        </NvDataTable>
      </section>
    </template>
  </BusinessLayout>
</template>
