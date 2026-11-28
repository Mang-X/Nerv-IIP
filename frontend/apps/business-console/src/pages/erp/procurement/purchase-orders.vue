<script setup lang="ts">
import { usePermission } from '@/composables/usePermission'
import type { BusinessConsoleErpPurchaseOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpPurchaseOrders } from '@/composables/erp/useBusinessErp'
import {
  useErpItemCatalog,
  useErpPartnerCatalog,
  useErpSiteCatalog,
} from '@/composables/erp/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/master-data/useBusinessPartnerNames'
import { useSkuNames } from '@/composables/master-data/useSkuNames'
import { usePagedList } from '@/composables/shared/usePagedList'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { i18n } from '@/i18n'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { RouterLink } from 'vue-router'
import {
  NvButton,
  NvDataTable,
  NvDatePicker,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvEntityPicker,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  UNAVAILABLE_TEXT,
  erpReadState,
  formatAmount,
  formatQuantity,
  pickerInvalidClass,
} from '../shared'

const { locale } = i18n.global

const { can } = usePermission()

definePage({
  meta: {
    requiresAuth: true,
    title: '采购订单',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const orders = useErpPurchaseOrders()
const auth = useAuthStore()
const permissions = computed(() => auth.principal?.permissionCodes ?? [])
const canManage = computed(() => can(P.erpProcurementManage))
const canReadApprovals = computed(() => permissions.value.includes(P.approvalsRead))
// 供应商 / 工厂 / 物料 / 单位一律从主数据目录里选，手输编码只会在提交时才发现敲错。
const { supplierOptions, partnersPending } = useErpPartnerCatalog()
const { siteOptions, sitesPending } = useErpSiteCatalog()
const { uomOptions, uomsPending, baseUomBySku } = useErpItemCatalog()
// 列表侧另需 code→name 反查（目录只给下拉选项，不做反查）；底层同一份查询，不会重复请求。
const { resolvePartner } = useBusinessPartnerNames()
const { resolveSkuName } = useSkuNames()
const { page, pageSize } = usePagedList(orders.filters, {
  resetOn: [() => orders.filters.status, () => orders.filters.keyword],
})

const statusFilter = computed({
  get: () => orders.filters.status || 'all',
  set: (value: string) => {
    orders.filters.status = value === 'all' ? undefined : value
  },
})

const rows = computed(() =>
  orders.items.value.flatMap((order) =>
    (order.lines ?? []).map((line) => ({
      purchaseOrderNo: order.purchaseOrderNo ?? '-',
      supplierCode: order.supplierCode ?? '-',
      supplierName: resolvePartner(order.supplierCode),
      siteCode: order.siteCode ?? '-',
      status: order.status ?? '-',
      order,
      receiptReadiness: order.receiptReadiness ?? '-',
      lineNo: line.lineNo ?? '-',
      skuCode: line.skuCode ?? '-',
      skuName: resolveSkuName(line.skuCode),
      sourceRequisitions:
        (line.sources ?? [])
          .map((source) => source.purchaseRequisitionNo)
          .filter(Boolean)
          .join(', ') || '-',
      orderedQuantity: line.orderedQuantity ?? 0,
      receivedQuantity: line.receivedQuantity ?? 0,
      openQuantity: Math.max((line.orderedQuantity ?? 0) - (line.receivedQuantity ?? 0), 0),
      amount: (line.orderedQuantity ?? 0) * (line.unitPrice ?? 0),
    })),
  ),
)

const columns: NvDataTableColumn<(typeof rows.value)[number]>[] = [
  {
    key: 'purchaseOrderNo',
    header: '采购单',
    width: 'w-56',
    cellClass: 'font-medium whitespace-normal break-all',
  },
  { key: 'status', header: '订单状态', width: 'w-24' },
  { key: 'receiptReadiness', header: '收货状态', width: 'w-24' },
  {
    key: 'supplierCode',
    header: '供应商',
    width: 'w-40',
    cellClass: 'whitespace-normal break-all',
  },
  { key: 'skuCode', header: '物料', width: 'w-40', cellClass: 'whitespace-normal break-all' },
  {
    key: 'sourceRequisitions',
    header: '来源申请',
    width: 'w-28',
    cellClass: 'whitespace-normal break-all',
  },
  { key: 'orderedQuantity', header: '订单数量', align: 'end', width: 'w-28' },
  { key: 'receivedQuantity', header: '已收数量', align: 'end', width: 'w-28' },
  { key: 'openQuantity', header: '未到数量', align: 'end', width: 'w-28' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32' },
  {
    key: 'actions',
    header: '审批',
    align: 'end',
    width: 'w-44',
    headerClass: 'sticky right-0 z-20 bg-card shadow-[-1px_0_0_0_var(--border)]',
    cellClass: 'sticky right-0 z-10 bg-card shadow-[-1px_0_0_0_var(--border)]',
  },
]

const readState = computed(() =>
  erpReadState({
    noun: '采购订单',
    unit: '张',
    ready: orders.ready.value,
    pending: orders.pending.value,
    error: orders.error.value,
    total: orders.total.value,
    filtered: Boolean(orders.filters.keyword || orders.filters.status),
    emptyHint: '还没有采购订单。已批准的供应商报价或采购申请转单后会在这里出现。',
  }),
)

const openQuantity = computed(() => rows.value.reduce((sum, row) => sum + row.openQuantity, 0))
const orderAmount = computed(() => rows.value.reduce((sum, row) => sum + row.amount, 0))
const orderCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'open-quantity',
    label: '未到数量',
    // 取不到订单时显 `—`：0 会被读成"供应商都交齐了"。
    value: readState.value.trustworthy ? formatQuantity(openQuantity.value) : UNAVAILABLE_TEXT,
    meta: readState.value.trustworthy ? '已下达但供应商尚未交付' : readState.value.emptyMessage,
  },
  {
    key: 'amount',
    label: '订单金额',
    value: readState.value.trustworthy ? formatAmount(orderAmount.value) : UNAVAILABLE_TEXT,
    meta: readState.value.trustworthy
      ? `当前列表 ${rows.value.length} 行采购明细合计`
      : readState.value.emptyMessage,
  },
])

const open = shallowRef(false)
const form = reactive({ supplierCode: '', siteCode: '' })
let nextLineId = 0
function newLine() {
  return {
    id: ++nextLineId,
    skuCode: '',
    uomCode: '',
    quantity: '1',
    promisedDate: '',
    unitPrice: '',
  }
}
const lines = ref([newLine()])
watch(
  () =>
    lines.value.map((line) => ({
      id: line.id,
      sku: line.skuCode,
      uom: baseUomBySku.value.get(line.skuCode.trim()),
    })),
  (defaults, previous) => {
    const previousById = new Map(previous.map((entry) => [entry.id, entry]))
    defaults.forEach(({ id, sku, uom }, index) => {
      const before = previousById.get(id)
      if (uom && (before?.sku !== sku || before?.uom !== uom)) lines.value[index]!.uomCode = uom
    })
  },
)
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  supplierCode: !form.supplierCode.trim(),
  siteCode: !form.siteCode.trim(),
}))
const invalidLines = computed(() =>
  lines.value.map((line) => ({
    skuCode: !line.skuCode.trim(),
    uomCode: !line.uomCode.trim(),
    promisedDate: !line.promisedDate,
    quantity: !Number.isFinite(Number(line.quantity)) || Number(line.quantity) <= 0,
    unitPrice:
      String(line.unitPrice).trim() === '' ||
      !Number.isFinite(Number(line.unitPrice)) ||
      Number(line.unitPrice) <= 0,
  })),
)
const canSubmit = computed(
  () =>
    !Object.values(invalid.value).some(Boolean) &&
    invalidLines.value.every((line) => !Object.values(line).some(Boolean)),
)
const draftTotal = computed(() =>
  lines.value.reduce(
    (total, line) => total + (Number(line.quantity) * Number(line.unitPrice) || 0),
    0,
  ),
)

