<script setup lang="ts">
import type { BusinessConsoleTelemetryHistoryItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessTelemetryHistory } from '@/composables/useBusinessTelemetry'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvInput, NvPageHeader, NvToolbar } from '@nerv-iip/ui'
import { GaugeIcon, RefreshCwIcon, Settings2Icon } from 'lucide-vue-next'
import { computed } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '历史趋势',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const route = useRoute()
const { filters, historyError, historyPending, refreshHistory, visibleHistoryItems } =
  useBusinessTelemetryHistory({
    deviceAssetId: routeQuery('deviceAssetId'),
    tagKey: routeQuery('tagKey'),
  })

const errorMessage = computed(() => formatError(historyError.value))

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
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
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
              query: { deviceAssetId: filters.deviceAssetId },
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

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.deviceAssetId"
          class="h-9 w-56"
          placeholder="设备编号"
          aria-label="设备编号"
        />
        <NvInput
          v-model="filters.tagKey"
          class="h-9 w-48"
          placeholder="采集标签"
          aria-label="采集标签"
        />
        <NvInput
          v-model="filters.windowStartUtc"
          class="h-9 w-64"
          placeholder="开始时间 ISO"
          aria-label="开始时间"
        />
        <NvInput
          v-model="filters.windowEndUtc"
          class="h-9 w-64"
          placeholder="结束时间 ISO"
          aria-label="结束时间"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      :columns="columns"
      :rows="visibleHistoryItems"
      :row-key="rowKey"
      :loading="historyPending"
      :searchable="false"
      :column-settings="false"
      empty-message="请输入设备编号和时间范围后查询历史趋势；采集标签留空时显示该设备窗口内的全部遥测记录。"
    >
      <template #cell-occurredAtUtc="{ row }">{{ formatDateTime(row.occurredAtUtc) }}</template>
      <template #cell-deviceAssetId="{ row }">
        <RouterLink
          :to="`/equipment/${row.deviceAssetId}`"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.deviceAssetId ?? '无设备' }}
        </RouterLink>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
