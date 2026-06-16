<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesDowntimeEvents } from '@/composables/useBusinessMes'
import { mesStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '设备与停机' } })

const { downtimeEvents, downtimeEventsError, downtimeEventsPending, downtimeEventsTotal, filters, refreshDowntimeEvents } = useMesDowntimeEvents()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
const statusFilter = shallowRef('all')

const openCount = computed(() => downtimeEvents.value.filter((x) => x.status?.toLowerCase() === 'open').length)
const errorMessage = computed(() => formatError(downtimeEventsError.value))
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

type DowntimeRow = (typeof downtimeEvents)['value'][number]
const columns: DataTableColumn<DowntimeRow>[] = [
  { key: 'downtimeEventId', header: '停机事件', cellClass: 'font-medium', accessor: (r) => r.downtimeEventId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '未关联' },
  { key: 'operationTaskId', header: '工序任务', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '未关联' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定' },
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
    <PageHeader title="设备与停机" :breadcrumbs="[{ label: '制造执行' }]" :count="`${downtimeEventsTotal} 条停机事件`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="downtimeEventsPending" @click="refreshDowntimeEvents">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="停机事件" :value="downtimeEventsTotal" hint="后端筛选总数" />
      <SectionCard description="本页未恢复" :value="openCount" hint="当前页统计" />
      <SectionCard description="本页已恢复" :value="downtimeEvents.length - openCount" hint="当前页统计" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="停机状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in mesStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="downtimeEvents"
      row-key="downtimeEventId"
      :loading="downtimeEventsPending"
      empty-message="暂无停机事件。从工序执行记录异常会在这里汇总。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-startedAtUtc="{ row }">{{ formatDateTime(row.startedAtUtc) }}</template>
      <template #cell-recoveredAtUtc="{ row }">{{ formatDateTime(row.recoveredAtUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="downtimeEventsTotal" />
  </BusinessLayout>
</template>
