<script setup lang="ts">
import type {
  BusinessConsoleRunScheduleRequest,
  BusinessConsoleScheduledOperation,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesSchedules } from '@/composables/useBusinessMes'
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
  Field,
  FieldGroup,
  FieldLabel,
  PageHeader,
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
} from '@nerv-iip/ui'
import { PlayIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '规则排程' } })

const { lastSchedule, runSchedule, runScheduleError, runSchedulePending } = useMesSchedules()

const runSuccess = shallowRef('')
const scheduleSheetOpen = shallowRef(false)

const runForm = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  trigger: 'Manual',
})

const assignments = computed<BusinessConsoleScheduledOperation[]>(() => lastSchedule.value?.assignments ?? [])
const affectedWorkOrderIds = computed(() => lastSchedule.value?.affectedWorkOrderIds ?? [])
const errorMessage = computed(() => formatError(runScheduleError.value))
const canRunSchedule = computed(
  () => isNonEmpty(runForm.organizationId) && isNonEmpty(runForm.environmentId) && isNonEmpty(runForm.trigger),
)

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedAssignments = computed(() => {
  const start = (page.value - 1) * pageSizeNumber.value
  return assignments.value.slice(start, start + pageSizeNumber.value)
})
watch([pageSize, () => assignments.value.length], () => {
  page.value = 1
})

const columns: DataTableColumn<BusinessConsoleScheduledOperation>[] = [
  { key: 'workOrderId', header: '工单', cellClass: 'font-medium', accessor: (r) => r.workOrderId ?? '无' },
  { key: 'operationTaskId', header: '工序', accessor: (r) => r.operationTaskId ?? '无' },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterId ?? '无' },
  { key: 'startUtc', header: '开始', width: 'w-44' },
  { key: 'endUtc', header: '结束', width: 'w-44' },
  { key: 'reason', header: '原因', accessor: (r) => r.reason ?? '无' },
]

function triggerLabel(value?: string | null) {
  if (value === 'Manual') return '手动'
  if (value === 'RushOrder') return '急单'
  if (value === 'AssetUnavailable') return '设备不可用'
  if (value === 'AssetRestored') return '设备恢复'
  return value ?? '尚未运行'
}

async function submitScheduleRun() {
  if (!canRunSchedule.value) return
  const body: BusinessConsoleRunScheduleRequest = {
    organizationId: runForm.organizationId.trim(),
    environmentId: runForm.environmentId.trim(),
    trigger: runForm.trigger.trim(),
  }
  const response = await runSchedule(body)
  runSuccess.value = `排程运行 ${response?.data?.scheduleVersion ?? body.trigger} 已完成。`
  scheduleSheetOpen.value = false
}

function rowKey(item: BusinessConsoleScheduledOperation) {
  return `${item.workOrderId ?? 'wo'}:${item.operationTaskId ?? ''}`
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="规则排程" :breadcrumbs="[{ label: '制造执行' }]" :count="`${assignments.length} 条分配`">
      <template #actions>
        <Button size="sm" type="button" @click="scheduleSheetOpen = true">
          <PlayIcon aria-hidden="true" />
          运行排程
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard
        description="排程版本"
        :value="lastSchedule?.scheduleVersion ?? '无'"
        :hint="lastSchedule?.trigger ? triggerLabel(lastSchedule.trigger) : '尚未运行'"
      />
      <SectionCard description="工序分配" :value="assignments.length" hint="返回的工序分配行" />
      <SectionCard description="影响工单" :value="affectedWorkOrderIds.length" hint="本次受影响工单号" />
    </SectionCards>

    <div class="flex items-center justify-between">
      <span class="text-sm font-semibold text-foreground">排程结果</span>
      <span class="text-sm text-muted-foreground">{{ formatDateTime(lastSchedule?.scheduledAtUtc) }}</span>
    </div>

    <DataTable
      :columns="columns"
      :rows="pagedAssignments"
      :row-key="rowKey"
      :loading="runSchedulePending"
      empty-message="尚无排程结果。运行排程后，这里会显示工序的工作中心与起止时间。"
    >
      <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
      <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="assignments.length" />

    <div v-if="affectedWorkOrderIds.length" class="rounded-lg border bg-background p-4">
      <h2 class="text-sm font-semibold text-foreground">受影响工单</h2>
      <div class="mt-3 flex flex-wrap gap-2">
        <StatusBadge v-for="workOrderId in affectedWorkOrderIds" :key="workOrderId" :label="workOrderId" tone="neutral" />
      </div>
    </div>

    <Dialog v-model:open="scheduleSheetOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>运行规则排程</DialogTitle>
          <DialogDescription>规则排程会重新计算工序分配，运行前请确认触发来源。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitScheduleRun">
          <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
          <p v-if="runSuccess" class="text-sm text-success" role="status">{{ runSuccess }}</p>

          <FieldGroup class="grid gap-3">
            <Field>
              <FieldLabel for="schedule-trigger">触发来源</FieldLabel>
              <Select v-model="runForm.trigger">
                <SelectTrigger id="schedule-trigger" aria-label="排程触发来源"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Manual">手动</SelectItem>
                  <SelectItem value="RushOrder">急单</SelectItem>
                  <SelectItem value="AssetUnavailable">设备不可用</SelectItem>
                  <SelectItem value="AssetRestored">设备恢复</SelectItem>
                </SelectContent>
              </Select>
            </Field>
          </FieldGroup>

          <DialogFooter>
            <Button type="button" variant="outline" @click="scheduleSheetOpen = false">取消</Button>
            <Button type="submit" :disabled="runSchedulePending || !canRunSchedule">
              <Spinner v-if="runSchedulePending" aria-hidden="true" />
              <PlayIcon v-else aria-hidden="true" />
              运行排程
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
