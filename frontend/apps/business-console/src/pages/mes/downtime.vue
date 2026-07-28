<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesDowntimeEvents } from '@/composables/useBusinessMes'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import {
  mesDowntimeStatusOptions,
  useMesReferenceLabels,
} from '@/composables/mes/useMesReferenceLabels'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '设备与停机',
    requiredPermissions: ['business.mes.downtime.read'],
  },
})

const {
  downtimeEvents,
  downtimeEventsError,
  downtimeEventsPending,
  downtimeEventsTotal,
  filters,
  refreshDowntimeEvents,
} = useMesDowntimeEvents()
const { keyword } = useMesKeywordFilter(filters)
const { statusLabel } = useMesReferenceLabels()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})
const statusFilter = shallowRef('all')

const openCount = computed(
  () => downtimeEvents.value.filter((x) => x.status?.toLowerCase() === 'open').length,
)
// 停机的决策点是「还有多少台没恢复」——一张构成卡把总量与恢复进度放在一起。
const downtimeSegments = computed(() =>
  pagedBreakdownSegments(downtimeEventsTotal.value, [
    { key: 'open', label: '未恢复', value: openCount.value, tone: 'danger' },
    {
      key: 'recovered',
      label: '已恢复',
      value: downtimeEvents.value.length - openCount.value,
      tone: 'success',
    },
  ]),
)
const errorMessage = computed(() => formatError(downtimeEventsError.value))
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

// 停机读面只回设备编码，中文设备名在设备台账里，按编码 join 出来。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })

type DowntimeRow = (typeof downtimeEvents)['value'][number]

function deviceCode(row: DowntimeRow) {
  return row.deviceAssetCode ?? row.deviceAssetId ?? ''
}
function deviceName(row: DowntimeRow) {
  return row.deviceAssetName ?? resolveDevice(deviceCode(row))
}
/** 「名称 编码」纯文本，供排序 / 导出用；名录查不到就只有编码，不编名字。 */
function deviceText(row: DowntimeRow) {
  const code = deviceCode(row)
  if (!code) return '未指定'
  const name = deviceName(row)
  return name ? `${name} ${code}` : code
}

const columns: NvDataTableColumn<DowntimeRow>[] = [
  {
    key: 'downtimeEventId',
    header: '停机事件',
    cellClass: 'font-medium',
    accessor: (r) => r.downtimeEventId ?? '无',
  },
  {
    key: 'workOrderId',
    header: '工单',
    accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '未关联',
  },
  {
    key: 'operationTaskId',
    header: '工序任务',
    accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '未关联',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => deviceText(r),
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'startedAtUtc', header: '开始', width: 'w-44' },
  { key: 'recoveredAtUtc', header: '恢复', width: 'w-44' },
]

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
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
      title="设备与停机"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${downtimeEventsTotal} 条停机事件`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="downtimeEventsPending"
          @click="refreshDowntimeEvents"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2">
      <NvMetricCard
        variant="breakdown"
        label="停机事件"
        :value="downtimeEventsTotal"
        unit="起"
        :segments="downtimeSegments"
      />
      <NvMetricCard
        variant="alert"
        label="未恢复停机"
        :value="openCount"
        unit="起"
        :tone="openCount > 0 ? 'danger' : 'neutral'"
        :status="
          openCount > 0
            ? { label: '需跟进恢复', tone: 'danger' }
            : { label: '全部恢复', tone: 'success' }
        "
        :foot-start="
          openCount > 0 ? '优先处理仍在影响工序执行的停机事件。' : '当前没有未恢复的停机事件。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="工单 / 工序 / 设备"
          aria-label="搜索停机事件"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="停机状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesDowntimeStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="downtimeEventsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="downtimeEvents"
      row-key="downtimeEventId"
      :loading="downtimeEventsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无停机事件。先在工序执行登记设备异常，再回到这里跟进恢复与影响范围。"
    >
      <template #cell-deviceAssetId="{ row }">
        <CodeWithNameCell :code="deviceCode(row)" :name="deviceName(row)" fallback="未指定" />
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
      </template>
      <template #cell-startedAtUtc="{ row }">{{ formatDateTime(row.startedAtUtc) }}</template>
      <template #cell-recoveredAtUtc="{ row }">{{ formatDateTime(row.recoveredAtUtc) }}</template>
    </NvDataTable>
  </BusinessLayout>
</template>
