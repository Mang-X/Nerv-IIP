<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenancePlanRequest,
  BusinessConsoleMaintenancePlanItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenancePlans } from '@/composables/useBusinessMaintenance'
import { useMaintenancePlanRuntimeRemaining } from '@/composables/useBusinessTelemetry'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
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
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvTabs,
  NvTabsList,
  NvTabsTrigger,
  Spinner,
  toast,
} from '@nerv-iip/ui'
import { CalendarClockIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
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
  createPlanError,
  generateDue,
  generateDuePending,
  generateDueError,
  filters,
} = useMaintenancePlans()
const { page, pageSize } = usePagedList(filters)
// Remaining runtime hours are derived on the client (one runtime-hours read per visible runtime plan,
// each over the plan's own [startsOn, now] window) — the list query itself never fans out to telemetry.
const { remainingByPlanId } = useMaintenancePlanRuntimeRemaining(plans)

// 保养周期以 ISO-8601 间隔登记（后端按此推算到期），界面给常用周期。
const intervalOptions = [
  { label: '每周', value: 'P7D' },
  { label: '每两周', value: 'P14D' },
  { label: '每月', value: 'P30D' },
  { label: '每季度', value: 'P90D' },
]

// 触发模式：日历周期（按日期到期）/ 运行小时（按累计运行小时到期，无日历到期）/ 两者组合（任一先到即开单）。
type TriggerMode = 'calendar' | 'runtime' | 'both'
const runtimeHourQuickValues = [500, 1000, 2000]

const createOpen = shallowRef(false)
const createForm = reactive({
  deviceAssetId: '',
  planCode: '',
  triggerMode: 'calendar' as TriggerMode,
  interval: 'P30D',
  runtimeHourInterval: '',
  startsOn: '',
  owner: '',
})
const createError = shallowRef('')
const runtimeHoursInvalid = shallowRef(false)

const generateOpen = shallowRef(false)
const generateForm = reactive({
  businessDate: '',
  requestedBy: '',
})
const generateError = shallowRef('')

