<script setup lang="ts">
import type { BusinessConsoleErpPurchaseOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpPurchaseOrders } from '@/composables/useBusinessErp'
import {
  useErpItemCatalog,
  useErpPartnerCatalog,
  useErpSiteCatalog,
} from '@/composables/useErpPickerCatalog'
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
  NvEntityPicker,
  NvField,
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
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import { formatAmount, formatQuantity, pickerInvalidClass } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '采购订单',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const orders = useErpPurchaseOrders()
// 供应商 / 工厂 / 物料 / 单位一律从主数据目录里选，手输编码只会在提交时才发现敲错。
const { supplierOptions, partnersPending } = useErpPartnerCatalog()
const { siteOptions, sitesPending } = useErpSiteCatalog()
const { skuOptions, skusPending, uomOptions, uomsPending, baseUomBySku } = useErpItemCatalog()
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
      siteCode: order.siteCode ?? '-',
      status: order.status ?? '-',
      receiptReadiness: order.receiptReadiness ?? '-',
      lineNo: line.lineNo ?? '-',
      skuCode: line.skuCode ?? '-',
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
  { key: 'purchaseOrderNo', header: '采购单', cellClass: 'font-medium' },
  { key: 'supplierCode', header: '供应商' },
  { key: 'skuCode', header: '物料' },
  { key: 'sourceRequisitions', header: '来源申请', width: 'w-40' },
  { key: 'orderedQuantity', header: '订单数量', align: 'end', width: 'w-28' },
  { key: 'receivedQuantity', header: '已收数量', align: 'end', width: 'w-28' },
  { key: 'openQuantity', header: '未到数量', align: 'end', width: 'w-28' },
  { key: 'status', header: '订单状态', width: 'w-28' },
  { key: 'receiptReadiness', header: '收货状态', width: 'w-28' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32' },
]

const openQuantity = computed(() => rows.value.reduce((sum, row) => sum + row.openQuantity, 0))
const orderAmount = computed(() => rows.value.reduce((sum, row) => sum + row.amount, 0))
const orderCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'open-quantity',
    label: '未到数量',
    value: formatQuantity(openQuantity.value),
    meta: '已下达但供应商尚未交付',
  },
  {
    key: 'amount',
    label: '订单金额',
    value: formatAmount(orderAmount.value),
    meta: `当前列表 ${rows.value.length} 行采购明细合计`,
  },
])

const open = shallowRef(false)
const form = reactive({
  supplierCode: '',
  siteCode: '',
  skuCode: '',
  uomCode: '',
  quantity: '1',
  unitPrice: '0',
  promisedDate: '',
})
// 采购单位默认跟随物料的基本单位；用户仍可改成采购包装单位。
watch(
  () => form.skuCode,
  (skuCode) => {
    const baseUom = baseUomBySku.value.get(skuCode.trim())
    if (baseUom) form.uomCode = baseUom
  },
)
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  supplierCode: !form.supplierCode.trim(),
  siteCode: !form.siteCode.trim(),
  skuCode: !form.skuCode.trim(),
  uomCode: !form.uomCode.trim(),
  promisedDate: !form.promisedDate,
  quantity: !(Number(form.quantity) > 0),
  unitPrice: !(Number(form.unitPrice) >= 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog() {
  form.supplierCode = ''
  form.siteCode = ''
  form.skuCode = ''
  form.uomCode = ''
  form.quantity = '1'
  form.unitPrice = '0'
  form.promisedDate = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  const quantity = Number(form.quantity)
  const unitPrice = Number(form.unitPrice)
  try {
    await orders.createPurchaseOrder({
      supplierCode: form.supplierCode.trim(),
      siteCode: form.siteCode.trim(),
      lines: [
        {
          lineNo: '10',
          skuCode: form.skuCode.trim(),
          uomCode: form.uomCode.trim(),
          quantity,
          unitPrice,
          promisedDate: form.promisedDate,
        },
      ],
    })
    open.value = false
    notifySuccess('采购订单已创建')
  } catch (error) {
    notifyError(orders.createPurchaseOrderError.value ?? error, '创建采购单失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采购订单"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="`${orders.total.value} 张订单`"
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
        <NvButton size="sm" type="button" @click="openDialog">
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
      empty-message="暂无采购订单。已批准供应商报价或采购申请可转入采购订单。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-orderedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.orderedQuantity) }}</span></template
      >
      <template #cell-receivedQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.receivedQuantity) }}</span></template
      >
      <template #cell-openQuantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.openQuantity) }}</span></template
      >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-receiptReadiness="{ row }"
        ><NvStatusBadge :value="row.receiptReadiness"
      /></template>
      <template #cell-amount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.amount) }}</span></template
      >
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>新建采购订单</NvDialogTitle>
          <NvDialogDescription class="sr-only">向供应商下达单项物料采购。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
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
                source-text="数据来自基础数据业务伙伴（供应商角色）"
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
                source-text="数据来自基础数据工厂主数据"
                empty-text="暂无工厂，请先在「基础数据 · 工厂」维护"
                :loading="sitesPending"
                aria-label="工厂"
                :class="pickerInvalidClass(showErrors && invalid.siteCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-po-sku">
                物料 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-po-sku"
                v-model="form.skuCode"
                :options="skuOptions"
                title="选择物料"
                placeholder="选择物料"
                source-text="数据来自基础数据物料主数据"
                empty-text="暂无物料，请先在「基础数据 · 物料」维护"
                :loading="skusPending"
                aria-label="物料"
                :class="pickerInvalidClass(showErrors && invalid.skuCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-po-uom">
                单位 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-po-uom"
                v-model="form.uomCode"
                :options="uomOptions"
                title="选择单位"
                placeholder="选择采购单位"
                source-text="数据来自基础数据计量单位；选定物料后默认带出基本单位"
                empty-text="暂无计量单位，请先在「基础数据 · 计量单位」维护"
                :loading="uomsPending"
                aria-label="单位"
                :class="pickerInvalidClass(showErrors && invalid.uomCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-po-qty">
                数量 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-po-qty"
                v-model="form.quantity"
                type="number"
                min="1"
                step="1"
                :data-invalid="showErrors && invalid.quantity ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-po-price">
                单价（元） <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-po-price"
                v-model="form.unitPrice"
                type="number"
                min="0"
                step="0.01"
                :data-invalid="showErrors && invalid.unitPrice ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-po-date">
                承诺日期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-po-date"
                v-model="form.promisedDate"
                type="date"
                :data-invalid="showErrors && invalid.promisedDate ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请选择供应商、工厂、物料、单位，填写承诺日期，并给出正数数量与非负单价。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="orders.createPurchaseOrderPending.value">
              <Spinner v-if="orders.createPurchaseOrderPending.value" aria-hidden="true" />
              创建采购单
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