function openDialog() {
  form.supplierCode = ''
  form.siteCode = ''
  lines.value = [newLine()]
  showErrors.value = false
  open.value = true
}

async function submit() {
  if (!can(P.erpProcurementManage)) return
  if (orders.createPurchaseOrderPending.value) return
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await orders.createPurchaseOrder({
      supplierCode: form.supplierCode.trim(),
      siteCode: form.siteCode.trim(),
      lines: lines.value.map((line, index) => ({
        lineNo: String((index + 1) * 10),
        skuCode: line.skuCode.trim(),
        uomCode: line.uomCode.trim(),
        quantity: Number(line.quantity),
        promisedDate: line.promisedDate,
        unitPrice: Number(line.unitPrice),
      })),
    })
    open.value = false
    notifySuccess('采购订单已创建')
  } catch (error) {
    notifyOperationFailure(
      '创建采购单失败',
      orders.createPurchaseOrderError.value ?? error,
      '创建采购单失败，请稍后重试。',
    )
  }
}

const revisionOpen = shallowRef(false)
const revisionError = shallowRef('')
const revisionOrderNo = shallowRef('')
const revisionLines = reactive<
  {
    lineNo: string
    skuCode: string
    orderedQuantity: number | string
    unitPrice: number | string
    promisedDate: string
  }[]
