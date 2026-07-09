<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesDowntimeEvents } from '@/composables/useBusinessMes'
import { mesDowntimeStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
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
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
const statusFilter = shallowRef('all')

const openCount = computed(
  () => downtimeEvents.value.filter((x) => x.status?.toLowerCase() === 'open').length,
)
const errorMessage = computed(() => formatError(downtimeEventsError.value))
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

type DowntimeRow = (typeof downtimeEvents)['value'][number]
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
    accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定',
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

    <NvSectionCards :columns="3">
      <NvSectionCard description="停机事件" :value="downtimeEventsTotal" hint="后端筛选总数" />
      <NvSectionCard description="本页未恢复" :value="openCount" hint="当前页统计" />
      <NvSectionCard
        description="本页已恢复"
        :value="downtimeEvents.length - openCount"
        hint="当前页统计"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
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
      empty-message="暂无停机事件。从工序执行记录异常会在这里汇总。"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-startedAtUtc="{ row }">{{ formatDateTime(row.startedAtUtc) }}</template>
      <template #cell-recoveredAtUtc="{ row }">{{ formatDateTime(row.recoveredAtUtc) }}</template>
    </NvDataTable>
  </BusinessLayout>
</template>
