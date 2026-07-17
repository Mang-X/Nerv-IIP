<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenancePlanRequest,
  BusinessConsoleMaintenancePlanItem,
  BusinessConsoleUpdateMaintenancePlanRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import MaintenancePlanFormDialog from '@/components/maintenance/MaintenancePlanFormDialog.vue'
import { useMaintenancePlans } from '@/composables/useBusinessMaintenance'
import { useMaintenancePlanRuntimeRemaining } from '@/composables/useBusinessTelemetry'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  Spinner,
} from '@nerv-iip/ui'
import { CalendarClockIcon, PencilIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '保养计划',
    requiredPermissions: ['business.maintenance.plans.read'],
  },
})

const {
  plans,
  plansError,
  plansPending,
  plansTotal,
  refreshPlans,
  createPlan,
  createPlanPending,
  updatePlan,
  updatePlanPending,
  generateDue,
  generateDuePending,
  generateDueError,
  filters,
} = useMaintenancePlans()
const { page, pageSize } = usePagedList(filters)
// Remaining runtime hours are derived on the client (one runtime-hours read per visible runtime plan,
// each over the plan's own [startsOn, now] window) — the list query itself never fans out to telemetry.
const { remainingByPlanId, remainingPending, refreshRemaining } =
  useMaintenancePlanRuntimeRemaining(plans)
// 「刷新」需同时重取计划与逐计划剩余小时:计划字段未变时 refreshPlans 得到相同 watch key,
// 运行小时读面不会自动重算,只有显式 refreshRemaining 才能反映设备继续运行后的新剩余。
async function refreshPlansAndRemaining() {
  await refreshPlans()
  await refreshRemaining()
}

// 保养周期以 ISO-8601 间隔登记（后端按此推算到期），界面给常用周期。
const intervalOptions = [
  { label: '每周', value: 'P7D' },
  { label: '每两周', value: 'P14D' },
  { label: '每月', value: 'P30D' },
  { label: '每季度', value: 'P90D' },
]

type PlanDialogMode = 'create' | 'edit'
type PlanFormSubmission =
  | { mode: 'create'; body: BusinessConsoleCreateMaintenancePlanRequest }
  | { mode: 'edit'; planId: string; body: BusinessConsoleUpdateMaintenancePlanRequest }

const planDialogOpen = shallowRef(false)
const planDialogMode = shallowRef<PlanDialogMode>('create')
const selectedPlan = shallowRef<BusinessConsoleMaintenancePlanItem>()
const planDialogPending = computed(() =>
  planDialogMode.value === 'create' ? createPlanPending.value : updatePlanPending.value,
)

const generateOpen = shallowRef(false)
const generateForm = reactive({
  businessDate: '',
  requestedBy: '',
})
const generateError = shallowRef('')

const listErrorMessage = computed(() => formatError(plansError.value))
const generateErrorMessage = computed(
  () => generateError.value || formatError(generateDueError.value),
)

