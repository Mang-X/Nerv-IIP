<script setup lang="ts">
import type {
  BusinessConsoleMesScheduleResultRow,
  BusinessConsoleRunScheduleRequest,
  BusinessConsoleScheduledOperation,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useMesSchedules } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { CalendarCogIcon, PlayIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '规则排程',
    requiredPermissions: ['business.mes.schedules.read', 'business.mes.schedules.manage'],
  },
})

const {
  lastSchedule,
  scheduleHistory,
  scheduleHistoryTotal,
  scheduleHistoryPending,
  runSchedule,
  runScheduleError,
  runSchedulePending,
} = useMesSchedules()
const businessContext = useBusinessContextStore()

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

// 选中的历史排程；未选时看最新一次。刚跑完那次会随历史重取出现在首行，
// 重取到达前先用本次运行的即时结果兜底，页面不会闪空。
const selectedVersion = shallowRef<number | undefined>()
const selectedRun = computed<BusinessConsoleMesScheduleResultRow | undefined>(() => {
  const rows = scheduleHistory.value
  if (selectedVersion.value !== undefined) {
    const matched = rows.find((row) => row.scheduleVersion === selectedVersion.value)
    if (matched) return matched
  }
  return rows[0]
})
const activeVersion = computed(
  () => selectedRun.value?.scheduleVersion ?? lastSchedule.value?.scheduleVersion,
)
const activeTrigger = computed(() => selectedRun.value?.trigger ?? lastSchedule.value?.trigger)
const activeScheduledAtUtc = computed(
  () => selectedRun.value?.scheduledAtUtc ?? lastSchedule.value?.scheduledAtUtc,
)
const assignments = computed<BusinessConsoleScheduledOperation[]>(
  () => selectedRun.value?.assignments ?? lastSchedule.value?.assignments ?? [],
)
const affectedWorkOrderIds = computed(
  () => selectedRun.value?.affectedWorkOrderIds ?? lastSchedule.value?.affectedWorkOrderIds ?? [],
)
const canRunSchedule = computed(
  () =>
    isNonEmpty(runForm.organizationId) &&
    isNonEmpty(runForm.environmentId) &&
    isNonEmpty(runForm.trigger),
)

const scheduleCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'version',
    label: '规则版本',
    value: activeVersion.value ?? '尚未运行',
    meta: activeTrigger.value ? `${triggerLabel(activeTrigger.value)}触发` : undefined,
  },
  {
    key: 'assignments',
    label: '工序分配',
    value: assignments.value.length,
    unit: '条',
  },
  {
    key: 'affected',
    label: '影响工单',
    value: affectedWorkOrderIds.value.length,
    unit: '张',
  },
  {
    key: 'history',
    label: '历史运行',
    value: scheduleHistoryTotal.value,
    unit: '次',
  },
])

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

const columns: NvDataTableColumn<BusinessConsoleScheduledOperation>[] = [
  {
    key: 'workOrderId',
    header: '工单',
    cellClass: 'font-medium',
    accessor: (r) => r.workOrderId ?? '无',
  },
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
  // 结果一律 toast，弹窗内不留常驻结果条；成功即关闭，结果在下方分配表里看。
  try {
    const response = await runSchedule(body)
    scheduleSheetOpen.value = false
    notifySuccess(`规则排程已完成（版本 ${response?.data?.scheduleVersion ?? body.trigger}）。`)
  } catch (error) {
    notifyError(error ?? runScheduleError.value, '运行规则排程失败，请稍后重试。')
  }
}

const historyColumns: NvDataTableColumn<BusinessConsoleMesScheduleResultRow>[] = [
  {
    key: 'scheduleVersion',
    header: '版本',
    width: 'w-24',
    cellClass: 'font-medium',
    accessor: (r) => (r.scheduleVersion === undefined ? '无' : `v${r.scheduleVersion}`),
  },
  { key: 'trigger', header: '触发来源', width: 'w-32', accessor: (r) => triggerLabel(r.trigger) },
  { key: 'scheduledAtUtc', header: '排程时间', width: 'w-48' },
  { key: 'assignmentCount', header: '工序分配', accessor: (r) => `${r.assignmentCount ?? 0} 条` },
  {
    key: 'affectedWorkOrderCount',
    header: '影响工单',
    accessor: (r) => `${r.affectedWorkOrderCount ?? 0} 张`,
  },
]

