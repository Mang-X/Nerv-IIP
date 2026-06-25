<script setup lang="ts">
import type { BusinessConsoleMesCreateReceiptRequest } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { mesReceiptStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useMesFinishedGoodsReceipts } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePaginationPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DropdownMenuProItem,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  RowActions,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { EyeIcon, PackageCheckIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '完工入库' } })

const {
  createReceiptRequest,
  createReceiptRequestError,
  createReceiptRequestPending,
  filters,
  receiptRequests,
  receiptRequestsError,
  receiptRequestsPending,
  receiptRequestsTotal,
  refreshReceiptRequests,
} = useMesFinishedGoodsReceipts()
const { page, pageSize, resetPage } = usePagedList(filters, { resetOn: [() => filters.status] })
const { resolveSku } = useMesDisplayNames()

const route = useRoute()
const successMessage = shallowRef('')
const receiptSheetOpen = shallowRef(false)
const quickViewWorkOrderId = ref<string | null>(null)

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => { filters.status = value === 'all' ? undefined : value },
})
const statusOptions = mesReceiptStatusOptions

// 完工入库申请状态的可读中文标签（与一线 / PDA 完工入库口径一致：待入库 / 部分入库 / 已入库…）。
// 通用 StatusBadge 会把 Completed 解析成「已完成」，但本域语义是「已入库」，故按入库上下文显式映射。
const RECEIPT_STATUS_LABELS: Record<string, string> = {
  Requested: '待入库',
  Pending: '待入库',
  Created: '待入库',
  Submitted: '待入库',
  PartiallyReceived: '部分入库',
  Received: '已入库',
  Completed: '已入库',
  Cancelled: '已取消',
  Rejected: '已驳回',
}
function receiptStatusLabel(status?: string | null) {
  return RECEIPT_STATUS_LABELS[status ?? ''] ?? '未知状态'
}

const form = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  workOrderId: '',
  skuId: '',
  quantity: '1',
  unitCost: '',
  uomCode: 'EA',
  requestedAtUtc: toLocalDateTimeInput(new Date()),
  idempotencyKey: '',
})

const listErrorMessage = computed(() => formatError(receiptRequestsError.value))
const createErrorMessage = computed(() => formatError(createReceiptRequestError.value))
const hasReceiptContext = computed(() => isNonEmpty(form.workOrderId) && isNonEmpty(form.skuId))
const canCreate = computed(
  () =>
    isNonEmpty(form.organizationId) &&
    isNonEmpty(form.environmentId) &&
    isNonEmpty(form.workOrderId) &&
    isNonEmpty(form.skuId) &&
    toPositiveNumber(form.quantity) !== undefined &&
    toPositiveNumber(form.unitCost) !== undefined &&
    isNonEmpty(form.uomCode) &&
    isNonEmpty(form.requestedAtUtc),
)

watch(
  () => route.query,
  (query) => {
    const workOrderId = firstQueryValue(query.workOrderId)
    const skuId = firstQueryValue(query.skuId)
    const quantity = firstQueryValue(query.quantity)
    if (workOrderId) form.workOrderId = workOrderId
    if (skuId) form.skuId = skuId
    if (quantity) form.quantity = quantity
    resetPage()
    if (workOrderId && skuId) receiptSheetOpen.value = true
  },
  { immediate: true },
)

