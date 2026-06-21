<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesDispatchTasks } from '@/composables/useBusinessMes'
import { mesOperationTaskStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DropdownMenuItem,
  Field,
  FieldLabel,
  Input,
  PageHeader,
  RowActions,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon, UserCheckIcon } from 'lucide-vue-next'
import { computed, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '派工看板' } })

const {
  assignDispatchTask,
  assignDispatchTaskPending,
  dispatchTasks,
  dispatchTasksError,
  dispatchTasksPending,
  dispatchTasksTotal,
  filters,
  refreshDispatchTasks,
} = useMesDispatchTasks()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
const { workers } = useBusinessWorkers()
const { resolveWorkCenter } = useMesDisplayNames()
const statusFilter = shallowRef('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const blockedCount = computed(() => dispatchTasks.value.filter((x) => x.blockingReasons?.length).length)
const dispatchableCount = computed(() => dispatchTasks.value.filter((x) => !x.blockingReasons?.length).length)
const errorMessage = computed(() => formatError(dispatchTasksError.value))

type DispatchRow = (typeof dispatchTasks)['value'][number]

// 操作员选项：value=userId（与 assignedUserId 同源），label=姓名 · 工号。
const workerOptions = computed(() =>
  workers.value
    .filter((w) => w.userId)
    .map((w) => ({ value: w.userId as string, label: w.employeeNo ? `${w.displayName ?? w.userId} · ${w.employeeNo}` : (w.displayName ?? w.userId as string) })),
)

const columns: DataTableColumn<DispatchRow>[] = [
  { key: 'operationTaskId', header: '工序任务', cellClass: 'font-medium', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44' },
  { key: 'blockingReasons', header: '阻塞处理' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

// ── 派工（指派操作员）─────────────────────────────────────────────
const assignOpen = shallowRef(false)
const assignTarget = shallowRef<DispatchRow | null>(null)
const assignedUserId = ref('')
function canDispatch(row: DispatchRow) {
  return Boolean(row.operationTaskId) && !row.blockingReasons?.length
}
function openAssign(row: DispatchRow) {
  if (!canDispatch(row)) return
  assignTarget.value = row
  assignedUserId.value = ''
  assignOpen.value = true
}
async function confirmAssign() {
  const target = assignTarget.value
  if (!target?.operationTaskId || !assignedUserId.value) return
  try {
    await assignDispatchTask(target.operationTaskId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      assignedUserId: assignedUserId.value,
      // 设备/班次沿用任务已排程值，不在此变更。
      deviceAssetId: target.deviceAssetId ?? undefined,
      shiftId: target.shiftId ?? undefined,
      idempotencyKey: `dispatch-assign-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    })
    notifySuccess('已派工：操作员已指派。')
    assignOpen.value = false
    assignTarget.value = null
    void refreshDispatchTasks()
  }
  catch (error) {
    notifyError(error)
  }
}

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

    <Toolbar :show-search="false">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="派工状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in mesOperationTaskStatusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="dispatchTasks"
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
      <template #cell-actions="{ row }">
        <RowActions :label="`派工操作 ${row.operationTaskId ?? ''}`">
          <DropdownMenuItem :disabled="!canDispatch(row)" @click="openAssign(row)">
            <UserCheckIcon aria-hidden="true" />
            {{ canDispatch(row) ? '派工（指派操作员）' : '有阻塞，先处理' }}
          </DropdownMenuItem>
        </RowActions>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="dispatchTasksTotal" />

    <Dialog v-model:open="assignOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>派工 · 指派操作员</DialogTitle>
          <DialogDescription>
            为工单 {{ assignTarget?.workOrderId ?? '' }} 的工序任务指派操作员；设备与班次沿用排程结果。
          </DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="confirmAssign">
          <Field>
            <FieldLabel for="assign-operator">操作员 <span class="text-destructive">*</span></FieldLabel>
            <Select v-model="assignedUserId">
              <SelectTrigger id="assign-operator"><SelectValue placeholder="选择操作员" /></SelectTrigger>
              <SelectContent>
                <SelectItem v-for="o in workerOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
              </SelectContent>
            </Select>
          </Field>
          <DialogFooter>
            <Button type="button" variant="outline" @click="assignOpen = false">取消</Button>
            <Button type="submit" :disabled="assignDispatchTaskPending || !assignedUserId">
              <Spinner v-if="assignDispatchTaskPending" aria-hidden="true" />
              确认派工
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
