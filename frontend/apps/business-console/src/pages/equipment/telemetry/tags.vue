<script setup lang="ts">
import type { BusinessConsoleTelemetryTagItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  formatSamplingPolicy,
  formatTelemetryUnit,
  formatTelemetryValueType,
} from '@/data/businessLabels'
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
    title: '采集点位',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const { filters, refreshTags, tags, tagsError, tagsPending, tagsTotal } = useBusinessTelemetryTags()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.deviceAssetId] })
const { deviceOptions, devicesPending } = useEquipmentDeviceCatalog()

const errorMessage = computed(() => inlineErrorMessage(tagsError.value))

// 采集点位读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })
/** 设备展示串：名称优先，名录查不到就只显编号，不编名字。 */
function deviceLabel(code?: string | null, fallback = '无设备') {
  if (!code) return fallback
  return resolveDevice(code) ?? code
}

const columns: NvDataTableColumn<BusinessConsoleTelemetryTagItem>[] = [
  // 点位名称可空（#3870 新增）；没填名称的点位用编码代替，编码列照常显示。
  {
    key: 'displayName',
    header: '点位名称',
    cellClass: 'font-medium',
    accessor: (r) => r.displayName?.trim() || r.tagKey || '—',
  },
  {
    key: 'tagKey',
    header: '点位编码',
    accessor: (r) => r.tagKey ?? '—',
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
    header: '数据类型',
    width: 'w-36',
    accessor: (r) => formatTelemetryValueType(r.valueType),
  },
  // 单位是设备侧工程单位（degC / mm/s），与主数据计量单位不同，走独立词表。
  {
    key: 'unitCode',
    header: '单位',
    width: 'w-32',
    accessor: (r) => formatTelemetryUnit(r.unitCode),
  },
  // 采集周期是配置串（sample-2s / bucket=30s;raw=7d），翻成「每 2 秒采样」。
  {
    key: 'samplingPolicy',
    header: '采集周期',
    accessor: (r) => formatSamplingPolicy(r.samplingPolicy),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function rowKey(row: BusinessConsoleTelemetryTagItem) {
  return row.telemetryTagId ?? `${row.deviceAssetId}-${row.tagKey}`
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采集点位"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${tagsTotal} 个采集点位`"
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
      empty-message="暂无采集点位。请在设备详情的「采集点位」里为设备添加点位。"
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
        <NvRowActions :label="`采集点位操作 ${row.tagKey ?? ''}`">
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
              OEE 趋势与横比
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
