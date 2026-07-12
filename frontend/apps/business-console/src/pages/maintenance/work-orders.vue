<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenanceWorkOrderRequest,
  BusinessConsoleMaintenanceSparePartInput,
  BusinessConsoleMaintenanceWorkOrderItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenanceWorkOrders } from '@/composables/useBusinessMaintenance'
import {
  useBusinessWorkers,
  useBusinessMasterDataResources,
} from '@/composables/useBusinessMasterData'
import { usePagedList } from '@/composables/usePagedList'
import { useAuthStore } from '@/stores/auth'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSearchSelect,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
  NvStatusBadge,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { storeToRefs } from 'pinia'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '维护工单',
    requiredPermissions: ['business.maintenance.work-orders.read'],
  },
})

const {
  workOrders,
  workOrdersError,
  workOrdersPending,
  workOrdersTotal,
  refreshWorkOrders,
  createWorkOrder,
  createWorkOrderPending,
  createWorkOrderError,
  completeWorkOrder,
  completeWorkOrderPending,
  completeWorkOrderError,
  filters,
} = useMaintenanceWorkOrders()
const { page, pageSize } = usePagedList(filters)
const route = useRoute()

// 技师目录（人员选择器数据源，读自 /master-data/workers）。
const { workers, workersPending } = useBusinessWorkers()
// 设备台账（设备编号联想建议，读自 master-data device-asset 资源）。
const { resources: deviceResources } = useBusinessMasterDataResources('device-asset')
// 当前登录用户（开单人默认当前用户，可改选他人，不自由输入）。
const auth = useAuthStore()
const { principal } = storeToRefs(auth)
const currentUserId = computed(() => principal.value?.principalId ?? '')
const workerOptions = computed(() =>
  workers.value
    .map((w) => ({
      value: w.userId ?? '',
      label: w.displayName ?? w.userId ?? '',
      hint: w.employeeNo ?? undefined,
    }))
    .filter((o) => o.value.length > 0),
)
function personLabel(userId: string) {
  return (
    workers.value.find((w) => w.userId === userId)?.displayName ??
    principal.value?.loginName ??
    userId
  )
}

const priorityOptions = [
  { label: '高', value: 'high' },
  { label: '中', value: 'medium' },
  { label: '低', value: 'low' },
]
const resultOptions = [
  { label: '已修复', value: 'repaired' },
  { label: '已更换部件', value: 'replaced' },
  { label: '已校准', value: 'calibrated' },
]
const reasonOptions = [
  { label: '预防性保养', value: 'preventive' },
  { label: '部件磨损', value: 'worn-part' },
  { label: '突发故障', value: 'breakdown' },
]
const UNASSIGNED = '__unassigned__'

// 设备编号联想建议：设备台账的编号 + 名称（可自由录入未登记设备）。
const deviceSuggestions = computed(() =>
  deviceResources.value
    .map((r) => ({ value: (r.code ?? '').trim(), label: r.displayName ?? r.code ?? '' }))
    .filter((s) => s.value.length > 0),
)
// 指派技师选项：未指派 + 工人目录（姓名 + 工号）。
// 注：技师只能在**建单**时写 assignedTechnicianUserId——CompleteMaintenanceWorkOrderRequest
// 契约无该字段、Maintenance 域也无 assign/reassign 端点，故"完工时记录/改选实际技师"是待补的
// 后端缺口（见 #793 审核，已建后端 follow-up）。可靠性汇总按建单指派技师聚合。
const technicianOptions = computed(() => [
  { value: UNASSIGNED, label: '未指派' },
  ...workers.value
    .map((w) => ({
      value: w.userId ?? '',
      label: w.displayName ?? w.userId ?? '',
      hint: w.employeeNo ?? undefined,
    }))
    .filter((o) => o.value.length > 0),
])

// 待执行 / 已完成基于本页可见行的语义统计（可行动数，非机械总数）。
const OPEN_STATUSES = new Set(['open', 'opened', 'scheduled', 'inprogress', 'in-progress'])
const pendingCount = computed(
  () => workOrders.value.filter((w) => OPEN_STATUSES.has((w.status ?? '').toLowerCase())).length,
)
const highPriorityPending = computed(
  () =>
    workOrders.value.filter(
      (w) =>
        OPEN_STATUSES.has((w.status ?? '').toLowerCase()) &&
        (w.priority ?? '').toLowerCase() === 'high',
    ).length,
)

