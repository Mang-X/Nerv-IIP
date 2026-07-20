<script setup lang="ts">
import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import TelemetryEventTimeline from '@/components/equipment/TelemetryEventTimeline.vue'
import TelemetryTrendPanel from '@/components/equipment/TelemetryTrendPanel.vue'
import {
  formatTelemetryDateTime,
  projectTelemetryHistory,
} from '@/components/equipment/telemetryHistoryPresentation'
import { useBusinessTelemetryHistory } from '@/composables/useBusinessTelemetry'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { friendlyErrorMessage } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
} from '@nerv-iip/ui'
import { GaugeIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed, nextTick, shallowRef, watch } from 'vue'
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
const { filters, historyError, historyPending, refreshHistory, visibleHistoryItems } =
  useBusinessTelemetryHistory({
    deviceAssetId: routeQuery('deviceAssetId'),
    tagKey: routeQuery('tagKey'),
    windowEndUtc: routeQuery('windowEndUtc') || undefined,
    windowStartUtc: routeQuery('windowStartUtc') || undefined,
  })
const defaultWindowStartUtc = filters.windowStartUtc
const defaultWindowEndUtc = filters.windowEndUtc
const windowInputError = shallowRef('')

const errorMessage = computed(() =>
  historyError.value
    ? friendlyErrorMessage(historyError.value, '历史遥测加载失败，请稍后重试。')
    : '',
)
const hasDeviceScope = computed(() => filters.deviceAssetId.trim().length > 0)
const projection = computed(() => projectTelemetryHistory(visibleHistoryItems.value))
const windowStartLocal = shallowRef(toLocalDateTime(filters.windowStartUtc))
const windowEndLocal = shallowRef(toLocalDateTime(filters.windowEndUtc))

function updateWindowStart(value: string | number) {
  updateWindow(
    String(value),
    windowStartLocal,
    () => filters.windowStartUtc,
    (parsed) => {
      filters.windowStartUtc = parsed
    },
  )
}

function updateWindowEnd(value: string | number) {
  updateWindow(
    String(value),
    windowEndLocal,
    () => filters.windowEndUtc,
    (parsed) => {
      filters.windowEndUtc = parsed
    },
  )
}

function updateWindow(
  value: string,
  localValue: { value: string },
  currentUtc: () => string,
  commit: (parsed: string) => void,
) {
  const parsed = toIsoDateTime(value)
  if (!parsed) {
    windowInputError.value = '时间范围不能为空或无效，请选择有效的本地日期时间。'
    localValue.value = value
    void nextTick(() => {
      localValue.value = toLocalDateTime(currentUtc())
    })
    return
  }
  windowInputError.value = ''
  localValue.value = value
  commit(parsed)
}

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
  () => [filters.windowStartUtc, filters.windowEndUtc] as const,
  ([windowStartUtc, windowEndUtc]) => {
    windowStartLocal.value = toLocalDateTime(windowStartUtc)
    windowEndLocal.value = toLocalDateTime(windowEndUtc)
  },
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

const columns: NvDataTableColumn<BusinessConsoleTelemetryHistoryItem>[] = [
  { key: 'occurredAtUtc', header: '时间', width: 'w-44' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '无设备' },
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
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function rowKey(row: BusinessConsoleTelemetryHistoryItem) {
  return `${row.deviceAssetId}-${row.tagKey ?? 'state'}-${row.occurredAtUtc}-${row.value}`
}
function toLocalDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}
function toIsoDateTime(value: string) {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date.toISOString()
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="历史趋势"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${visibleHistoryItems.length} 条记录`"
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

    <NvFieldGroup
      class="grid gap-3 rounded-lg border bg-card p-4 md:grid-cols-2 xl:grid-cols-[minmax(220px,1fr)_minmax(200px,1fr)_220px_220px]"
    >
      <NvField>
        <NvFieldLabel for="history-device">设备</NvFieldLabel>
        <NvInput
          id="history-device"
          v-model="filters.deviceAssetId"
          placeholder="设备编号"
          aria-label="设备编号"
        />
      </NvField>
      <NvField>
        <NvFieldLabel for="history-tag">采集标签</NvFieldLabel>
        <NvInput
          id="history-tag"
          v-model="filters.tagKey"
          placeholder="采集标签"
          aria-label="采集标签"
        />
      </NvField>
      <NvField>
        <NvFieldLabel for="history-start">开始时间</NvFieldLabel>
        <NvInput
          id="history-start"
          :model-value="windowStartLocal"
          type="datetime-local"
          aria-label="开始时间"
          @update:model-value="updateWindowStart"
        />
      </NvField>
      <NvField>
        <NvFieldLabel for="history-end">结束时间</NvFieldLabel>
        <NvInput
          id="history-end"
          :model-value="windowEndLocal"
          type="datetime-local"
          aria-label="结束时间"
          @update:model-value="updateWindowEnd"
        />
      </NvField>
    </NvFieldGroup>
    <p v-if="windowInputError" class="text-sm text-destructive" role="alert">
      {{ windowInputError }}
    </p>

    <div
      v-if="!hasDeviceScope"
      class="rounded-lg border border-dashed p-6 text-center text-sm text-muted-foreground"
    >
      请选择设备并确认时间范围后查询历史遥测。
    </div>
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
              class="text-brand underline-offset-4 hover:underline"
            >
              {{ row.deviceAssetId ?? '无设备' }}
            </RouterLink>
          </template>
        </NvDataTable>
      </section>
    </template>
  </BusinessLayout>
</template>
