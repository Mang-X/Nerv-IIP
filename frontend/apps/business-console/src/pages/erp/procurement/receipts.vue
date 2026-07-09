<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpPurchaseReceipts } from '@/composables/useBusinessErp'
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
  Spinner,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { formatError, formatQuantity } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '采购收货',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const receipts = useErpPurchaseReceipts()
const { page, pageSize } = usePagedList(receipts.filters, {
  resetOn: [() => receipts.filters.keyword],
})

const rows = computed(() =>
  receipts.items.value.flatMap((order) =>
    (order.lines ?? []).map((line) => ({
      purchaseOrderNo: order.purchaseOrderNo ?? '-',
      supplierCode: order.supplierCode ?? '-',
      status: order.status ?? '-',
      receiptReadiness: order.receiptReadiness ?? '-',
      lineNo: line.lineNo ?? '-',
      skuCode: line.skuCode ?? '-',
      orderedQuantity: line.orderedQuantity ?? 0,
      receivedQuantity: line.receivedQuantity ?? 0,
      openQuantity: Math.max((line.orderedQuantity ?? 0) - (line.receivedQuantity ?? 0), 0),
    })),
  ),
)

const columns: NvDataTableColumn<(typeof rows.value)[number]>[] = [
  { key: 'purchaseOrderNo', header: '采购单', cellClass: 'font-medium' },
  { key: 'supplierCode', header: '供应商' },
  { key: 'lineNo', header: '行号', width: 'w-20' },
  { key: 'skuCode', header: '物料' },
  { key: 'orderedQuantity', header: '订单数量', align: 'end', width: 'w-28' },
  { key: 'receivedQuantity', header: '已收数量', align: 'end', width: 'w-28' },
  { key: 'openQuantity', header: '待收数量', align: 'end', width: 'w-28' },
  { key: 'receiptReadiness', header: '收货状态', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

const receivableLines = computed(() => rows.value.filter((row) => row.openQuantity > 0).length)
const openQuantity = computed(() => rows.value.reduce((sum, row) => sum + row.openQuantity, 0))

const open = shallowRef(false)
const form = reactive({
  purchaseOrderNo: '',
  lineNo: '',
  receivedQuantity: '1',
  purchaseReceiptNo: '',
})
const formError = shallowRef('')

function openDialog(row?: (typeof rows.value)[number]) {
  form.purchaseOrderNo = row?.purchaseOrderNo === '-' ? '' : (row?.purchaseOrderNo ?? '')
  form.lineNo = row?.lineNo === '-' ? '' : (row?.lineNo ?? '')
  form.receivedQuantity = String(row?.openQuantity && row.openQuantity > 0 ? row.openQuantity : 1)
  form.purchaseReceiptNo = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  const receivedQuantity = Number(form.receivedQuantity)
  if (!form.purchaseOrderNo.trim() || !form.lineNo.trim()) {
    formError.value = '请填写采购单和行号。'
    return
  }
  if (!(receivedQuantity > 0)) {
    formError.value = '收货数量需为正数。'
    return
  }
  try {
    await receipts.recordPurchaseReceipt({
      purchaseOrderNo: form.purchaseOrderNo.trim(),
      purchaseReceiptNo: form.purchaseReceiptNo.trim() || undefined,
      lines: [{ purchaseOrderLineNo: form.lineNo.trim(), receivedQuantity }],
    })
    open.value = false
    toast.success('采购收货已记录')
  } catch {
    formError.value =
      formatError(receipts.recordPurchaseReceiptError.value) || '收货失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采购收货"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="`${receipts.total.value} 张采购单来源`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="receipts.pending.value"
          @click="receipts.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog()">
          <PlusIcon aria-hidden="true" />
          登记收货
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="可收货行" :value="receivableLines" hint="本页仍有待收数量" />
      <NvSectionCard
        description="待收数量"
        :value="formatQuantity(openQuantity)"
        hint="本页未完成收货数量"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="receipts.filters.keyword"
          class="h-9 w-64"
          placeholder="采购单 / 供应商 / 物料"
          aria-label="采购收货关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="receipts.total.value"
      :columns="columns"
      :rows="rows"
      :row-key="(r) => `${r.purchaseOrderNo}-${r.lineNo}`"
      :loading="receipts.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无可收货采购订单。采购订单释放后会在这里跟进入库。"
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
      <template #cell-receiptReadiness="{ row }"
        ><NvStatusBadge :value="row.receiptReadiness"
      /></template>
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="row.openQuantity <= 0"
          @click="openDialog(row)"
          >登记收货</NvButton
        >
      </template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>登记采购收货</NvDialogTitle>
          <NvDialogDescription
            >按采购订单行记录真实收货，后端负责暂估和后续库存/WMS 联动。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField
              ><NvFieldLabel for="erp-receipt-po">采购单</NvFieldLabel
              ><NvInput id="erp-receipt-po" v-model="form.purchaseOrderNo" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-receipt-line">采购行</NvFieldLabel
              ><NvInput id="erp-receipt-line" v-model="form.lineNo" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-receipt-qty">收货数量</NvFieldLabel
              ><NvInput
                id="erp-receipt-qty"
                v-model="form.receivedQuantity"
                type="number"
                min="1"
                step="1"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-receipt-no">收货单号（可选）</NvFieldLabel
              ><NvInput id="erp-receipt-no" v-model="form.purchaseReceiptNo" autocomplete="off"
            /></NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="receipts.recordPurchaseReceiptPending.value">
              <Spinner v-if="receipts.recordPurchaseReceiptPending.value" aria-hidden="true" />
              提交收货
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
