<script setup lang="ts">
import type { BusinessConsoleTelemetryTagItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { formatSamplingPolicy, formatTelemetryUnit } from '@/data/businessLabels'
import { useBusinessTelemetryTags } from '@/composables/useBusinessTelemetry'
import { useEquipmentDeviceCatalog } from '@/composables/useEquipmentPickerCatalog'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvEntityPicker,
  NvPageHeader,
  NvRowActions,
  NvToolbar,
} from '@nerv-iip/ui'
import { EyeIcon, GaugeIcon, LineChartIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '采集标签',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const { filters, refreshTags, tags, tagsError, tagsPending, tagsTotal } = useBusinessTelemetryTags()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.deviceAssetId] })
const { deviceOptions, devicesPending } = useEquipmentDeviceCatalog()

const errorMessage = computed(() => formatError(tagsError.value))

// 采集标签读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })
/** 设备展示串：名称优先，名录查不到就只显编号，不编名字。 */
function deviceLabel(code?: string | null, fallback = '无设备') {
  if (!code) return fallback
  return resolveDevice(code) ?? code
}

const columns: NvDataTableColumn<BusinessConsoleTelemetryTagItem>[] = [
  {
    key: 'tagKey',
    header: '采集标签',
    cellClass: 'font-medium',
    accessor: (r) => r.tagKey ?? '无标签',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) =>
      resolveDevice(r.deviceAssetId)
        ? `${resolveDevice(r.deviceAssetId)} ${r.deviceAssetId}`
        : (r.deviceAssetId ?? '无设备'),
  },
  {
    key: 'valueType',
    header: '值类型',
    width: 'w-24',
    accessor: (r) => valueTypeLabel(r.valueType),
  },
  // 单位是设备侧工程单位（degC / mm/s），与主数据计量单位不同，走独立词表。
  {
    key: 'unitCode',
    header: '单位',
    width: 'w-32',
    accessor: (r) => formatTelemetryUnit(r.unitCode),
  },
  // 采样策略是配置串（sample-2s / bucket=30s;raw=7d），翻成「每 2 秒采样」。
  {
    key: 'samplingPolicy',
    header: '采样策略',
    accessor: (r) => formatSamplingPolicy(r.samplingPolicy),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function valueTypeLabel(value?: string | null) {
  const labels: Record<string, string> = {
    bool: '布尔',
    boolean: '布尔',
    number: '数值',
    numeric: '数值',
    decimal: '数值',
    int: '整数',
    integer: '整数',
    text: '文本',
    string: '文本',
  }
  // 词表漏了就说「未知类型」，绝不把后端英文码回吐到界面上。
  return value ? (labels[value.toLowerCase()] ?? '未知类型') : '未知'
}
function rowKey(row: BusinessConsoleTelemetryTagItem) {
  return row.telemetryTagId ?? `${row.deviceAssetId}-${row.tagKey}`
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采集标签"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${tagsTotal} 个采集标签`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/alarm-rules"
            ><Settings2Icon aria-hidden="true" />报警规则</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/equipment/telemetry/history"
            ><LineChartIcon aria-hidden="true" />历史趋势</RouterLink
          >
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="tagsPending"
          @click="refreshTags"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="filters.deviceAssetId"
          class="w-72"
          :options="deviceOptions"
          title="选择设备"
          placeholder="全部设备"
          source-text="数据来自基础数据设备资产"
          empty-text="暂无设备资产，请先在基础数据登记设备"
          :loading="devicesPending"
          clearable
          aria-label="设备"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="tagsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="tags"
      :row-key="rowKey"
      :loading="tagsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无采集标签。请先完成设备采集映射，再查看历史趋势和报警规则。"
    >
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
      <template #cell-actions="{ row }">
        <NvRowActions :label="`采集标签操作 ${row.tagKey ?? ''}`">
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{
                path: '/equipment/telemetry/history',
                query: { deviceAssetId: row.deviceAssetId, tagKey: row.tagKey },
              }"
            >
              <LineChartIcon aria-hidden="true" />
              查看趋势
            </RouterLink>
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{
                path: '/equipment/telemetry/oee',
                query: { deviceAssetId: row.deviceAssetId },
              }"
            >
              <GaugeIcon aria-hidden="true" />
              OEE 与可用性
            </RouterLink>
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink :to="`/equipment/${row.deviceAssetId}`"
              ><EyeIcon aria-hidden="true" />设备详情</RouterLink
            >
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
