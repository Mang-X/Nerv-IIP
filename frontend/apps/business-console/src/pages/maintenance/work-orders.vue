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
import {
  useEquipmentSkuCatalog,
  useEquipmentUomCatalog,
} from '@/composables/useEquipmentPickerCatalog'
import { usePagedList } from '@/composables/usePagedList'
import { useAuthStore } from '@/stores/auth'
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvDropdownMenuItem,
  NvEntityPicker,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricCard,
  NvMetricStrip,
  NvPageHeader,
  NvRowActions,
  NvSearchSelect,
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
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { storeToRefs } from 'pinia'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

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
  completeWorkOrder,
  completeWorkOrderPending,
  filters,
} = useMaintenanceWorkOrders()
const { page, pageSize } = usePagedList(filters)
const route = useRoute()
const router = useRouter()

// 技师目录（人员选择器数据源，读自 /master-data/workers）。
const { workers, workersPending } = useBusinessWorkers()
// 设备台账（设备编号联想建议，读自 master-data device-asset 资源）。
const { resources: deviceResources } = useBusinessMasterDataResources('device-asset')
// 完工登记的换件行：物料与单位从主数据选，单位默认跟随物料基本单位。
const { skuOptions, skusPending, baseUomBySku } = useEquipmentSkuCatalog()
const { uomOptions, uomsPending } = useEquipmentUomCatalog()
function applySpareRowSku(row: { skuCode: string; uomCode: string }) {
  const baseUom = baseUomBySku.value.get(row.skuCode.trim())
  if (baseUom) row.uomCode = baseUom
}
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
// 设备编号联想建议：设备台账的编号 + 名称（可自由录入未登记设备）。
const deviceSuggestions = computed(() =>
  deviceResources.value
    .map((r) => ({ value: (r.code ?? '').trim(), label: r.displayName ?? r.code ?? '' }))
    .filter((s) => s.value.length > 0),
)
// 指派技师 / 实际技师均走 master-data 复用件 WorkerSelect（服务端检索工人目录，绑 userId）。
// 建单写 assignedTechnicianUserId、完工写 actualTechnicianUserId（#897 已补契约）。
// 可靠性汇总按技师聚合。

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
  assignedTechnicianUserId: '',
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
  // 单位留空：选完物料自动带出它的基本单位，提交时仍保留 EA 兜底（见 buildSparePartInputs）。
  return { id: nextSpareRowId++, skuCode: '', quantity: '1', uomCode: '', unitCost: '' }
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
  // 实际执行技师（#897 完工契约 actualTechnicianUserId，userId）。默认取建单指派技师。
  actualTechnicianUserId: '',
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
// 服务端错误走 toast；这里只留点提交后的字段级校验汇总。
const createErrorMessage = computed(() => createError.value)
const completeErrorMessage = computed(() => completeError.value)
const queryPrefilled = shallowRef(false)
// 从报警行「创建维修工单」带入时：设备与来源报警是既定事实，只读呈现，不再给输入位。
const createCarried = shallowRef(false)
const createCarriedItems = computed(() => [
  { label: '设备', value: createForm.deviceAssetId },
  { label: '来源报警', value: createForm.sourceAlarmId },
])
const completeCarriedItems = computed(() => {
  const target = completeTarget.value
  if (!target) return []
  // 「—」是列表占位符，不是事实——只读区里直接不渲染该条。
  const omitPlaceholder = (value: string) => (value === '—' ? undefined : value)
  return [
    { label: '工单号', value: workOrderNo(target) },
    { label: '设备', value: target.deviceAssetId },
    { label: '优先级', value: omitPlaceholder(priorityLabel(target.priority)) },
    { label: '保修', value: omitPlaceholder(warrantyStatusLabel(target.warrantyStatus)) },
    { label: '供应商', value: target.supplierPartnerCode },
    { label: '开单时间', value: omitPlaceholder(formatDateTime(target.openedAtUtc)) },
  ]
})

