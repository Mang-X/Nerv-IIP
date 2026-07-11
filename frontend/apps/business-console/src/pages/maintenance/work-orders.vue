<script setup lang="ts">
import type {
  BusinessConsoleCreateMaintenanceWorkOrderRequest,
  BusinessConsoleMaintenanceWorkOrderItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMaintenanceWorkOrders } from '@/composables/useBusinessMaintenance'
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
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  toast,
} from '@nerv-iip/ui'
import { CheckCircle2Icon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '维护工单', requiredPermissions: ['business.maintenance.work-orders.read'] } })

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

// 待执行 / 已完成基于本页可见行的语义统计（可行动数，非机械总数）。
const OPEN_STATUSES = new Set(['open', 'opened', 'scheduled', 'inprogress', 'in-progress'])
const pendingCount = computed(
  () => workOrders.value.filter((w) => OPEN_STATUSES.has((w.status ?? '').toLowerCase())).length,
)
const highPriorityPending = computed(
  () => workOrders.value.filter(
    (w) => OPEN_STATUSES.has((w.status ?? '').toLowerCase()) && (w.priority ?? '').toLowerCase() === 'high',
  ).length,
)

const createOpen = shallowRef(false)
const createForm = reactive({
  deviceAssetId: '',
  priority: 'medium',
  openedBy: '',
  sourceAlarmId: '',
})
const createError = shallowRef('')

const completeOpen = shallowRef(false)
const completeTarget = shallowRef<BusinessConsoleMaintenanceWorkOrderItem>()
const completeForm = reactive({
  result: 'repaired',
  downtimeReasonCode: 'preventive',
  downtimeMinutes: '30',
  sparePartSku: '',
  sparePartQuantity: '1',
})
const completeError = shallowRef('')

const listErrorMessage = computed(() => formatError(workOrdersError.value))
const createErrorMessage = computed(() => createError.value || formatError(createWorkOrderError.value))
const completeErrorMessage = computed(() => completeError.value || formatError(completeWorkOrderError.value))
const queryPrefilled = shallowRef(false)

type WorkOrderRow = BusinessConsoleMaintenanceWorkOrderItem
const columns: NvDataTableColumn<WorkOrderRow>[] = [
  { key: 'workOrderNo', header: '工单号', cellClass: 'font-medium', accessor: (r) => workOrderNo(r) },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '—' },
  { key: 'warrantyStatus', header: '保修', width: 'w-24' },
  { key: 'warrantyExpiresOn', header: '保修到期', width: 'w-28', accessor: (r) => formatDate(r.warrantyExpiresOn) },
  { key: 'supplierPartnerCode', header: '供应商', width: 'w-28', accessor: (r) => r.supplierPartnerCode ?? '—' },
  { key: 'priority', header: '优先级', width: 'w-20' },
  { key: 'status', header: '状态', width: 'w-24' },
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
function warrantyStatusLabel(value?: string | null) {
  switch ((value ?? '').toLowerCase()) {
    case 'in-warranty': return '在保'
    case 'out-of-warranty': return '出保'
    default: return '未知'
  }
}
function rowKey(row: WorkOrderRow) {
  return row.workOrderId ?? '维护工单'
}

function openCreate(prefill: Partial<typeof createForm> = {}) {
  createForm.deviceAssetId = prefill.deviceAssetId ?? ''
  createForm.priority = 'medium'
  createForm.openedBy = prefill.openedBy ?? ''
  createForm.sourceAlarmId = prefill.sourceAlarmId ?? ''
  createError.value = ''
  createOpen.value = true
}
async function submitCreate() {
  if (!createForm.deviceAssetId.trim() || !createForm.openedBy.trim()) {
    createError.value = '请填写设备与开单人。'
    return
  }
  const body: BusinessConsoleCreateMaintenanceWorkOrderRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    deviceAssetId: createForm.deviceAssetId.trim(),
    priority: createForm.priority,
    openedBy: createForm.openedBy.trim(),
    sourceAlarmId: createForm.sourceAlarmId.trim() || undefined,
  }
  try {
    await createWorkOrder(body)
    createOpen.value = false
    toast.success('维护工单已创建')
  } catch {
    // 失败信息由对话框错误区呈现。
  }
}

