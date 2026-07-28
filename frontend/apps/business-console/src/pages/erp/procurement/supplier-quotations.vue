<script setup lang="ts">
import type {
  BusinessConsoleErpRequestForQuotationItem,
  BusinessConsoleErpSupplierQuotationItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpRequestsForQuotation, useErpSupplierQuotations } from '@/composables/useBusinessErp'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
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
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { formatAmount, formatDate, formatDateTime, formatQuantity } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '供应商报价',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

// 主列表 = 真正的报价单（erp.supplier_quotations）；询价单只在「录入报价」弹窗里当回价对象来源。
const quotes = useErpSupplierQuotations()
const rfqs = useErpRequestsForQuotation({ status: 'Open', take: 100 })
// 供应商列/下拉显名称：读面只回编码，中文名在主数据业务伙伴里，前端按编码 join。
const { resolvePartnerLabel } = useBusinessPartnerNames()
const { page, pageSize } = usePagedList(quotes.filters, { resetOn: [() => quotes.filters.keyword] })

const columns: NvDataTableColumn<BusinessConsoleErpSupplierQuotationItem>[] = [
  {
    key: 'quotationNo',
    header: '报价单号',
    cellClass: 'font-medium',
    accessor: (r) => r.quotationNo ?? '-',
  },
  { key: 'rfqNo', header: '关联询价单', accessor: (r) => r.rfqNo ?? '-' },
  {
    key: 'supplierCode',
    header: '供应商',
    accessor: (r) => resolvePartnerLabel(r.supplierCode ?? '', '') || '-',
  },
  { key: 'skuCode', header: '物料', accessor: (r) => r.lines?.[0]?.skuCode ?? '-' },
  {
    key: 'quantity',
    header: '报价数量',
    align: 'end',
    width: 'w-28',
    accessor: (r) => {
      const line = r.lines?.[0]
      if (!line || line.quantity === null || line.quantity === undefined) return '-'
      return `${formatQuantity(line.quantity)}${line.uomCode ? ` ${line.uomCode}` : ''}`
    },
  },
  {
    key: 'unitPrice',
    header: '单价（元）',
    align: 'end',
    width: 'w-28',
    accessor: (r) => formatAmount(r.lines?.[0]?.unitPrice),
  },
  {
    key: 'totalAmount',
    header: '报价金额（元）',
    align: 'end',
    width: 'w-32',
    accessor: (r) => formatAmount(r.totalAmount),
  },
  {
    key: 'promisedDate',
    header: '承诺交期',
    width: 'w-28',
    accessor: (r) => formatDate(r.lines?.[0]?.promisedDate),
  },
  {
    key: 'receivedAtUtc',
    header: '收到时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.receivedAtUtc),
  },
]

const quotedAmount = computed(() =>
  quotes.items.value.reduce((sum, r) => sum + (r.totalAmount ?? 0), 0),
)
const quotedSupplierCount = computed(
  () => new Set(quotes.items.value.map((r) => r.supplierCode).filter(Boolean)).size,
)
const quoteCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'quoted',
    label: '已收报价',
    value: quotes.total.value,
    unit: '份',
    meta: '按收到时间倒序',
  },
  {
    key: 'amount',
    label: '报价金额',
    value: formatAmount(quotedAmount.value),
    meta: `当前列表 ${quotes.items.value.length} 份报价合计`,
  },
  {
    key: 'suppliers',
    label: '回价供应商',
    value: quotedSupplierCount.value,
    unit: '家',
    meta: `尚有 ${rfqs.total.value} 张询价单可回价`,
  },
])

// 「带出式录入」：回价对象只能由所选询价单行带入——RFQ / 物料 / 单位 / 数量只读带出，
// 用户只补供应商真正给出的新信息（单价、承诺日期、对方报价号）。
const open = shallowRef(false)
const form = reactive({
  rfqNo: '',
  supplierCode: '',
  quotationNo: '',
  unitPrice: '0',
  promisedDate: '',
})
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)

const quoteRow = computed<BusinessConsoleErpRequestForQuotationItem | null>(
  () => rfqs.items.value.find((r) => r.rfqNo === form.rfqNo) ?? null,
)
/** 询价供应商候选：只有一家时自动选中并只读带出，多家时才需要用户挑。 */
const supplierOptions = computed(() =>
  (quoteRow.value?.supplierCodes ?? []).map((code) => (code ?? '').trim()).filter(Boolean),
)
const needsSupplierChoice = computed(() => supplierOptions.value.length > 1)
const quotedLine = computed(() => quoteRow.value?.lines?.[0])

// 换询价单就重新带出：单一供应商自动落定，承诺交期默认取询价需求日期。
watch(quoteRow, (row) => {
  const codes = (row?.supplierCodes ?? []).map((code) => (code ?? '').trim()).filter(Boolean)
  form.supplierCode = codes.length === 1 ? codes[0]! : ''
  form.promisedDate = row?.lines?.[0]?.requiredDate ?? ''
})

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
  rfqNo: !form.rfqNo.trim(),
  supplierCode: !form.supplierCode.trim(),
  promisedDate: !form.promisedDate,
  unitPrice: !(Number(form.unitPrice) >= 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog() {
  form.rfqNo = ''
  form.supplierCode = ''
  form.quotationNo = ''
  form.unitPrice = '0'
  form.promisedDate = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  const row = quoteRow.value
  const line = quotedLine.value
  showErrors.value = true
  if (!canSubmit.value || !row?.rfqNo || !line?.skuCode) return
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
      :count="`${quotes.total.value} 份报价`"
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
        <NvButton size="sm" type="button" :disabled="!rfqs.items.value.length" @click="openDialog">
          录入报价
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="quoteCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="quotes.filters.keyword"
          class="h-9 w-64"
          placeholder="报价单号 / 询价单 / 供应商 / 物料"
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
      :row-key="(r: BusinessConsoleErpSupplierQuotationItem) => r.quotationNo ?? 'SQ'"
      :loading="quotes.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无供应商报价。先在询价单页面发起询价，供应商回价后在此汇总比价。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    />

    <!-- 「带出式录入」：先选回价对象（询价单），物料 / 单位 / 数量随之只读带出，不做输入位。 -->
    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>录入供应商报价</NvDialogTitle>
          <NvDialogDescription class="sr-only">
            先选择回价对象（询价单），物料与数量随询价行带出。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvField>
            <NvFieldLabel for="erp-sq-rfq">
              回价对象（询价单） <span class="text-destructive">*</span>
            </NvFieldLabel>
            <NvSelect v-model="form.rfqNo">
              <NvSelectTrigger
                id="erp-sq-rfq"
                :data-invalid="showErrors && invalid.rfqNo ? '' : undefined"
              >
                <NvSelectValue placeholder="选择询价单" />
              </NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="rfq in rfqs.items.value"
                  :key="rfq.rfqNo ?? ''"
                  :value="rfq.rfqNo ?? ''"
                >
                  {{ rfq.rfqNo }} · {{ rfq.lines?.[0]?.skuCode ?? '-' }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <CarriedContextSummary v-if="quoteRow" label="回价对象" :items="quoteContextItems" />
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
                  <NvSelectItem v-for="code in supplierOptions" :key="code" :value="code">
                    {{ resolvePartnerLabel(code) }}
                  </NvSelectItem>
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
            请选择回价询价单与供应商，并填写非负单价与承诺交期。
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