type WorkOrderRow = BusinessConsoleMaintenanceWorkOrderItem
const columns: NvDataTableColumn<WorkOrderRow>[] = [
  {
    key: 'workOrderNo',
    header: '工单号',
    cellClass: 'font-medium',
    accessor: (r) => workOrderNo(r),
  },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '—' },
  { key: 'warrantyStatus', header: '保修', width: 'w-24' },
  {
    key: 'warrantyExpiresOn',
    header: '保修到期',
    width: 'w-28',
    accessor: (r) => formatDate(r.warrantyExpiresOn),
  },
  {
    key: 'supplierPartnerCode',
    header: '供应商',
    width: 'w-28',
    accessor: (r) => r.supplierPartnerCode ?? '—',
  },
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
// 建单只开放高/中/低三档，但报警自动开单等来源会带 critical/urgent 等更高档位，
// 只查建单选项会把它们原样漏成英文码，所以显示走一张覆盖全部来源的映射表。
const PRIORITY_LABELS: Record<string, string> = {
  critical: '紧急',
  urgent: '紧急',
  high: '高',
  medium: '中',
  normal: '中',
  low: '低',
}
function priorityLabel(value?: string | null) {
  const code = (value ?? '').trim().toLowerCase()
  if (!code) return '—'
  return PRIORITY_LABELS[code] ?? '—'
}
function technicianLabel(userId?: string | null) {
  if (!userId) return '未指派'
  const worker = workers.value.find((w) => w.userId === userId)
  return worker?.displayName ?? userId
}
function warrantyStatusLabel(value?: string | null) {
  switch ((value ?? '').toLowerCase()) {
    case 'in-warranty':
      return '在保'
    case 'out-of-warranty':
      return '出保'
    default:
      // 设备没登记保修信息不是一种"状态"，用占位符弱化，别喊「未知」制造疑问。
      return '—'
  }
}
function rowKey(row: WorkOrderRow) {
  return row.workOrderId ?? '维护工单'
}
function round2(value: number) {
  return Math.round(value * 100) / 100
}

