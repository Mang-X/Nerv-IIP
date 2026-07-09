<script setup lang="ts">
import type { BusinessConsoleErpRequestForQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpSupplierQuotations } from '@/composables/useBusinessErp'
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
    title: '供应商报价',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const quotes = useErpSupplierQuotations()
const { page, pageSize } = usePagedList(quotes.filters, { resetOn: [() => quotes.filters.keyword] })

const columns: NvDataTableColumn<BusinessConsoleErpRequestForQuotationItem>[] = [
  { key: 'rfqNo', header: '关联 RFQ', cellClass: 'font-medium', accessor: (r) => r.rfqNo ?? '-' },
  {
    key: 'supplierCodes',
    header: '询价供应商',
    accessor: (r) => (r.supplierCodes ?? []).join(' / ') || '-',
  },
  {
    key: 'lineCount',
    header: '询价明细',
    align: 'end',
    width: 'w-24',
    accessor: (r) => r.lines?.length ?? 0,
  },
  { key: 'status', header: 'RFQ 状态', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

const quoteableCount = computed(
  () => quotes.items.value.filter((r) => (r.status ?? '').toLowerCase() === 'open').length,
)
const lineQuantity = computed(() =>
  quotes.items.value
    .flatMap((r) => r.lines ?? [])
    .reduce((sum, line) => sum + (line.quantity ?? 0), 0),
)

const open = shallowRef(false)
const form = reactive({
  rfqNo: '',
  supplierCode: '',
  quotationNo: '',
  skuCode: '',
  uomCode: 'EA',
  quantity: '1',
  unitPrice: '0',
  promisedDate: '',
})
const formError = shallowRef('')

function openDialog(row?: BusinessConsoleErpRequestForQuotationItem) {
  const firstLine = row?.lines?.[0]
  form.rfqNo = row?.rfqNo ?? ''
  form.supplierCode = row?.supplierCodes?.[0] ?? ''
  form.quotationNo = ''
  form.skuCode = firstLine?.skuCode ?? ''
  form.uomCode = firstLine?.uomCode ?? 'EA'
  form.quantity = String(firstLine?.quantity ?? 1)
  form.unitPrice = '0'
  form.promisedDate = firstLine?.requiredDate ?? ''
  formError.value = ''
  open.value = true
}

async function submit() {
  const quantity = Number(form.quantity)
  const unitPrice = Number(form.unitPrice)
  if (
    !form.rfqNo.trim() ||
    !form.supplierCode.trim() ||
    !form.skuCode.trim() ||
    !form.uomCode.trim() ||
    !form.promisedDate
  ) {
    formError.value = '请填写 RFQ、供应商、物料、单位和承诺日期。'
    return
  }
  if (!(quantity > 0) || !(unitPrice >= 0)) {
    formError.value = '数量需为正数、单价不可为负。'
    return
  }
  try {
    await quotes.receiveSupplierQuotation({
      rfqNo: form.rfqNo.trim(),
      supplierCode: form.supplierCode.trim(),
      quotationNo: form.quotationNo.trim() || undefined,
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
    toast.success('供应商报价已录入')
  } catch {
    formError.value =
      formatError(quotes.receiveSupplierQuotationError.value) || '录入失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="供应商报价"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="`${quotes.total.value} 张 RFQ 来源`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="quotes.pending.value"
          @click="quotes.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog()">
          <PlusIcon aria-hidden="true" />
          录入报价
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard
        description="可回价 RFQ"
        :value="quoteableCount"
        hint="当前后端以 RFQ 为报价入口"
      />
      <NvSectionCard
        description="询价数量"
        :value="formatQuantity(lineQuantity)"
        hint="本页 RFQ 明细数量"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="quotes.filters.keyword"
          class="h-9 w-64"
          placeholder="RFQ / 供应商 / 物料"
          aria-label="供应商报价关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="quotes.total.value"
      :columns="columns"
      :rows="quotes.items.value"
      :row-key="(r: BusinessConsoleErpRequestForQuotationItem) => r.rfqNo ?? 'RFQ'"
      :loading="quotes.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无可回价 RFQ。先在 RFQ 页面发起询价。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
      <template #cell-actions="{ row }">
        <NvButton size="sm" type="button" variant="outline" @click="openDialog(row)"
          >录入报价</NvButton
        >
      </template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>录入供应商报价</NvDialogTitle>
          <NvDialogDescription
            >记录真实供应商回价。当前后端尚未提供供应商报价列表读面，提交后回到 RFQ
            来源继续跟进。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="erp-sq-rfq">RFQ</NvFieldLabel>
              <NvInput id="erp-sq-rfq" v-model="form.rfqNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-supplier">供应商</NvFieldLabel>
              <NvInput id="erp-sq-supplier" v-model="form.supplierCode" autocomplete="off" />
            </NvField>
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="erp-sq-no">供应商报价号（可选）</NvFieldLabel>
              <NvInput id="erp-sq-no" v-model="form.quotationNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-sku">物料</NvFieldLabel>
              <NvInput id="erp-sq-sku" v-model="form.skuCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-uom">单位</NvFieldLabel>
              <NvInput id="erp-sq-uom" v-model="form.uomCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-qty">数量</NvFieldLabel>
              <NvInput id="erp-sq-qty" v-model="form.quantity" type="number" min="1" step="1" />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-price">单价（元）</NvFieldLabel>
              <NvInput
                id="erp-sq-price"
                v-model="form.unitPrice"
                type="number"
                min="0"
                step="0.01"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-date">承诺日期</NvFieldLabel>
              <NvInput id="erp-sq-date" v-model="form.promisedDate" type="date" />
            </NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="quotes.receiveSupplierQuotationPending.value">
              <Spinner v-if="quotes.receiveSupplierQuotationPending.value" aria-hidden="true" />
              提交报价
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
