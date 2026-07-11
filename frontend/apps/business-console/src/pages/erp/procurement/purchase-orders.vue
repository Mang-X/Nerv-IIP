<script setup lang="ts">
import type { BusinessConsoleErpPurchaseOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpPurchaseOrders } from '@/composables/useBusinessErp'
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
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { formatAmount, formatError, formatQuantity } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '采购订单',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const orders = useErpPurchaseOrders()
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

const open = shallowRef(false)
const form = reactive({
  supplierCode: '',
  siteCode: '',
  skuCode: '',
  uomCode: 'EA',
  quantity: '1',
  unitPrice: '0',
  promisedDate: '',
})
const formError = shallowRef('')

function openDialog() {
  form.supplierCode = ''
  form.siteCode = ''
  form.skuCode = ''
  form.uomCode = 'EA'
  form.quantity = '1'
  form.unitPrice = '0'
  form.promisedDate = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  const quantity = Number(form.quantity)
  const unitPrice = Number(form.unitPrice)
  if (
    !form.supplierCode.trim() ||
    !form.siteCode.trim() ||
    !form.skuCode.trim() ||
    !form.uomCode.trim() ||
    !form.promisedDate
  ) {
    formError.value = '请填写供应商、工厂、物料、单位和承诺日期。'
    return
  }
  if (!(quantity > 0) || !(unitPrice >= 0)) {
    formError.value = '数量需为正数、单价不可为负。'
    return
  }
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
    toast.success('采购订单已创建')
  } catch {
    formError.value = formatError(orders.createPurchaseOrderError.value) || '创建失败，请稍后重试。'
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

    <NvSectionCards :columns="2">
      <NvSectionCard
        description="未到数量"
        :value="formatQuantity(openQuantity)"
        hint="本页未收货数量"
      />
      <NvSectionCard
        description="订单金额"
        :value="formatAmount(orderAmount)"
        hint="本页采购金额合计"
      />
    </NvSectionCards>

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
          <NvDialogDescription>创建真实采购订单，后续可在收货页登记采购收货。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField
              ><NvFieldLabel for="erp-po-supplier">供应商</NvFieldLabel
              ><NvInput id="erp-po-supplier" v-model="form.supplierCode" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-po-site">工厂</NvFieldLabel
              ><NvInput id="erp-po-site" v-model="form.siteCode" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-po-sku">物料</NvFieldLabel
              ><NvInput id="erp-po-sku" v-model="form.skuCode" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-po-uom">单位</NvFieldLabel
              ><NvInput id="erp-po-uom" v-model="form.uomCode" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-po-qty">数量</NvFieldLabel
              ><NvInput id="erp-po-qty" v-model="form.quantity" type="number" min="1" step="1"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-po-price">单价（元）</NvFieldLabel
              ><NvInput
                id="erp-po-price"
                v-model="form.unitPrice"
                type="number"
                min="0"
                step="0.01"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-po-date">承诺日期</NvFieldLabel
              ><NvInput id="erp-po-date" v-model="form.promisedDate" type="date"
            /></NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="orders.createPurchaseOrderPending.value">
              <Spinner v-if="orders.createPurchaseOrderPending.value" aria-hidden="true" />
              创建
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