function openCreate(prefill: Partial<typeof createForm> = {}) {
  createCarried.value = Boolean(prefill.deviceAssetId || prefill.sourceAlarmId)
  createForm.deviceAssetId = prefill.deviceAssetId ?? ''
  createForm.priority = 'medium'
  createForm.openedByUserId = currentUserId.value
  createForm.sourceAlarmId = prefill.sourceAlarmId ?? ''
  createForm.assignedTechnicianUserId = ''
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
    assignedTechnicianUserId: createForm.assignedTechnicianUserId || undefined,
    ...(estimatedLaborMinutes !== undefined ? { estimatedLaborMinutes } : {}),
  }
  try {
    await createWorkOrder(body)
    createOpen.value = false
    notifySuccess('维护工单已创建')
  } catch (error) {
    notifyError(error, '维护工单创建失败，请稍后重试。')
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
  // 实际技师默认沿用建单指派技师，完工时可改选真正执行人。
  completeForm.actualTechnicianUserId = row.assignedTechnicianUserId ?? ''
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
      // #897：完工登记实际执行技师（userId）；空则不带该字段。
      actualTechnicianUserId: completeForm.actualTechnicianUserId.trim() || undefined,
    })
    completeOpen.value = false
    notifySuccess(`维护工单 ${workOrderNo(target)} 已完成`)
  } catch (error) {
    notifyError(error, '维护工单完成失败，请稍后重试。')
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
function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString()
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

    <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,26rem)]">
      <!-- 高优先待执行只出现在右侧告警卡（它带「看可用窗口」的行动出口）；
           这里再列一格同名同值的读数纯属重复，故只留总量与待执行两格。 -->
      <NvMetricStrip
        :cells="[
          {
            key: 'total',
            label: '维护工单',
            value: workOrdersTotal,
            unit: '张',
            meta: '当前业务范围内全部维护工单',
          },
          {
            key: 'pending',
            label: '待派工执行',
            value: pendingCount,
            unit: '张',
            meta: '尚未完工，需要排人排窗口',
          },
        ]"
      />
      <NvMetricCard
        variant="alert"
        label="高优先待执行"
        :value="highPriorityPending"
        unit="张"
        :tone="highPriorityPending > 0 ? 'warning' : 'neutral'"
        :status="
          highPriorityPending > 0
            ? { label: '需优先排程', tone: 'warning' }
            : { label: '无积压', tone: 'success' }
        "
        :foot-start="
          highPriorityPending > 0
            ? '高优先工单会占用设备可用窗口，先排这些再排常规保养。'
            : '当前没有高优先级的待执行维护工单。'
        "
        :action="highPriorityPending > 0 ? { label: '看可用窗口' } : undefined"
        @action="router.push({ path: '/maintenance/availability' })"
      />
    </div>

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
      <template #cell-warrantyStatus="{ row }"
        ><NvStatusBadge :value="warrantyStatusLabel(row.warrantyStatus)"
      /></template>
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
          <!-- 开单对象在下方呈现；此处仅供读屏播报。 -->
          <NvSheetDescription class="sr-only">
            为设备 {{ createForm.deviceAssetId || '（待选择）' }} 开具维护工单。
          </NvSheetDescription>
        </NvSheetHeader>
        <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitCreate">
          <!-- 从报警行带入：设备与来源报警只读呈现，不做成看起来还能改的输入位。 -->
          <CarriedContextSummary
            v-if="createCarried"
            label="开单对象"
            :items="createCarriedItems"
          />

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField v-if="!createCarried">
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
            <NvField v-if="!createCarried">
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
              <WorkerSelect
                id="mwo-technician"
                v-model="createForm.assignedTechnicianUserId"
                placeholder="未指派"
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
          <!-- 工单事实已在下方只读区完整呈现；此处仅供读屏播报。 -->
          <NvSheetDescription class="sr-only">
            {{ completeTarget ? workOrderNo(completeTarget) : '完成维护工单' }} 的完工登记。
          </NvSheetDescription>
        </NvSheetHeader>
        <form class="grid gap-4 px-4 pb-4" @submit.prevent="submitComplete">
          <CarriedContextSummary label="完工工单" :items="completeCarriedItems" />

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
            <NvField>
              <NvFieldLabel for="mwo-actual-technician">实际执行技师</NvFieldLabel>
              <!-- #897 完工登记实际技师；keep-out-of-range 保留预填的建单指派技师（即便不在当前检索页）。 -->
              <WorkerSelect
                id="mwo-actual-technician"
                v-model="completeForm.actualTechnicianUserId"
                keep-out-of-range
                placeholder="请选择实际执行技师"
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
              class="grid items-end gap-2 rounded-md border p-3 sm:grid-cols-[1fr_5rem_8rem_6rem_auto]"
            >
              <NvField>
                <NvFieldLabel :for="`spare-sku-${row.id}`">物料</NvFieldLabel>
                <NvEntityPicker
                  :id="`spare-sku-${row.id}`"
                  :model-value="row.skuCode"
                  :options="skuOptions"
                  title="选择备件物料"
                  placeholder="选择备件物料"
                  source-text="数据来自基础数据物料主数据"
                  empty-text="暂无物料主数据，请先在基础数据维护物料"
                  :loading="skusPending"
                  aria-label="备件物料"
                  @update:model-value="
                    (value: string) => {
                      row.skuCode = value
                      applySpareRowSku(row)
                    }
                  "
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
                <NvEntityPicker
                  :id="`spare-uom-${row.id}`"
                  v-model="row.uomCode"
                  :options="uomOptions"
                  title="选择单位"
                  placeholder="跟随物料"
                  source-text="数据来自基础数据计量单位"
                  empty-text="暂无计量单位，请先在基础数据维护单位"
                  :loading="uomsPending"
                  clearable
                  aria-label="备件单位"
                />
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