const createOpen = shallowRef(false)
const createForm = reactive({
  deviceAssetId: '',
  priority: 'medium',
  openedByUserId: '',
  sourceAlarmId: '',
  assignedTechnicianUserId: UNASSIGNED,
  estimatedLaborMinutes: '',
})
const createError = shallowRef('')

interface SparePartRow {
  id: number
  skuCode: string
  quantity: string
  uomCode: string
  unitCost: string
}
let nextSpareRowId = 1
function createSpareRow(): SparePartRow {
  return { id: nextSpareRowId++, skuCode: '', quantity: '1', uomCode: 'EA', unitCost: '' }
}

const completeOpen = shallowRef(false)
const completeTarget = shallowRef<BusinessConsoleMaintenanceWorkOrderItem>()
const completeForm = reactive({
  result: 'repaired',
  downtimeReasonCode: 'preventive',
  downtimeMinutes: '30',
  actualLaborMinutes: '',
  externalServiceCostAmount: '',
  costCurrencyCode: 'CNY',
})
const spareRows = reactive<SparePartRow[]>([createSpareRow()])
// 备件成本覆盖：空串 = 未覆盖（用自动合计）；非空 = 人工改写值。
const sparePartCostOverride = shallowRef('')
const completeError = shallowRef('')

// 自动合计：Σ(数量 × 单价)，仅计入数值有效的行。
const autoSparePartCost = computed(() =>
  spareRows.reduce((sum, row) => {
    // number 输入框经 v-model 可能回传 number，String() 归一后再判空/解析。
    const unitRaw = String(row.unitCost ?? '').trim()
    const qty = Number(row.quantity)
    const unit = Number(unitRaw)
    if (!unitRaw || !Number.isFinite(qty) || !Number.isFinite(unit)) return sum
    return sum + qty * unit
  }, 0),
)
// 展示/编辑用：未覆盖时回显自动合计，人工输入即视为覆盖。
const sparePartCostDisplay = computed({
  get: () =>
    sparePartCostOverride.value !== ''
      ? sparePartCostOverride.value
      : autoSparePartCost.value > 0
        ? String(round2(autoSparePartCost.value))
        : '',
  set: (value: string) => {
    sparePartCostOverride.value = value
  },
})
const listErrorMessage = computed(() => formatError(workOrdersError.value))
const createErrorMessage = computed(
  () => createError.value || formatError(createWorkOrderError.value),
)
const completeErrorMessage = computed(
  () => completeError.value || formatError(completeWorkOrderError.value),
)
const queryPrefilled = shallowRef(false)

type WorkOrderRow = BusinessConsoleMaintenanceWorkOrderItem
const columns: NvDataTableColumn<WorkOrderRow>[] = [
  {
    key: 'workOrderNo',
    header: '工单号',
    cellClass: 'font-medium',
    accessor: (r) => workOrderNo(r),
  },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '—' },
  { key: 'priority', header: '优先级', width: 'w-20' },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'assignedTechnicianUserId',
    header: '技师',
    accessor: (r) => technicianLabel(r.assignedTechnicianUserId),
  },
  { key: 'openedAtUtc', header: '开单时间', accessor: (r) => formatDateTime(r.openedAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function workOrderNo(row: WorkOrderRow) {
  const id = row.workOrderId ?? ''
  // 人读单号：取 GUID 末段大写，GUID 自身仅作内部点击目标。
  return id ? `WO-${id.slice(-8).toUpperCase()}` : '维护工单'
}
function priorityLabel(value?: string | null) {
  return priorityOptions.find((o) => o.value === (value ?? '').toLowerCase())?.label ?? value ?? '—'
}
function technicianLabel(userId?: string | null) {
  if (!userId) return '未指派'
  const worker = workers.value.find((w) => w.userId === userId)
  return worker?.displayName ?? userId
}
function rowKey(row: WorkOrderRow) {
  return row.workOrderId ?? '维护工单'
}
function round2(value: number) {
  return Math.round(value * 100) / 100
}

function openCreate(prefill: Partial<typeof createForm> = {}) {
  createForm.deviceAssetId = prefill.deviceAssetId ?? ''
  createForm.priority = 'medium'
  createForm.openedByUserId = currentUserId.value
  createForm.sourceAlarmId = prefill.sourceAlarmId ?? ''
  createForm.assignedTechnicianUserId = UNASSIGNED
  createForm.estimatedLaborMinutes = ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.deviceAssetId.trim() || !createForm.openedByUserId) {
    createError.value = '请填写设备并选择开单人。'
    return
  }
  const estimatedLaborMinutes = optionalNonNegativeInt(createForm.estimatedLaborMinutes)
  if (estimatedLaborMinutes === false) {
    createError.value = '预估工时需为非负整数。'
    return
  }
  const body: BusinessConsoleCreateMaintenanceWorkOrderRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    deviceAssetId: createForm.deviceAssetId.trim(),
    priority: createForm.priority,
    openedBy: personLabel(createForm.openedByUserId),
    sourceAlarmId: createForm.sourceAlarmId.trim() || undefined,
    assignedTechnicianUserId:
      createForm.assignedTechnicianUserId === UNASSIGNED
        ? undefined
        : createForm.assignedTechnicianUserId,
    ...(estimatedLaborMinutes !== undefined ? { estimatedLaborMinutes } : {}),
  }
  try {
    await createWorkOrder(body)
    createOpen.value = false
    toast.success('维护工单已创建')
  } catch {
    // 失败信息由抽屉错误区呈现。
  }
}

