<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenancePlanRequest,
  BusinessConsoleMaintenancePlanItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenancePlans } from '@/composables/useBusinessMaintenance'
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

// 保养周期以 ISO-8601 间隔登记（后端按此推算到期），界面给常用周期。
const intervalOptions = [
  { label: '每周', value: 'P7D' },
  { label: '每两周', value: 'P14D' },
  { label: '每月', value: 'P30D' },
  { label: '每季度', value: 'P90D' },
]

const createOpen = shallowRef(false)
const createForm = reactive({
  deviceAssetId: '',
  planCode: '',
  interval: 'P30D',
  startsOn: '',
  owner: '',
})
const createError = shallowRef('')

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
  { key: 'interval', header: '保养周期', accessor: (r) => intervalLabel(r.interval) },
  { key: 'startsOn', header: '起始日期', accessor: (r) => r.startsOn ?? '—' },
]

function planNo(row: PlanRow) {
  const id = row.planId ?? ''
  return id ? `PM-${id.slice(-8).toUpperCase()}` : '保养计划'
}
function intervalLabel(value?: string | null) {
  return intervalOptions.find((o) => o.value === value)?.label ?? value ?? '—'
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
  createForm.interval = 'P30D'
  createForm.startsOn = todayDate()
  createForm.owner = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.deviceAssetId.trim() || !createForm.owner.trim()) {
    createError.value = '请填写设备与负责班组。'
    return
  }
  const body: BusinessConsoleCreateMaintenancePlanRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    deviceAssetId: createForm.deviceAssetId.trim(),
    planCode: createForm.planCode.trim() || undefined,
    interval: createForm.interval,
    startsOn: createForm.startsOn || todayDate(),
    owner: createForm.owner.trim(),
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
            <NvField>
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