>([])

function openRevision(order: BusinessConsoleErpPurchaseOrderItem) {
  if (!canManage.value || !order.canReviseBeforeApproval || !order.purchaseOrderNo) return
  revisionOrderNo.value = order.purchaseOrderNo
  revisionLines.splice(
    0,
    revisionLines.length,
    ...(order.lines ?? []).map((line) => ({
      lineNo: line.lineNo ?? '',
      skuCode: line.skuCode ?? '',
      orderedQuantity: line.orderedQuantity ?? 0,
      unitPrice: line.unitPrice ?? 0,
      promisedDate: line.promisedDate ?? '',
    })),
  )
  revisionError.value = ''
  revisionOpen.value = true
}

watch([orders.organizationId, orders.environmentId], () => {
  revisionOpen.value = false
})

async function submitRevision() {
  if (!can(P.erpProcurementManage)) return
  if (!canManage.value || orders.revisePurchaseOrderPending.value) return
  const order = orders.items.value.find((item) => item.purchaseOrderNo === revisionOrderNo.value)
  if (!order?.canReviseBeforeApproval) return

  const lines = revisionLines
    .map((line) => ({
      lineNo: line.lineNo,
      orderedQuantity: Number(line.orderedQuantity),
      unitPrice: Number(line.unitPrice),
      promisedDate: line.promisedDate,
    }))
    .filter((line) => {
      const original = order.lines?.find((item) => item.lineNo === line.lineNo)
      return (
        original?.orderedQuantity !== line.orderedQuantity ||
        original?.unitPrice !== line.unitPrice ||
        original?.promisedDate !== line.promisedDate
      )
    })
  if (
    lines.length === 0 ||
    lines.some((line) => !(line.orderedQuantity > 0) || !(line.unitPrice > 0) || !line.promisedDate)
  ) {
    revisionError.value = '请至少修改一行，数量与单价须大于 0，并填写交期。'
    return
  }

  try {
    revisionError.value = ''
    const result = await orders.revisePurchaseOrder(revisionOrderNo.value, lines)
    revisionOpen.value = false
    notifySuccess(
      `采购单已重提审批${result?.approvalChainId ? `（${result.approvalChainId}）` : ''}`,
    )
  } catch (error) {
    notifyOperationFailure('采购单重提失败', error, '采购单重提失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采购订单"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="readState.count"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="orders.pending.value"
          @click="orders.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton v-if="canManage" size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          新建采购单
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="orderCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="orders.filters.keyword"
          class="h-9 w-64"
          placeholder="采购单 / 供应商 / 物料 / 工厂"
          aria-label="采购订单关键字"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="订单状态"
            ><NvSelectValue placeholder="订单状态"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部订单</NvSelectItem>
            <NvSelectItem value="Released">已下达</NvSelectItem>
            <NvSelectItem value="Closed">已关闭</NvSelectItem>
            <NvSelectItem value="Cancelled">已取消</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="orders.total.value"
      :columns="columns"
      :rows="rows"
      :row-key="(r) => `${r.purchaseOrderNo}-${r.lineNo}`"
      :loading="orders.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="orders.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-supplierCode="{ row }">
        <CodeWithNameCell :code="row.supplierCode" :name="row.supplierName" />
      </template>
      <template #cell-skuCode="{ row }">
        <CodeWithNameCell :code="row.skuCode" :name="row.skuName" fallback="未指定物料" />
      </template>
      <template #cell-orderedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.orderedQuantity) }}</span></template
      >
      <template #cell-receivedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.receivedQuantity) }}</span></template
      >
      <template #cell-openQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.openQuantity) }}</span></template
      >
      <template #cell-status="{ row }">
        <NvStatusBadge
          :value="row.status"
          :label="row.order.canReviseBeforeApproval ? '待修订' : undefined"
        />
      </template>
      <template #cell-receiptReadiness="{ row }"
        ><NvStatusBadge :value="row.receiptReadiness"
      /></template>
      <template #cell-amount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.amount) }}</span></template
      >
      <template #cell-actions="{ row }">
        <div
          v-if="row.lineNo === row.order.lines?.[0]?.lineNo"
          class="flex items-center justify-end gap-2"
        >
          <RouterLink
            v-if="
              canReadApprovals && (row.order.approvalChainId || row.order.canReviseBeforeApproval)
            "
            class="text-sm text-primary underline"
            :to="{
              path: '/approval',
              query: {
                documentType: 'purchase-order',
                documentId: row.purchaseOrderNo,
                sourceService: 'business-erp',
              },
            }"
            >审批记录</RouterLink
          >
          <NvButton
            v-if="canManage && row.order.canReviseBeforeApproval"
            size="sm"
            variant="outline"
            @click="openRevision(row.order)"
          >
            修订并重提
          </NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="revisionOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>修订采购单 {{ revisionOrderNo }}</NvDialogTitle>
          <NvDialogDescription>修改采购明细后提交新一轮审批。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRevision">
          <fieldset
            v-for="(line, index) in revisionLines"
            :key="line.lineNo"
            class="grid gap-3 sm:grid-cols-3"
          >
            <legend class="mb-2 text-sm font-medium">{{ line.lineNo }} · {{ line.skuCode }}</legend>
            <NvField>
              <NvFieldLabel :for="`revision-quantity-${index}`">数量</NvFieldLabel>
              <NvInput
                :id="`revision-quantity-${index}`"
                v-model="line.orderedQuantity"
                type="number"
                step="any"
                required
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="`revision-price-${index}`">单价</NvFieldLabel>
              <NvInput
                :id="`revision-price-${index}`"
                v-model="line.unitPrice"
                type="number"
                step="any"
                required
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="`revision-date-${index}`">交期</NvFieldLabel>
              <NvInput
                :id="`revision-date-${index}`"
                v-model="line.promisedDate"
                type="date"
                required
              />
            </NvField>
          </fieldset>
          <NvFieldError v-if="revisionError" :errors="[revisionError]" />
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="orders.revisePurchaseOrderPending.value"
              >提交审批</NvButton
            >
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-if="canManage" v-model:open="open">
      <NvDialogContent class="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>新建采购订单</NvDialogTitle>
          <NvDialogDescription class="sr-only">向供应商下达单项物料采购。</NvDialogDescription>
        </NvDialogHeader>
        <form novalidate class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="erp-po-supplier">
                供应商 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-po-supplier"
                v-model="form.supplierCode"
                :options="supplierOptions"
                title="选择供应商"
                placeholder="选择供应商"
                empty-text="暂无供应商，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="供应商"
                :class="pickerInvalidClass(showErrors && invalid.supplierCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-po-site">
                工厂 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-po-site"
                v-model="form.siteCode"
                :options="siteOptions"
                title="选择工厂"
                placeholder="选择收货工厂"
                empty-text="暂无工厂，请先在「基础数据 · 工厂」维护"
                :loading="sitesPending"
                aria-label="工厂"
                :class="pickerInvalidClass(showErrors && invalid.siteCode)"
              />
            </NvField>
          </NvFieldGroup>
          <fieldset
            v-for="(line, index) in lines"
            :key="line.id"
            class="grid gap-3 rounded-md border p-3"
          >
            <legend class="px-1 text-sm font-medium">物料明细 {{ index + 1 }}</legend>
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField>
                <NvFieldLabel :for="`erp-po-sku-${line.id}`">
                  物料 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <DirectoryPicker
                  :id="`erp-po-sku-${line.id}`"
                  v-model="line.skuCode"
                  directory-type="material"
                  creatable
                  :class="pickerInvalidClass(showErrors && invalidLines[index]?.skuCode)"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-po-uom-${line.id}`">
                  单位 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvEntityPicker
                  :id="`erp-po-uom-${line.id}`"
                  v-model="line.uomCode"
                  :options="uomOptions"
                  title="选择单位"
                  placeholder="选择采购单位"
                  empty-text="暂无计量单位，请先在「基础数据 · 计量单位」维护"
                  :loading="uomsPending"
                  aria-label="单位"
                  :class="pickerInvalidClass(showErrors && invalidLines[index]?.uomCode)"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-po-qty-${line.id}`">
                  数量 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvInput
                  :id="`erp-po-qty-${line.id}`"
                  v-model="line.quantity"
                  type="number"
                  step="any"
                  :invalid="showErrors && invalidLines[index]?.quantity"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-po-price-${line.id}`">
                  单价（元） <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvInput
                  :id="`erp-po-price-${line.id}`"
                  v-model="line.unitPrice"
                  type="number"
                  min="0"
                  step="0.01"
                  :invalid="showErrors && invalidLines[index]?.unitPrice"
                />
              </NvField>
              <NvField>
                <NvFieldLabel :for="`erp-po-date-${line.id}`">
                  承诺日期 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvDatePicker
                  :id="`erp-po-date-${line.id}`"
                  v-model="line.promisedDate"
                  :locale="locale"
                  clearable
                  :aria-invalid="showErrors && invalidLines[index]?.promisedDate"
                  :class="pickerInvalidClass(showErrors && invalidLines[index]?.promisedDate)"
                />
              </NvField>
            </NvFieldGroup>
            <NvButton
              type="button"
              variant="outline"
              class="justify-self-start"
              :disabled="lines.length === 1"
              :aria-label="`删除第 ${index + 1} 行`"
              @click="lines.splice(index, 1)"
              >删除行</NvButton
            >
            <p
              v-if="showErrors && Object.values(invalidLines[index] ?? {}).some(Boolean)"
              role="alert"
              class="text-sm text-destructive"
            >
              请核对第 {{ index + 1 }} 行标出的字段。
            </p>
          </fieldset>
          <NvButton
            type="button"
            variant="outline"
            class="justify-self-start"
            @click="lines.push(newLine())"
            >添加明细</NvButton
          >
          <p class="text-sm font-medium" aria-live="polite">合计 {{ formatAmount(draftTotal) }}</p>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请选择供应商、工厂、物料、单位，填写承诺日期，并给出正数数量与正数单价。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="orders.createPurchaseOrderPending.value">
              <NvSpinner v-if="orders.createPurchaseOrderPending.value" aria-hidden="true" />
              创建采购单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
