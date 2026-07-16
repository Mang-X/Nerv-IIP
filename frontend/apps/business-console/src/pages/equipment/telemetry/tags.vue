<script setup lang="ts">
import type { BusinessConsoleTelemetryTagItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessTelemetryTags } from '@/composables/useBusinessTelemetry'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvToolbar,
} from '@nerv-iip/ui'
import { EyeIcon, GaugeIcon, LineChartIcon, RefreshCwIcon, Settings2Icon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '采集标签',
    requiredPermissions: ['business.iiot.telemetry.read'],
  },
})

const { filters, refreshTags, tags, tagsError, tagsPending, tagsTotal } = useBusinessTelemetryTags()
const { page, pageSize } = usePagedList(filters)

const errorMessage = computed(() => formatError(tagsError.value))

const columns: NvDataTableColumn<BusinessConsoleTelemetryTagItem>[] = [
  {
    key: 'tagKey',
    header: '采集标签',
    cellClass: 'font-medium',
    accessor: (r) => r.tagKey ?? '无标签',
  },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '无设备' },
  {
    key: 'valueType',
    header: '值类型',
    width: 'w-24',
    accessor: (r) => valueTypeLabel(r.valueType),
  },
  { key: 'unitCode', header: '单位', width: 'w-24', accessor: (r) => r.unitCode ?? '无' },
  { key: 'samplingPolicy', header: '采样策略', accessor: (r) => r.samplingPolicy ?? '未标注' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function valueTypeLabel(value?: string | null) {
  const labels: Record<string, string> = {
    bool: '布尔',
    boolean: '布尔',
    number: '数值',
    numeric: '数值',
    text: '文本',
  }
  return value ? (labels[value.toLowerCase()] ?? value) : '未知'
}
function rowKey(row: BusinessConsoleTelemetryTagItem) {
  return row.telemetryTagId ?? `${row.deviceAssetId}-${row.tagKey}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
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
        <NvInput
          v-model="filters.deviceAssetId"
          class="h-9 w-72"
          placeholder="按设备编号筛选"
          aria-label="设备编号"
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
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.deviceAssetId ?? '无设备' }}
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
