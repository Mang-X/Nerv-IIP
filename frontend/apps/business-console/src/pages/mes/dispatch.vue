<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { describeMesReadinessReason, useMesDispatchTasks } from '@/composables/useBusinessMes'
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
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref } from 'vue'

definePage({ meta: { requiresAuth: true, title: '派工看板' } })

const { dispatchTasks, dispatchTasksError, dispatchTasksPending, dispatchTasksTotal, filters, refreshDispatchTasks } = useMesDispatchTasks()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const keyword = ref('')
const filtered = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  if (!kw) return dispatchTasks.value
  return dispatchTasks.value.filter((r) =>
    [r.operationTaskId, r.workOrderId, r.workCenterId, r.deviceAssetId, r.shiftId].some((v) => (v ?? '').toLowerCase().includes(kw)),
  )
})

const blockedCount = computed(() => dispatchTasks.value.filter((x) => x.blockingReasons?.length).length)
const dispatchableCount = computed(() => dispatchTasks.value.filter((x) => !x.blockingReasons?.length).length)
const errorMessage = computed(() => formatError(dispatchTasksError.value))

type DispatchRow = (typeof dispatchTasks)['value'][number]
const columns: DataTableColumn<DispatchRow>[] = [
  { key: 'operationTaskId', header: '工序任务', cellClass: 'font-medium', accessor: (r) => r.operationTaskId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '未指定' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44' },
  { key: 'blockingReasons', header: '阻塞处理' },
]

function readinessList(reasons?: string[] | null) {
  return (reasons ?? []).map(describeMesReadinessReason)
}
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
    <PageHeader title="派工看板" :breadcrumbs="[{ label: '制造执行' }]" :count="`${dispatchTasksTotal} 个待派工序`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="dispatchTasksPending" @click="refreshDispatchTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="派工任务" :value="dispatchTasksTotal" hint="后端筛选总数" />
      <SectionCard description="本页可派工" :value="dispatchableCount" hint="当前页统计" />
      <SectionCard description="本页有阻塞" :value="blockedCount" hint="当前页统计" />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索工序、工单、工作中心、设备">
      <template #filters>
        <Input v-model="filters.status" class="h-9 w-32" placeholder="状态（可选）" aria-label="派工状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="filtered"
      row-key="operationTaskId"
      :loading="dispatchTasksPending"
      empty-message="暂无待派工序。工单释放并排程后，待派工序会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-plannedStartUtc="{ row }">{{ formatDateTime(row.plannedStartUtc) }}</template>
      <template #cell-blockingReasons="{ row }">
        <div v-if="row.blockingReasons?.length" class="grid gap-2">
          <div v-for="reason in readinessList(row.blockingReasons)" :key="`${row.operationTaskId}-${reason.code}`" class="grid gap-0.5">
            <StatusBadge :label="reason.label" tone="warning" />
            <p class="text-xs text-muted-foreground">{{ reason.nextStep }}</p>
          </div>
        </div>
        <span v-else class="text-muted-foreground">可派工</span>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="dispatchTasksTotal" />
  </BusinessLayout>
</template>