function openComplete(row: WorkOrderRow) {
  completeTarget.value = row
  completeForm.result = 'repaired'
  completeForm.downtimeReasonCode = 'preventive'
  completeForm.downtimeMinutes = '30'
  completeForm.actualLaborMinutes = ''
  completeForm.externalServiceCostAmount = ''
  completeForm.costCurrencyCode = 'CNY'
  spareRows.splice(0, spareRows.length, createSpareRow())
  sparePartCostOverride.value = ''
  completeError.value = ''
  completeOpen.value = true
}
function addSpareRow() {
  spareRows.push(createSpareRow())
}
function removeSpareRow(rowId: number) {
  if (spareRows.length === 1) {
    Object.assign(spareRows[0], createSpareRow())
    return
  }
  const index = spareRows.findIndex((row) => row.id === rowId)
  if (index >= 0) spareRows.splice(index, 1)
}
function spareOutOfBounds(row: SparePartRow) {
  return Boolean(row.skuCode.trim()) && !(Number(row.quantity) > 0)
}

async function submitComplete() {
  const target = completeTarget.value
  if (!target?.workOrderId) return
  const minutes = Number(completeForm.downtimeMinutes)
  if (!(minutes >= 0)) {
    completeError.value = '停机时长需为非负数。'
    return
  }
  const actualLaborMinutes = optionalNonNegativeInt(completeForm.actualLaborMinutes)
  if (actualLaborMinutes === false) {
    completeError.value = '实际工时需为非负整数。'
    return
  }
  const externalServiceCostAmount = optionalNonNegativeNumber(
    completeForm.externalServiceCostAmount,
  )
  if (externalServiceCostAmount === false) {
    completeError.value = '外委费用需为非负数。'
    return
  }
  // 备件成本人工覆盖须为合法非负数——否则负值会发出负成本、非法值会静默丢字段。
  const overrideCost = optionalNonNegativeNumber(sparePartCostOverride.value)
  if (overrideCost === false) {
    completeError.value = '备件成本汇总需为非负数。'
    return
  }
  const sparePartCostAmount =
    overrideCost !== undefined
      ? overrideCost
      : autoSparePartCost.value > 0
        ? round2(autoSparePartCost.value)
        : undefined
  // 完成需登记至少一条更换备件（领料扣减）；后端以此核销维护成本。
  const filledSpares = spareRows.filter((row) => row.skuCode.trim())
  if (filledSpares.length === 0) {
    completeError.value = '请登记至少一条更换备件（物料 + 数量）。'
    return
  }
  if (filledSpares.some(spareOutOfBounds)) {
    completeError.value = '备件数量需为正数。'
    return
  }
  const spareParts: BusinessConsoleMaintenanceSparePartInput[] = filledSpares.map((row) => ({
    skuCode: row.skuCode.trim(),
    quantity: Number(row.quantity),
    uomCode: row.uomCode.trim() || 'EA',
  }))
  try {
    await completeWorkOrder(target.workOrderId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      result: completeForm.result,
      downtimeReasonCode: completeForm.downtimeReasonCode,
      downtimeMinutes: minutes,
      spareParts,
      ...(actualLaborMinutes !== undefined ? { actualLaborMinutes } : {}),
      ...(sparePartCostAmount !== undefined ? { sparePartCostAmount } : {}),
      ...(externalServiceCostAmount !== undefined ? { externalServiceCostAmount } : {}),
      costCurrencyCode: completeForm.costCurrencyCode.trim() || undefined,
    })
    completeOpen.value = false
    toast.success(`维护工单 ${workOrderNo(target)} 已完成`)
  } catch {
    // 失败信息由抽屉错误区呈现。
  }
}