type ReceiptRow = (typeof receiptRequests)['value'][number]
const columns: DataTableProColumn<ReceiptRow>[] = [
  { key: 'requestNo', header: '入库单', cellClass: 'font-medium', accessor: (r) => r.requestNo ?? r.receiptRequestId ?? '无' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'skuId', header: '成品', accessor: (r) => resolveSku(r.skuCode ?? r.skuId) ?? '无' },
  { key: 'quantity', header: '入库数量', align: 'end', width: 'w-28' },
  { key: 'unitCost', header: '单位成本', align: 'end', width: 'w-28' },
  { key: 'receiptStatus', header: '入库状态', width: 'w-24' },
  { key: 'requestedAtUtc', header: '登记时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function openWorkOrder(workOrderId?: string | null) {
  if (workOrderId) quickViewWorkOrderId.value = workOrderId
}
function openReceiptSheet() {
  if (!hasReceiptContext.value) return
  successMessage.value = ''
  receiptSheetOpen.value = true
}

async function submitReceiptRequest() {
  if (!canCreate.value) return
  const body: BusinessConsoleMesCreateReceiptRequest = {
    organizationId: form.organizationId.trim(),
    environmentId: form.environmentId.trim(),
    workOrderId: form.workOrderId.trim(),
    skuId: form.skuId.trim(),
    quantity: toPositiveNumber(form.quantity),
    unitCost: toPositiveNumber(form.unitCost),
    uomCode: form.uomCode.trim(),
    requestedAtUtc: toIsoFromLocalInput(form.requestedAtUtc),
    idempotencyKey: optionalText(form.idempotencyKey) ?? `receipt-${form.workOrderId.trim()}`,
  }
  await createReceiptRequest(body)
  if (createReceiptRequestError.value) return
  successMessage.value = '成品入库登记已提交，可在列表查看入库状态。'
  receiptSheetOpen.value = false
  await refreshReceiptRequests()
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function toIsoFromLocalInput(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toISOString()
}
function toLocalDateTimeInput(date: Date) {
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}
function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
function formatUnitCost(value?: number | null) {
  return value === undefined || value === null
    ? '—'
    : new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 6 }).format(value)
}
function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function firstQueryValue(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function toPositiveNumber(value: string) {
  const parsed = toOptionalNumber(value)
  return parsed !== undefined && parsed > 0 ? parsed : undefined
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
    <PageHeader title="完工入库" :breadcrumbs="[{ label: '制造执行' }]" :count="`${receiptRequestsTotal} 条入库登记`">
      <template #actions>
        <ButtonPro size="sm" type="button" :disabled="!hasReceiptContext" @click="openReceiptSheet">
          <PackageCheckIcon aria-hidden="true" />
          {{ hasReceiptContext ? '登记完工入库' : '从工单详情发起' }}
        </ButtonPro>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="receiptRequestsPending" @click="refreshReceiptRequests">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <Toolbar :show-search="false">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="按入库状态筛选"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="receiptRequests"
      row-key="receiptRequestId"
      :loading="receiptRequestsPending"
      empty-message="还没有完工入库登记。末道工序报完工后，在此把成品登记入库即会出现对应记录。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-requestNo="{ row }">
        <span v-if="row.requestNo">{{ row.requestNo }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          {{ row.workOrderNo ?? row.workOrderId }}
        </button>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-skuId="{ row }">
        <span v-if="row.skuId">{{ resolveSku(row.skuCode ?? row.skuId) }}</span>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template>
      <template #cell-unitCost="{ row }"><span class="tabular-nums">{{ formatUnitCost(row.unitCost) }}</span></template>
      <template #cell-receiptStatus="{ row }">
        <StatusBadgePro :value="row.receiptStatus" :label="receiptStatusLabel(row.receiptStatus)" />
      </template>
      <template #cell-requestedAtUtc="{ row }">{{ formatDateTime(row.requestedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <RowActions :label="`入库登记操作 ${row.requestNo ?? row.workOrderId ?? ''}`">
          <DropdownMenuProItem :disabled="!row.workOrderId" @click="openWorkOrder(row.workOrderId)">
            <EyeIcon aria-hidden="true" />
            查看工单
          </DropdownMenuProItem>
        </RowActions>
      </template>
    </DataTablePro>

    <DataTablePaginationPro
      v-model:page="page"
      :page-size="pageSize"
      :total-items="receiptRequestsTotal"
      @update:page-size="(v) => (pageSize = String(v))"
    />

    <DialogPro v-model:open="receiptSheetOpen">
      <DialogProContent>
        <DialogProHeader>
          <DialogProTitle>登记完工入库</DialogProTitle>
          <DialogProDescription>把完工成品登记入库。工单与成品由报工完成或工单详情带出，只需确认入库数量、单位成本和单位。</DialogProDescription>
        </DialogProHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitReceiptRequest">
          <p v-if="createErrorMessage" class="text-sm text-destructive" role="alert">{{ createErrorMessage }}</p>
          <p v-if="successMessage" class="text-sm text-success" role="status">{{ successMessage }}</p>

          <FieldProGroup class="grid gap-3">
            <FieldPro>
              <FieldProLabel for="receipt-work-order">工单号</FieldProLabel>
              <InputPro id="receipt-work-order" v-model="form.workOrderId" readonly required />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="receipt-sku">成品</FieldProLabel>
              <InputPro id="receipt-sku" v-model="form.skuId" readonly required />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="receipt-quantity">入库数量</FieldProLabel>
              <InputPro id="receipt-quantity" v-model="form.quantity" inputmode="decimal" min="0.000001" step="0.000001" required type="number" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="receipt-unit-cost">单位成本</FieldProLabel>
              <InputPro id="receipt-unit-cost" v-model="form.unitCost" inputmode="decimal" min="0.000001" step="0.000001" required type="number" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="receipt-uom">单位</FieldProLabel>
              <InputPro id="receipt-uom" v-model="form.uomCode" required />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="receipt-requested-at">登记时间</FieldProLabel>
              <InputPro id="receipt-requested-at" v-model="form.requestedAtUtc" required type="datetime-local" />
            </FieldPro>
          </FieldProGroup>

          <DialogProFooter>
            <ButtonPro type="button" variant="outline" @click="receiptSheetOpen = false">取消</ButtonPro>
            <ButtonPro type="submit" :disabled="createReceiptRequestPending || !canCreate">
              <Spinner v-if="createReceiptRequestPending" aria-hidden="true" />
              <PackageCheckIcon v-else aria-hidden="true" />
              提交入库登记
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