function openComplete(row: WorkOrderRow) {
  completeTarget.value = row
  completeForm.result = 'repaired'
  completeForm.downtimeReasonCode = 'preventive'
  completeForm.downtimeMinutes = '30'
  completeForm.sparePartSku = ''
  completeForm.sparePartQuantity = '1'
  completeError.value = ''
  completeOpen.value = true
}
async function submitComplete() {
  const target = completeTarget.value
  if (!target?.workOrderId) return
  const minutes = Number(completeForm.downtimeMinutes)
  if (!(minutes >= 0)) {
    completeError.value = '停机时长需为非负数。'
    return
  }
  // 完成需登记至少一条更换备件（领料扣减）；后端以此核销维护成本。
  if (!completeForm.sparePartSku.trim()) {
    completeError.value = '请登记一条更换备件（物料 + 数量）。'
    return
  }
  const quantity = Number(completeForm.sparePartQuantity)
  if (!(quantity > 0)) {
    completeError.value = '备件数量需为正数。'
    return
  }
  try {
    await completeWorkOrder(target.workOrderId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      result: completeForm.result,
      downtimeReasonCode: completeForm.downtimeReasonCode,
      downtimeMinutes: minutes,
      spareParts: [{ skuCode: completeForm.sparePartSku.trim(), quantity, uomCode: 'EA' }],
    })
    completeOpen.value = false
    toast.success(`维护工单 ${workOrderNo(target)} 已完成`)
  } catch {
    // 失败信息由对话框错误区呈现。
  }
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
    <NvPageHeader title="维护工单" :breadcrumbs="[{ label: '设备监控' }]" :count="`${workOrdersTotal} 张维护工单`">
      <template #actions>
        <NvButton size="sm" type="button" variant="outline" :disabled="workOrdersPending" @click="refreshWorkOrders">
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
      <NvSectionCard description="待执行 · 高优先" :value="highPriorityPending" hint="本页高优先级，需优先排程" />
    </NvSectionCards>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

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
      <template #cell-warrantyStatus="{ row }"><NvStatusBadge :value="warrantyStatusLabel(row.warrantyStatus)" /></template>
      <template #cell-priority="{ row }"><NvStatusBadge :value="priorityLabel(row.priority)" /></template>
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


    <NvDialog v-model:open="createOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建维护工单</NvDialogTitle>
          <NvDialogDescription>对设备开具维护工单，可关联触发的设备报警。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="mwo-device">设备</NvFieldLabel>
              <NvInput id="mwo-device" v-model="createForm.deviceAssetId" autocomplete="off" placeholder="如 DEV-SMT-01" />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-priority">优先级</NvFieldLabel>
              <NvSelect v-model="createForm.priority">
                <NvSelectTrigger id="mwo-priority" aria-label="优先级"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in priorityOptions" :key="o.value" :value="o.value">{{ o.label }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-opened-by">开单人</NvFieldLabel>
              <NvInput id="mwo-opened-by" v-model="createForm.openedBy" autocomplete="off" placeholder="如 巡检员-张工" />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-alarm">关联报警</NvFieldLabel>
              <NvInput id="mwo-alarm" v-model="createForm.sourceAlarmId" autocomplete="off" placeholder="可选" />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="createErrorMessage" :errors="[createErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createWorkOrderPending">
              <Spinner v-if="createWorkOrderPending" aria-hidden="true" />
              创建维护工单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="completeOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>完成维护工单</NvDialogTitle>
          <NvDialogDescription>
            {{ completeTarget ? `${workOrderNo(completeTarget)} · ${completeTarget.deviceAssetId ?? ''}` : '登记维护结果与停机时长。' }}
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitComplete">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="mwo-result">维护结果</NvFieldLabel>
              <NvSelect v-model="completeForm.result">
                <NvSelectTrigger id="mwo-result" aria-label="维护结果"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in resultOptions" :key="o.value" :value="o.value">{{ o.label }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-reason">停机原因</NvFieldLabel>
              <NvSelect v-model="completeForm.downtimeReasonCode">
                <NvSelectTrigger id="mwo-reason" aria-label="停机原因"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in reasonOptions" :key="o.value" :value="o.value">{{ o.label }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-minutes">停机时长（分钟）</NvFieldLabel>
              <NvInput id="mwo-minutes" v-model="completeForm.downtimeMinutes" type="number" min="0" step="1" />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-spare-sku">更换备件物料</NvFieldLabel>
              <NvInput id="mwo-spare-sku" v-model="completeForm.sparePartSku" autocomplete="off" placeholder="如 主控芯片MCU" />
            </NvField>
            <NvField>
              <NvFieldLabel for="mwo-spare-qty">备件数量</NvFieldLabel>
              <NvInput id="mwo-spare-qty" v-model="completeForm.sparePartQuantity" type="number" min="1" step="1" />
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="completeErrorMessage" :errors="[completeErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="completeWorkOrderPending">
              <Spinner v-if="completeWorkOrderPending" aria-hidden="true" />
              <CheckCircle2Icon v-else aria-hidden="true" />
              完成工单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