const listErrorMessage = computed(() => formatError(plansError.value))
const createErrorMessage = computed(() => createError.value || formatError(createPlanError.value))
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
    if (entry?.status === 'ok') return `剩余 ${formatHours(entry.hours)}`
    if (entry?.status === 'error') return '运行小时（读取失败）'
    // no-samples or not-yet-computed: prefer the calendar due for combined plans, else say no samples.
    if (row.interval && row.nextDueOn) return row.nextDueOn
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
  createForm.deviceAssetId = ''
  createForm.planCode = ''
  createForm.triggerMode = 'calendar'
  createForm.interval = 'P30D'
  createForm.runtimeHourInterval = ''
  createForm.startsOn = todayDate()
  createForm.owner = ''
  createError.value = ''
  runtimeHoursInvalid.value = false
  createOpen.value = true
}
function setRuntimeHours(value: number) {
  createForm.runtimeHourInterval = String(value)
  runtimeHoursInvalid.value = false
}
async function submitCreate() {
  createError.value = ''
  runtimeHoursInvalid.value = false
  if (!createForm.deviceAssetId.trim() || !createForm.owner.trim()) {
    createError.value = '请填写设备与负责班组。'
    return
  }

  const usesRuntime = createForm.triggerMode !== 'calendar'
  let runtimeHourInterval: number | undefined
  if (usesRuntime) {
    const parsed = Number(createForm.runtimeHourInterval)
    if (!createForm.runtimeHourInterval.trim() || !Number.isFinite(parsed) || parsed <= 0) {
      runtimeHoursInvalid.value = true
      createError.value = '请填写正的触发运行小时数。'
      return
    }
    runtimeHourInterval = parsed
  }

  // 运行小时模式不带日历到期（interval 留空 → 真正的纯运行小时触发）；日历/两者组合用所选周期。
  const interval = createForm.triggerMode === 'runtime' ? undefined : createForm.interval

  const body: BusinessConsoleCreateMaintenancePlanRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    deviceAssetId: createForm.deviceAssetId.trim(),
    planCode: createForm.planCode.trim() || undefined,
    interval,
    startsOn: createForm.startsOn || todayDate(),
    owner: createForm.owner.trim(),
    runtimeHourInterval,
  }
  try {
    await createPlan(body)
    createOpen.value = false
    toast.success('保养计划已创建')
  } catch {
    // 失败信息由对话框错误区呈现。
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
    toast.success(count > 0 ? `已生成 ${count} 张到期维护工单` : '当前无到期保养计划')
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
          @click="refreshPlans"
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
    />

    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建保养计划</NvDialogTitle>
          <NvDialogDescription
            >为设备登记周期保养，系统据此推算到期并批量生成维护工单。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvField>
            <NvFieldLabel>触发模式</NvFieldLabel>
            <NvTabs v-model="createForm.triggerMode">
              <NvTabsList class="w-full">
                <NvTabsTrigger value="calendar" class="flex-1">日历周期</NvTabsTrigger>
                <NvTabsTrigger value="runtime" class="flex-1">运行小时</NvTabsTrigger>
                <NvTabsTrigger value="both" class="flex-1">两者组合</NvTabsTrigger>
              </NvTabsList>
            </NvTabs>
            <p class="text-xs text-muted-foreground">
              <template v-if="createForm.triggerMode === 'calendar'"
                >按保养周期到期开单，例如每月一次。</template
              >
              <template v-else-if="createForm.triggerMode === 'runtime'"
                >按设备累计运行小时到期开单，不受日历影响；例如每运行满 1000
                小时保养一次。</template
              >
              <template v-else>同时保留日历周期与运行小时两条到期线，任一先到即开单。</template>
            </p>
          </NvField>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="plan-device">设备</NvFieldLabel>
              <NvInput
                id="plan-device"
                v-model="createForm.deviceAssetId"
                autocomplete="off"
                placeholder="如 DEV-SMT-01"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="plan-code">计划编号</NvFieldLabel>
              <NvInput
                id="plan-code"
                v-model="createForm.planCode"
                autocomplete="off"
                placeholder="可选，如 PM-SMT-01-M"
              />
            </NvField>
            <NvField v-if="createForm.triggerMode !== 'runtime'">
              <NvFieldLabel for="plan-interval">保养周期</NvFieldLabel>
              <NvSelect v-model="createForm.interval">
                <NvSelectTrigger id="plan-interval" aria-label="保养周期"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in intervalOptions" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField v-if="createForm.triggerMode !== 'calendar'">
              <NvFieldLabel for="plan-runtime-hours">触发运行小时</NvFieldLabel>
              <NvInput
                id="plan-runtime-hours"
                v-model="createForm.runtimeHourInterval"
                type="number"
                inputmode="numeric"
                min="1"
                step="1"
                autocomplete="off"
                placeholder="如 1000"
                :invalid="runtimeHoursInvalid"
                aria-describedby="plan-runtime-hours-error"
                @input="runtimeHoursInvalid = false"
              />
              <NvFieldError
                v-if="runtimeHoursInvalid"
                id="plan-runtime-hours-error"
                :errors="['请填写正的触发运行小时数。']"
              />
              <div class="flex flex-wrap gap-2 pt-1">
                <NvButton
                  v-for="value in runtimeHourQuickValues"
                  :key="value"
                  size="sm"
                  type="button"
                  variant="outline"
                  @click="setRuntimeHours(value)"
                  >{{ value }} 小时</NvButton
                >
              </div>
            </NvField>
            <NvField>
              <NvFieldLabel for="plan-starts">起始日期</NvFieldLabel>
              <NvInput id="plan-starts" v-model="createForm.startsOn" type="date" />
            </NvField>
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="plan-owner">负责班组</NvFieldLabel>
              <NvInput
                id="plan-owner"
                v-model="createForm.owner"
                autocomplete="off"
                placeholder="如 设备保全班"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createPlanPending">
              <Spinner v-if="createPlanPending" aria-hidden="true" />
              创建保养计划
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

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