type PlanRow = BusinessConsoleMaintenancePlanItem
const columns: NvDataTableColumn<PlanRow>[] = [
  {
    key: 'planCode',
    header: '计划编号',
    cellClass: 'font-medium',
    accessor: (r) => r.planCode ?? planNo(r),
  },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '—' },
  { key: 'triggerMode', header: '触发模式', accessor: (r) => triggerModeLabel(r) },
  { key: 'interval', header: '保养周期', accessor: (r) => intervalLabel(r.interval) },
  { key: 'nextDue', header: '下次到期', accessor: (r) => nextDueLabel(r) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function planNo(row: PlanRow) {
  const id = row.planId ?? ''
  return id ? `PM-${id.slice(-8).toUpperCase()}` : '保养计划'
}
function intervalLabel(value?: string | null) {
  if (!value) return '—'
  return intervalOptions.find((o) => o.value === value)?.label ?? value
}
// 三态由存储事实区分：无日历 interval = 运行小时；无 runtimeHourInterval = 日历周期；两者皆有 = 两者组合。
function triggerModeLabel(row: PlanRow) {
  const hasCalendar = !!row.interval
  const hasRuntime = row.runtimeHourInterval != null
  if (hasCalendar && hasRuntime) return '两者组合'
  if (hasRuntime) return '运行小时'
  return '日历周期'
}
function formatHours(value?: number | null) {
  if (value === null || value === undefined) return '—'
  return `${Number(value)} 小时`
}
// 下次到期：运行小时型显剩余小时（前端按各计划起算口径经 runtime-hours facade 算出）；日历型显下次
// 到期日。遥测读取失败与真实无样本分开呈现，不把故障误报成「暂无样本」。两者组合在剩余未知时回落到
// 日历到期日。
function nextDueLabel(row: PlanRow) {
  if (row.runtimeHourInterval != null) {
    const entry = row.planId ? remainingByPlanId.value[row.planId] : undefined
    // In flight (including a refresh that superseded a prior settled value) never shows the stale value.
    if (entry?.status === 'loading' || (!entry && remainingPending.value))
      return '运行小时（读取中…）'
    if (entry?.status === 'ok') return `剩余 ${formatHours(entry.hours)}`
    // Runtime remaining unknown (read failed / no samples / inconsistent cursor — settled, not loading).
    // A combined plan still has a valid calendar due — keep showing it (with the runtime state noted),
    // rather than hiding the known date.
    const note =
      entry?.status === 'error'
        ? '运行小时读取失败'
        : entry?.status === 'invalid'
          ? '运行小时阈值缺失'
          : '暂无运行样本'
    if (row.interval && row.nextDueOn) return `${row.nextDueOn}（${note}）`
    // Runtime-only plan (no calendar fallback): surface the settled runtime state as the value.
    if (entry?.status === 'error') return '运行小时（读取失败）'
    if (entry?.status === 'invalid') return '运行小时（阈值缺失）'
    return '运行小时（暂无样本）'
  }
  return row.nextDueOn ?? row.startsOn ?? '—'
}
function rowKey(row: PlanRow) {
  return row.planId ?? row.planCode ?? '保养计划'
}
function todayDate() {
  return new Date().toISOString().slice(0, 10)
}

function openCreate() {
  selectedPlan.value = undefined
  planDialogMode.value = 'create'
  planDialogOpen.value = true
}

function openEdit(row: PlanRow) {
  if (!row.planId) return
  selectedPlan.value = row
  planDialogMode.value = 'edit'
  planDialogOpen.value = true
}

async function submitPlan(submission: PlanFormSubmission) {
  try {
    if (submission.mode === 'create') {
      await createPlan(submission.body)
      notifySuccess('保养计划已创建')
    } else {
      await updatePlan(submission.planId, submission.body)
      notifySuccess('保养计划触发条件已更新')
    }
    planDialogOpen.value = false
  } catch (error) {
    notifyError(
      error,
      submission.mode === 'create'
        ? '保养计划创建失败，请稍后重试。'
        : '保养计划更新失败，请稍后重试。',
    )
  }
}

function openGenerate() {
  generateForm.businessDate = todayDate()
  generateForm.requestedBy = ''
  generateError.value = ''
  generateOpen.value = true
}
async function submitGenerate() {
  if (!generateForm.requestedBy.trim()) {
    generateError.value = '请填写发起人。'
    return
  }
  try {
    const result = await generateDue({
      businessDate: generateForm.businessDate || todayDate(),
      requestedBy: generateForm.requestedBy.trim(),
    })
    const count = result?.data?.generatedCount ?? 0
    generateOpen.value = false
    notifySuccess(count > 0 ? `已生成 ${count} 张到期维护工单` : '当前无到期保养计划')
  } catch {
    // 失败信息由对话框错误区呈现。
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="保养计划"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${plansTotal} 个保养计划`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="plansPending"
          @click="refreshPlansAndRemaining"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" @click="openGenerate">
          <CalendarClockIcon aria-hidden="true" />
          生成到期工单
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建保养计划
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="plansTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="plans"
      :row-key="rowKey"
      :loading="plansPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无保养计划。为关键设备登记周期保养，再用「生成到期工单」批量开单。"
    >
      <template #cell-actions="{ row }">
        <NvRowActions :label="`保养计划操作 ${row.planCode ?? planNo(row)}`">
          <NvDropdownMenuItem :disabled="!row.planId" @click="openEdit(row)">
            <PencilIcon aria-hidden="true" />
            编辑触发条件
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <MaintenancePlanFormDialog
      v-model:open="planDialogOpen"
      :mode="planDialogMode"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
      :plan="selectedPlan"
      :pending="planDialogPending"
      @submit="submitPlan"
    />

    <NvDialog v-model:open="generateOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>生成到期工单</NvDialogTitle>
          <NvDialogDescription
            >按业务日期扫描全部保养计划，对到期者批量开具维护工单。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitGenerate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="gen-date">业务日期</NvFieldLabel>
              <NvInput id="gen-date" v-model="generateForm.businessDate" type="date" />
            </NvField>
            <NvField>
              <NvFieldLabel for="gen-by">发起人</NvFieldLabel>
              <NvInput
                id="gen-by"
                v-model="generateForm.requestedBy"
                autocomplete="off"
                placeholder="如 设备调度"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="generateErrorMessage" :errors="[generateErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="generateDuePending">
              <Spinner v-if="generateDuePending" aria-hidden="true" />
              <CalendarClockIcon v-else aria-hidden="true" />
              生成到期工单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