function historyRowKey(item: BusinessConsoleMesScheduleResultRow) {
  return String(item.scheduleVersion ?? '')
}
function selectRun(item: BusinessConsoleMesScheduleResultRow) {
  selectedVersion.value = item.scheduleVersion
  page.value = 1
}

function rowKey(item: BusinessConsoleScheduledOperation) {
  return `${item.workOrderId ?? 'wo'}:${item.operationTaskId ?? ''}`
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="规则排程"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${assignments.length} 条分配`"
    >
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink to="/scheduling">
            <CalendarCogIcon aria-hidden="true" />
            排产工作台
          </RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" @click="scheduleSheetOpen = true">
          <PlayIcon aria-hidden="true" />
          运行规则排程
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="scheduleCells" />

    <div class="flex items-center justify-between">
      <span class="text-sm font-semibold text-foreground">历史排程运行</span>
      <span class="text-sm text-muted-foreground">共 {{ scheduleHistoryTotal }} 次</span>
    </div>

    <NvDataTable
      :columns="historyColumns"
      :rows="scheduleHistory"
      :row-key="historyRowKey"
      :loading="scheduleHistoryPending"
      :searchable="false"
      :column-settings="false"
      empty-message="尚无历史排程运行记录。点击右上角「运行规则排程」后，本次结果会记入历史。"
      @row-click="selectRun"
    >
      <template #cell-scheduledAtUtc="{ row }">{{ formatDateTime(row.scheduledAtUtc) }}</template>
    </NvDataTable>

    <div class="flex items-center justify-between">
      <span class="text-sm font-semibold text-foreground">
        工序分配{{ activeVersion === undefined ? '' : `（v${activeVersion}）` }}
      </span>
      <span class="text-sm text-muted-foreground">{{ formatDateTime(activeScheduledAtUtc) }}</span>
    </div>

    <NvDataTable
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
      empty-message="该次排程没有工序分配。在上方历史列表中选择另一次运行，或点击右上角重新运行规则排程。"
    >
      <template #cell-startUtc="{ row }">{{ formatDateTime(row.startUtc) }}</template>
      <template #cell-endUtc="{ row }">{{ formatDateTime(row.endUtc) }}</template>
    </NvDataTable>

    <div v-if="affectedWorkOrderIds.length" class="rounded-lg border bg-background p-4">
      <h2 class="text-sm font-semibold text-foreground">受影响工单</h2>
      <div class="mt-3 flex flex-wrap gap-2">
        <NvStatusBadge
          v-for="workOrderId in affectedWorkOrderIds"
          :key="workOrderId"
          :label="workOrderId"
          tone="neutral"
        />
      </div>
    </div>

    <NvDialog v-model:open="scheduleSheetOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>运行规则排程</NvDialogTitle>
          <NvDialogDescription class="sr-only">重新计算车间的工序分配。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitScheduleRun">
          <p
            v-if="!isNonEmpty(runForm.organizationId) || !isNonEmpty(runForm.environmentId)"
            class="text-sm text-muted-foreground"
            role="status"
          >
            请先完成业务上下文选择。
          </p>

          <NvFieldGroup class="grid gap-3">
            <NvField>
              <NvFieldLabel for="schedule-trigger">触发来源</NvFieldLabel>
              <NvSelect v-model="runForm.trigger">
                <NvSelectTrigger id="schedule-trigger" aria-label="排程触发来源"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="Manual">手动</NvSelectItem>
                  <NvSelectItem value="RushOrder">急单</NvSelectItem>
                  <NvSelectItem value="AssetUnavailable">设备不可用</NvSelectItem>
                  <NvSelectItem value="AssetRestored">设备恢复</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
          </NvFieldGroup>

          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="scheduleSheetOpen = false"
              >取消</NvButton
            >
            <NvButton type="submit" :disabled="runSchedulePending || !canRunSchedule">
              <Spinner v-if="runSchedulePending" aria-hidden="true" />
              <PlayIcon v-else aria-hidden="true" />
              运行规则排程
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