// 非负整数：空 → undefined（不带该字段）；非法 → false；合法 → number。
// number 输入框经 v-model 可能回传 number（非 string），故统一 String() 归一再判。
function optionalNonNegativeInt(value: string | number): number | undefined | false {
  const trimmed = String(value ?? '').trim()
  if (!trimmed) return undefined
  const n = Number(trimmed)
  return Number.isInteger(n) && n >= 0 ? n : false
}
function optionalNonNegativeNumber(value: string | number): number | undefined | false {
  const trimmed = String(value ?? '').trim()
  if (!trimmed) return undefined
  const n = Number(trimmed)
  return Number.isFinite(n) && n >= 0 ? round2(n) : false
}

function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

watch(
  () => route.query,
  (query) => {
    if (queryPrefilled.value) return
    const deviceAssetId = typeof query.deviceAssetId === 'string' ? query.deviceAssetId : ''
    const sourceAlarmId = typeof query.sourceAlarmId === 'string' ? query.sourceAlarmId : ''
    if (!deviceAssetId && !sourceAlarmId) return
    queryPrefilled.value = true
    openCreate({ deviceAssetId, sourceAlarmId })
  },
  { immediate: true },
)
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="维护工单"
      :breadcrumbs="[{ label: '设备监控' }]"
      :count="`${workOrdersTotal} 张维护工单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="workOrdersPending"
          @click="refreshWorkOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建维护工单
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="待执行工单" :value="pendingCount" hint="本页未完成，待派工执行" />
      <NvSectionCard
        description="待执行 · 高优先"
        :value="highPriorityPending"
        hint="本页高优先级，需优先排程"
      />
    </NvSectionCards>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="workOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="workOrders"
      :row-key="rowKey"
      :loading="workOrdersPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无维护工单。设备报警或巡检发现异常时在此开单。"
    >
      <template #cell-priority="{ row }"
        ><NvStatusBadge :value="priorityLabel(row.priority)"
      /></template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`维护工单操作 ${workOrderNo(row)}`">
          <NvDropdownMenuItem @click="openComplete(row)">
            <CheckCircle2Icon aria-hidden="true" />
            完成工单
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <!-- 新建维护工单：设备/优先级/开单人/报警/技师/预估工时（6 字段）→ 侧滑 Sheet（A1 §1）。 -->
    <NvSheet v-model:open="createOpen">
      <NvSheetContent class="flex w-full flex-col overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>新建维护工单</NvSheetTitle>
          <NvSheetDescription
            >对设备开具维护工单，可关联触发的设备报警，并指派技师与预估工时。</NvSheetDescription
          >
        </NvSheetHeader>
        <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="mwo-device">设备</NvFieldLabel>
              <NvCombobox
                id="mwo-device"
                v-model="createForm.deviceAssetId"
                :suggestions="deviceSuggestions"
                placeholder="搜索设备台账或直接输入，如 DEV-SMT-01"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-priority">优先级</NvFieldLabel>
              <NvSelect v-model="createForm.priority">
                <NvSelectTrigger id="mwo-priority" aria-label="优先级"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in priorityOptions" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-opened-by">开单人</NvFieldLabel>
              <NvSearchSelect
                id="mwo-opened-by"
                v-model="createForm.openedByUserId"
                :options="workerOptions"
                :loading="workersPending"
                aria-label="开单人"
                placeholder="选择开单人"
                search-placeholder="搜索姓名 / 工号…"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-alarm">关联报警</NvFieldLabel>
              <NvInput
                id="mwo-alarm"
                v-model="createForm.sourceAlarmId"
                autocomplete="off"
                placeholder="可选"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-technician">指派技师</NvFieldLabel>
              <NvSearchSelect
                id="mwo-technician"
                v-model="createForm.assignedTechnicianUserId"
                :options="technicianOptions"
                :loading="workersPending"
                aria-label="指派技师"
                placeholder="未指派"
                search-placeholder="搜索技师姓名 / 工号…"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-est-labor">预估工时（分钟）</NvFieldLabel>
              <NvInput
                id="mwo-est-labor"
                v-model="createForm.estimatedLaborMinutes"
                type="number"
                min="0"
                step="1"
                placeholder="可选"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <NvSheetFooter class="px-0">
            <NvButton type="button" variant="outline" @click="createOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="createWorkOrderPending">
              <Spinner v-if="createWorkOrderPending" aria-hidden="true" />
              创建维护工单
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>

    <!-- 完成维护工单：结果/停机/工时 + 备件动态行 + 成本汇总 → 侧滑 Sheet（A1 §1）。 -->
    <NvSheet v-model:open="completeOpen">
      <NvSheetContent class="flex w-full flex-col overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>完成维护工单</NvSheetTitle>
          <NvSheetDescription>
            {{
              completeTarget
                ? `${workOrderNo(completeTarget)} · ${completeTarget.deviceAssetId ?? ''}`
                : '登记维护结果、工时与成本。'
            }}
          </NvSheetDescription>
        </NvSheetHeader>
        <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitComplete">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="mwo-result">维护结果</NvFieldLabel>
              <NvSearchSelect
                id="mwo-result"
                v-model="completeForm.result"
                :options="resultOptions"
                aria-label="维护结果"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-reason">停机原因</NvFieldLabel>
              <NvSearchSelect
                id="mwo-reason"
                v-model="completeForm.downtimeReasonCode"
                :options="reasonOptions"
                aria-label="停机原因"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-minutes">停机时长（分钟）</NvFieldLabel>
              <NvInput
                id="mwo-minutes"
                v-model="completeForm.downtimeMinutes"
                type="number"
                min="0"
                step="1"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-actual-labor">实际工时（分钟）</NvFieldLabel>
              <NvInput
                id="mwo-actual-labor"
                v-model="completeForm.actualLaborMinutes"
                type="number"
                min="0"
                step="1"
                placeholder="可选"
              />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">更换备件</span>
              <NvButton type="button" variant="outline" size="sm" @click="addSpareRow">
                <PlusIcon aria-hidden="true" />
                添加一行
              </NvButton>
            </div>
            <div
              v-for="row in spareRows"
              :key="row.id"
              :data-testid="`spare-row-${row.id}`"
              class="grid items-end gap-2 rounded-md border p-3 sm:grid-cols-[1fr_5rem_4rem_6rem_auto]"
            >
              <NvField>
                <NvFieldLabel :for="`spare-sku-${row.id}`">物料</NvFieldLabel>
                <NvInput
                  :id="`spare-sku-${row.id}`"
                  v-model="row.skuCode"
                  autocomplete="off"
                  placeholder="如 主控芯片MCU"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`spare-qty-${row.id}`">数量</NvFieldLabel>
                <NvInput
                  :id="`spare-qty-${row.id}`"
                  v-model="row.quantity"
                  type="number"
                  min="1"
                  step="1"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`spare-uom-${row.id}`">单位</NvFieldLabel>
                <NvInput :id="`spare-uom-${row.id}`" v-model="row.uomCode" autocomplete="off" />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`spare-cost-${row.id}`">单价</NvFieldLabel>
                <NvInput
                  :id="`spare-cost-${row.id}`"
                  v-model="row.unitCost"
                  type="number"
                  min="0"
                  step="any"
                  placeholder="可选"
                />
              </NvField>
              <NvButton
                type="button"
                variant="ghost"
                size="icon"
                aria-label="移除该备件"
                @click="removeSpareRow(row.id)"
              >
                <Trash2Icon aria-hidden="true" />
              </NvButton>
            </div>
          </div>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="mwo-spare-cost">备件成本汇总</NvFieldLabel>
              <NvInput
                id="mwo-spare-cost"
                v-model="sparePartCostDisplay"
                type="number"
                min="0"
                step="any"
                :placeholder="`自动合计 ${round2(autoSparePartCost)}`"
              />
              <p class="text-xs text-muted-foreground">
                自动从备件行（数量 × 单价）合计
                <span class="tabular-nums">{{ round2(autoSparePartCost) }}</span
                >，可直接改写。
              </p>
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-external-cost">外委费用</NvFieldLabel>
              <NvInput
                id="mwo-external-cost"
                v-model="completeForm.externalServiceCostAmount"
                type="number"
                min="0"
                step="any"
                placeholder="可选"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-currency">币种</NvFieldLabel>
              <NvInput
                id="mwo-currency"
                v-model="completeForm.costCurrencyCode"
                autocomplete="off"
              />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="completeErrorMessage" :errors="[completeErrorMessage]" />

          <NvSheetFooter class="px-0">
            <NvButton type="button" variant="outline" @click="completeOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="completeWorkOrderPending">
              <Spinner v-if="completeWorkOrderPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              完成工单
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
