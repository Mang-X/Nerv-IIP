<script setup lang="ts">
import type { BusinessConsoleErpRequestForQuotationItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpSupplierQuotations } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
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
import { RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { formatDate, formatQuantity } from '../shared'

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
const quoteCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'quoteable',
    label: '可回价 RFQ',
    value: quoteableCount.value,
    unit: '单',
    meta: '报价从询价单发起',
  },
  {
    key: 'quantity',
    label: '询价数量',
    value: formatQuantity(lineQuantity.value),
    meta: `当前列表 ${quotes.items.value.length} 张询价单合计`,
  },
])

// 「带出式录入」：回价对象只能由所选询价单行带入——RFQ / 物料 / 单位 / 数量只读带出，
// 用户只补供应商真正给出的新信息（单价、承诺日期、对方报价号）。
const open = shallowRef(false)
const quoteRow = shallowRef<BusinessConsoleErpRequestForQuotationItem | null>(null)
const form = reactive({ supplierCode: '', quotationNo: '', unitPrice: '0', promisedDate: '' })
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)

/** 询价供应商候选：只有一家时自动选中并只读带出，多家时才需要用户挑。 */
const supplierOptions = computed(() =>
  (quoteRow.value?.supplierCodes ?? []).map((code) => (code ?? '').trim()).filter(Boolean),
)
const needsSupplierChoice = computed(() => supplierOptions.value.length > 1)
const quotedLine = computed(() => quoteRow.value?.lines?.[0])

const quoteContextItems = computed(() => {
  const row = quoteRow.value
  if (!row) return []
  const line = quotedLine.value
  return [
    { label: '询价单', value: row.rfqNo },
    { label: '供应商', value: needsSupplierChoice.value ? undefined : supplierOptions.value[0] },
    { label: '物料', value: line?.skuCode },
    {
      label: '询价数量',
      value:
        line?.quantity === null || line?.quantity === undefined
          ? undefined
          : `${formatQuantity(line.quantity)}${line.uomCode ? ` ${line.uomCode}` : ''}`,
    },
    { label: '需求日期', value: line?.requiredDate ? formatDate(line.requiredDate) : undefined },
  ]
})

const invalid = computed(() => ({
  supplierCode: !form.supplierCode.trim(),
  promisedDate: !form.promisedDate,
  unitPrice: !(Number(form.unitPrice) >= 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog(row: BusinessConsoleErpRequestForQuotationItem) {
  quoteRow.value = row
  const codes = (row.supplierCodes ?? []).map((code) => (code ?? '').trim()).filter(Boolean)
  // 只有一家询价供应商时自动选中，不让用户再点一次。
  form.supplierCode = codes.length === 1 ? codes[0]! : ''
  form.quotationNo = ''
  form.unitPrice = '0'
  // 承诺日期默认取询价需求日期，供应商改期时才动。
  form.promisedDate = row.lines?.[0]?.requiredDate ?? ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  const row = quoteRow.value
  const line = quotedLine.value
  if (!row?.rfqNo || !line?.skuCode) return
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await quotes.receiveSupplierQuotation({
      rfqNo: row.rfqNo,
      supplierCode: form.supplierCode.trim(),
      quotationNo: form.quotationNo.trim() || undefined,
      lines: [
        {
          lineNo: '10',
          skuCode: line.skuCode,
          uomCode: line.uomCode ?? 'EA',
          quantity: line.quantity ?? 1,
          unitPrice: Number(form.unitPrice),
          promisedDate: form.promisedDate,
        },
      ],
    })
    open.value = false
    notifySuccess(`${row.rfqNo} 的供应商报价已录入`)
  } catch (error) {
    notifyError(quotes.receiveSupplierQuotationError.value ?? error, '录入报价失败，请稍后重试。')
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
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="quoteCells" />

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
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!row.rfqNo || !row.lines?.length"
          @click="openDialog(row)"
          >录入报价</NvButton
        >
      </template>
    </NvDataTable>

    <!-- 「带出式录入」：RFQ / 物料 / 单位 / 数量由所选询价行带出，只读呈现，不做输入位。 -->
    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>录入供应商报价</NvDialogTitle>
          <NvDialogDescription class="sr-only">
            回价对象：询价单 {{ quoteRow?.rfqNo ?? '' }}。
          </NvDialogDescription>
        </NvDialogHeader>
        <form v-if="quoteRow" class="grid gap-4" @submit.prevent="submit">
          <CarriedContextSummary label="回价对象" :items="quoteContextItems" />
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField v-if="needsSupplierChoice">
              <NvFieldLabel for="erp-sq-supplier">
                回价供应商 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvSelect v-model="form.supplierCode">
                <NvSelectTrigger
                  id="erp-sq-supplier"
                  :data-invalid="showErrors && invalid.supplierCode ? '' : undefined"
                >
                  <NvSelectValue placeholder="选择供应商" />
                </NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="code in supplierOptions" :key="code" :value="code">{{
                    code
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-price">
                单价（元） <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-sq-price"
                v-model="form.unitPrice"
                type="number"
                min="0"
                step="0.01"
                autofocus
                :data-invalid="showErrors && invalid.unitPrice ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-sq-date">
                承诺交期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-sq-date"
                v-model="form.promisedDate"
                type="date"
                :data-invalid="showErrors && invalid.promisedDate ? '' : undefined"
              />
            </NvField>
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="erp-sq-no">供应商报价号（可选）</NvFieldLabel>
              <NvInput id="erp-sq-no" v-model="form.quotationNo" autocomplete="off" />
            </NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请选择回价供应商，并填写非负单价与承诺交期。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="quotes.receiveSupplierQuotationPending.value">
              <Spinner v-if="quotes.receiveSupplierQuotationPending.value" aria-hidden="true" />
              录入报价
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
