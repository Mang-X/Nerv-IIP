<script setup lang="ts">
import type {
  BusinessConsoleRunScheduleRequest,
  BusinessConsoleScheduledOperation,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useMesSchedules } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
} from '@nerv-iip/ui'
import { CalendarCogIcon, PlayIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '规则排程（过渡）', requiredPermissions: ['business.mes.schedules.read', 'business.mes.schedules.manage'] } })

const { lastSchedule, runSchedule, runScheduleError, runSchedulePending } = useMesSchedules()
const businessContext = useBusinessContextStore()

const runSuccess = shallowRef('')
const scheduleSheetOpen = shallowRef(false)

const runForm = reactive({
  organizationId: businessContext.organizationId,
  environmentId: businessContext.environmentId,
  trigger: 'Manual',
})

watch(
  () => [businessContext.organizationId, businessContext.environmentId] as const,
  ([organizationId, environmentId]) => {
    runForm.organizationId = organizationId
    runForm.environmentId = environmentId
  },
  { flush: 'sync', immediate: true },
)

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

const columns: DataTableProColumn<BusinessConsoleScheduledOperation>[] = [
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
  runSuccess.value = `规则排程 ${response?.data?.scheduleVersion ?? body.trigger} 已完成。`
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
    <PageHeader title="规则排程（过渡）" :breadcrumbs="[{ label: '制造执行' }]" :count="`${assignments.length} 条分配`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/scheduling">
            <CalendarCogIcon aria-hidden="true" />
            排产工作台
          </RouterLink>
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="scheduleSheetOpen = true">
          <PlayIcon aria-hidden="true" />
          运行规则排程
        </ButtonPro>
      </template>
    </PageHeader>

    <p class="max-w-3xl text-sm leading-6 text-muted-foreground">
      此页保留 MES 执行域内的规则排程过渡和诊断结果，用于查看或手动触发工序分配。正式 APS / 甘特、方案发布和冲突治理请进入排产工作台。
    </p>

    <SectionCards :columns="3">
      <SectionCard
        description="规则版本"
        :value="lastSchedule?.scheduleVersion ?? '无'"
        :hint="lastSchedule?.trigger ? triggerLabel(lastSchedule.trigger) : '尚未运行'"
      />
      <SectionCard description="规则分配" :value="assignments.length" hint="执行域返回的工序分配行" />
      <SectionCard description="影响工单" :value="affectedWorkOrderIds.length" hint="本次受影响工单号" />
    </SectionCards>

    <div class="flex items-center justify-between">
      <span class="text-sm font-semibold text-foreground">规则排程结果</span>
      <span class="text-sm text-muted-foreground">{{ formatDateTime(lastSchedule?.scheduledAtUtc) }}</span>
    </div>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="assignments.length"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="pagedAssignments"
      :row-key="rowKey"
      :loading="runSchedulePending"
      :searchable="false"
      :column-settings="false"
      empty-message="尚无规则排程结果。运行规则排程后，这里会显示工序的工作中心与起止时间。"
    >
      <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
      <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
    </DataTablePro>


    <div v-if="affectedWorkOrderIds.length" class="rounded-lg border bg-background p-4">
      <h2 class="text-sm font-semibold text-foreground">受影响工单</h2>
      <div class="mt-3 flex flex-wrap gap-2">
        <StatusBadgePro v-for="workOrderId in affectedWorkOrderIds" :key="workOrderId" :label="workOrderId" tone="neutral" />
      </div>
    </div>

    <DialogPro v-model:open="scheduleSheetOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>运行规则排程</DialogProTitle>
          <DialogProDescription>规则排程只重新计算 MES 执行域工序分配；正式排产方案、甘特和发布动作请在排产工作台处理。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitScheduleRun">
          <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>
          <p v-if="!isNonEmpty(runForm.organizationId) || !isNonEmpty(runForm.environmentId)" class="text-sm text-muted-foreground" role="status">
            请先完成业务上下文选择。
          </p>
          <p v-if="runSuccess" class="text-sm text-success" role="status">{{ runSuccess }}</p>

          <FieldProGroup class="grid gap-3">
            <FieldPro>
              <FieldProLabel for="schedule-trigger">触发来源</FieldProLabel>
              <SelectPro v-model="runForm.trigger">
                <SelectProTrigger id="schedule-trigger" aria-label="排程触发来源"><SelectProValue /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem value="Manual">手动</SelectProItem>
                  <SelectProItem value="RushOrder">急单</SelectProItem>
                  <SelectProItem value="AssetUnavailable">设备不可用</SelectProItem>
                  <SelectProItem value="AssetRestored">设备恢复</SelectProItem>
                </SelectProContent>
              </SelectPro>
            </FieldPro>
          </FieldProGroup>

          <DialogProFooter>
            <ButtonPro type="button" variant="outline" @click="scheduleSheetOpen = false">取消</ButtonPro>
            <ButtonPro type="submit" :disabled="runSchedulePending || !canRunSchedule">
              <Spinner v-if="runSchedulePending" aria-hidden="true" />
              <PlayIcon v-else aria-hidden="true" />
              运行规则排程
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
