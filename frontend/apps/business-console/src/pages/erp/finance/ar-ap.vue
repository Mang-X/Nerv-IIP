<script setup lang="ts">
import type {
  BusinessConsoleErpPayableItem,
  BusinessConsoleErpReceivableItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpPayables, useErpReceivables } from '@/composables/useBusinessErp'
import {
  useErpPartnerCatalog,
  useErpPayableSourceCatalog,
  useErpReceivableSourceCatalog,
} from '@/composables/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
import { buildKpiTrend, seriesFromDatedItems } from '@/utils/kpiTrend'
import PartnerNameCell from '@/components/erp/PartnerNameCell.vue'
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
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { UNAVAILABLE_TEXT, erpReadState, formatAmount, pickerInvalidClass } from '../shared'

definePage({
  meta: { requiresAuth: true, title: 'AR/AP', requiredPermissions: ['business.erp.finance.read'] },
})

const receivables = useErpReceivables()
const payables = useErpPayables()
// 客户/供应商与来源单据都从既有读面里选，不再手输编码。
const { customerOptions, supplierOptions, partnersPending } = useErpPartnerCatalog()
const { receivableSourceOptions, receivableSourcesPending } = useErpReceivableSourceCatalog()
const { payableSourceOptions, payableSourcesPending } = useErpPayableSourceCatalog()
// 列表侧另需 code→name 反查（目录只给下拉选项，不做反查）；底层同一份查询，不会重复请求。
const { resolvePartner } = useBusinessPartnerNames()
const receivablesPaged = usePagedList(receivables.filters, {
  resetOn: [() => receivables.filters.status, () => receivables.filters.keyword],
})
const payablesPaged = usePagedList(payables.filters, {
  resetOn: [() => payables.filters.status, () => payables.filters.keyword],
})

const receivableStatus = computed({
  get: () => receivables.filters.status || 'all',
  set: (value: string) => {
    receivables.filters.status = value === 'all' ? undefined : value
  },
})
const payableStatus = computed({
  get: () => payables.filters.status || 'all',
  set: (value: string) => {
    payables.filters.status = value === 'all' ? undefined : value
  },
})

const receivableColumns: NvDataTableColumn<BusinessConsoleErpReceivableItem>[] = [
  {
    key: 'receivableNo',
    header: '应收单号',
    cellClass: 'font-medium',
    accessor: (r) => r.receivableNo ?? '-',
  },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '-' },
  {
    key: 'customerCode',
    header: '客户',
    accessor: (r) => resolvePartner(r.customerCode) ?? r.customerCode ?? '-',
  },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  {
    key: 'openAmount',
    header: '未结',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.openAmount ?? 0,
  },
  { key: 'status', header: '状态', width: 'w-24' },
]
const payableColumns: NvDataTableColumn<BusinessConsoleErpPayableItem>[] = [
  {
    key: 'payableNo',
    header: '应付单号',
    cellClass: 'font-medium',
    accessor: (r) => r.payableNo ?? '-',
  },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '-' },
  {
    key: 'supplierCode',
    header: '供应商',
    accessor: (r) => resolvePartner(r.supplierCode) ?? r.supplierCode ?? '-',
  },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  {
    key: 'openAmount',
    header: '未结',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.openAmount ?? 0,
  },
  { key: 'status', header: '状态', width: 'w-24' },
]

/** 应收 / 应付各自的读面六档状态：两张表分别有可能失败，不能共用一个结论。 */
const receivableState = computed(() =>
  erpReadState({
    noun: '应收账款',
    unit: '笔',
    ready: receivables.ready.value,
    pending: receivables.pending.value,
    error: receivables.error.value,
    total: receivables.total.value,
    filtered: Boolean(receivables.filters.keyword || receivables.filters.status),
    emptyHint: '还没有应收账款。销售出货或手工登记后会在这里形成应收。',
  }),
)
const payableState = computed(() =>
  erpReadState({
    noun: '应付账款',
    unit: '笔',
    ready: payables.ready.value,
    pending: payables.pending.value,
    error: payables.error.value,
    total: payables.total.value,
    filtered: Boolean(payables.filters.keyword || payables.filters.status),
    emptyHint: '还没有应付账款。采购收货或手工登记后会在这里形成应付。',
  }),
)

/** 页头两路读数各自独立：一路挂了只说这一路取不到，不把另一路也抹掉。 */
const headerCount = computed(() => {
  if (receivableState.value.count === undefined && payableState.value.count === undefined) {
    return undefined
  }
  return `${receivableState.value.count ?? '应收读取中'} / ${payableState.value.count ?? '应付读取中'}`
})

const receivableAmount = computed(() =>
  receivables.items.value.reduce((sum, r) => sum + (r.openAmount ?? 0), 0),
)
const payableAmount = computed(() =>
  payables.items.value.reduce((sum, r) => sum + (r.openAmount ?? 0), 0),
)
// 应收/应付是一对读数，通栏放一行才看得出资金缺口方向；金额只能对已取回的行加总，
// 口径写进副行而不是让它冒充全量余额。
const settlementCells = computed<NvMetricStripCell[]>(() => {
  // 应收/应付明细自带 createdAtUtc，走真实数据：按开单日累加成存量口径的未结余额曲线。
  // 当前页行数太少、日期又挤在同一天时会退化成一根竖线，buildKpiTrend 自己会回落到补形状。
  const receivable = receivableState.value.trustworthy
    ? buildKpiTrend('erp.arap.receivable', receivableAmount.value, {
        kind: 'amount',
        polarity: 'neutral',
        realSeries: seriesFromDatedItems(receivables.items.value, {
          date: (r) => r.createdAtUtc,
          value: (r) => r.openAmount ?? 0,
          mode: 'cumulative',
        }),
      })
    : undefined
  const payable = payableState.value.trustworthy
    ? buildKpiTrend('erp.arap.payable', payableAmount.value, {
        kind: 'amount',
        polarity: 'neutral',
        realSeries: seriesFromDatedItems(payables.items.value, {
          date: (r) => r.createdAtUtc,
          value: (r) => r.openAmount ?? 0,
          mode: 'cumulative',
        }),
      })
    : undefined

  return [
    {
      key: 'receivable',
      label: '应收未结',
      // 取不到数时绝不显 ¥0.00——那会被当成"确实没有欠款"。
      value: receivableState.value.trustworthy
        ? formatAmount(receivableAmount.value)
        : UNAVAILABLE_TEXT,
      meta: receivableState.value.trustworthy
        ? `当前列表 ${receivables.items.value.length} 笔应收合计`
        : receivableState.value.emptyMessage,
      delta: receivable?.delta,
      series: receivable?.series,
      seriesLabels: receivable?.seriesLabels,
    },
    {
      key: 'payable',
      label: '应付未结',
      value: payableState.value.trustworthy ? formatAmount(payableAmount.value) : UNAVAILABLE_TEXT,
      meta: payableState.value.trustworthy
        ? `当前列表 ${payables.items.value.length} 笔应付合计`
        : payableState.value.emptyMessage,
      delta: payable?.delta,
      series: payable?.series,
      seriesLabels: payable?.seriesLabels,
    },
  ]
})

const receivableOpen = shallowRef(false)
const payableOpen = shallowRef(false)
const receivableForm = reactive({ sourceDocumentNo: '', customerCode: '', amount: '0' })
const payableForm = reactive({ sourceDocumentNo: '', supplierCode: '', amount: '0' })
// 两个弹窗各自的校验状态：点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const receivableShowErrors = shallowRef(false)
const payableShowErrors = shallowRef(false)
const receivableInvalid = computed(() => ({
  sourceDocumentNo: !receivableForm.sourceDocumentNo.trim(),
  customerCode: !receivableForm.customerCode.trim(),
  amount: !(Number(receivableForm.amount) > 0),
}))
const payableInvalid = computed(() => ({
  sourceDocumentNo: !payableForm.sourceDocumentNo.trim(),
  supplierCode: !payableForm.supplierCode.trim(),
  amount: !(Number(payableForm.amount) > 0),
}))
const canSubmitReceivable = computed(() => !Object.values(receivableInvalid.value).some(Boolean))
const canSubmitPayable = computed(() => !Object.values(payableInvalid.value).some(Boolean))

function openReceivableDialog() {
  receivableForm.sourceDocumentNo = ''
  receivableForm.customerCode = ''
  receivableForm.amount = '0'
  receivableShowErrors.value = false
  receivableOpen.value = true
}
function openPayableDialog() {
  payableForm.sourceDocumentNo = ''
  payableForm.supplierCode = ''
  payableForm.amount = '0'
  payableShowErrors.value = false
  payableOpen.value = true
}

async function submitReceivable() {
  receivableShowErrors.value = true
  if (!canSubmitReceivable.value) return
  try {
    await receivables.createReceivable({
      sourceDocumentNo: receivableForm.sourceDocumentNo.trim(),
      customerCode: receivableForm.customerCode.trim(),
      amount: Number(receivableForm.amount),
      currencyCode: 'CNY',
    })
    receivableOpen.value = false
    notifySuccess('应收已登记')
  } catch (error) {
    notifyOperationFailure(
      '登记应收失败',
      receivables.createReceivableError.value ?? error,
      '登记应收失败，请稍后重试。',
    )
  }
}

async function submitPayable() {
  payableShowErrors.value = true
  if (!canSubmitPayable.value) return
  try {
    await payables.createPayable({
      sourceDocumentNo: payableForm.sourceDocumentNo.trim(),
      supplierCode: payableForm.supplierCode.trim(),
      amount: Number(payableForm.amount),
      currencyCode: 'CNY',
    })
    payableOpen.value = false
    notifySuccess('应付已登记')
  } catch (error) {
    notifyOperationFailure(
      '登记应付失败',
      payables.createPayableError.value ?? error,
      '登记应付失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="AR/AP"
      :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]"
      :count="headerCount"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          @click="
            () => {
              receivables.refresh()
              payables.refresh()
            }
          "
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openReceivableDialog"
          ><PlusIcon aria-hidden="true" />登记应收</NvButton
        >
        <NvButton size="sm" type="button" @click="openPayableDialog"
          ><PlusIcon aria-hidden="true" />登记应付</NvButton
        >
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="settlementCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="receivables.filters.keyword"
          class="h-9 w-48"
          placeholder="应收单号 / 客户"
          aria-label="应收关键字"
        />
        <NvSelect v-model="receivableStatus">
          <NvSelectTrigger class="h-9 w-32" aria-label="应收状态"
            ><NvSelectValue placeholder="全部状态"
          /></NvSelectTrigger>
          <NvSelectContent
            ><NvSelectItem value="all">全部应收</NvSelectItem
            ><NvSelectItem value="open">未结</NvSelectItem
            ><NvSelectItem value="settled">已结清</NvSelectItem></NvSelectContent
          >
        </NvSelect>
      </template>
    </NvToolbar>
    <NvDataTable
      manual
      :page="receivablesPaged.page.value"
      :page-size="receivablesPaged.pageSize.value"
      :total-items="receivables.total.value"
      :columns="receivableColumns"
      :rows="receivables.items.value"
      :row-key="
        (r: BusinessConsoleErpReceivableItem) => r.receivableNo ?? r.sourceDocumentNo ?? '应收'
      "
      :loading="receivables.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="receivableState.emptyMessage"
      :error="receivableState.error"
      :error-message="receivableState.errorMessage"
      :awaiting-scope="receivableState.awaitingScope"
      :awaiting-scope-message="receivableState.awaitingScopeMessage"
      @retry="receivables.refresh"
      @update:page="receivablesPaged.page.value = $event"
      @update:page-size="(v) => (receivablesPaged.pageSize.value = String(v))"
    >
      <template #cell-customerCode="{ row }">
        <PartnerNameCell :code="row.customerCode" />
      </template>
      <template #cell-amount="{ row }"
        ><span class="tabular-nums">{{
          formatAmount(row.amount, row.currencyCode ?? 'CNY')
        }}</span></template
      >
      <template #cell-openAmount="{ row }"
        ><span class="tabular-nums">{{
          formatAmount(row.openAmount, row.currencyCode ?? 'CNY')
        }}</span></template
      >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="payables.filters.keyword"
          class="h-9 w-48"
          placeholder="应付单号 / 供应商"
          aria-label="应付关键字"
        />
        <NvSelect v-model="payableStatus">
          <NvSelectTrigger class="h-9 w-32" aria-label="应付状态"
            ><NvSelectValue placeholder="全部状态"
          /></NvSelectTrigger>
          <NvSelectContent
            ><NvSelectItem value="all">全部应付</NvSelectItem
            ><NvSelectItem value="open">未结</NvSelectItem
            ><NvSelectItem value="settled">已结清</NvSelectItem></NvSelectContent
          >
        </NvSelect>
      </template>
    </NvToolbar>
    <NvDataTable
      manual
      :page="payablesPaged.page.value"
      :page-size="payablesPaged.pageSize.value"
      :total-items="payables.total.value"
      :columns="payableColumns"
      :rows="payables.items.value"
      :row-key="(r: BusinessConsoleErpPayableItem) => r.payableNo ?? r.sourceDocumentNo ?? '应付'"
      :loading="payables.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="payableState.emptyMessage"
      :error="payableState.error"
      :error-message="payableState.errorMessage"
      :awaiting-scope="payableState.awaitingScope"
      :awaiting-scope-message="payableState.awaitingScopeMessage"
      @retry="payables.refresh"
      @update:page="payablesPaged.page.value = $event"
      @update:page-size="(v) => (payablesPaged.pageSize.value = String(v))"
    >
      <template #cell-supplierCode="{ row }">
        <PartnerNameCell :code="row.supplierCode" />
      </template>
      <template #cell-amount="{ row }"
        ><span class="tabular-nums">{{
          formatAmount(row.amount, row.currencyCode ?? 'CNY')
        }}</span></template
      >
      <template #cell-openAmount="{ row }"
        ><span class="tabular-nums">{{
          formatAmount(row.openAmount, row.currencyCode ?? 'CNY')
        }}</span></template
      >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="receivableOpen">
      <NvDialogContent>
        <NvDialogHeader
          ><NvDialogTitle>登记应收</NvDialogTitle
          ><NvDialogDescription class="sr-only"
            >登记客户应收款项。</NvDialogDescription
          ></NvDialogHeader
        >
        <form class="grid gap-4" @submit.prevent="submitReceivable">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="erp-ar-source">
                来源单据 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-ar-source"
                v-model="receivableForm.sourceDocumentNo"
                :options="receivableSourceOptions"
                title="选择来源单据"
                placeholder="选择销售订单或发货单"
                source-text="数据来自销售订单与发货单"
                empty-text="暂无可开票的销售订单或发货单"
                :loading="receivableSourcesPending"
                aria-label="来源单据"
                :class="
                  pickerInvalidClass(receivableShowErrors && receivableInvalid.sourceDocumentNo)
                "
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-ar-customer">
                客户 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-ar-customer"
                v-model="receivableForm.customerCode"
                :options="customerOptions"
                title="选择客户"
                placeholder="选择客户"
                source-text="数据来自基础数据业务伙伴（客户角色）"
                empty-text="暂无客户，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="客户"
                :class="pickerInvalidClass(receivableShowErrors && receivableInvalid.customerCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-ar-amount">
                金额（元） <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-ar-amount"
                v-model="receivableForm.amount"
                type="number"
                min="0"
                step="0.01"
                :data-invalid="receivableShowErrors && receivableInvalid.amount ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p
            v-if="receivableShowErrors && !canSubmitReceivable"
            class="text-sm text-destructive"
            role="alert"
          >
            请选择来源单据与客户，并给出正数金额。
          </p>
          <NvDialogFooter
            ><NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            ><NvButton type="submit" :disabled="receivables.createReceivablePending.value"
              ><Spinner
                v-if="receivables.createReceivablePending.value"
                aria-hidden="true"
              />登记应收</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="payableOpen">
      <NvDialogContent>
        <NvDialogHeader
          ><NvDialogTitle>登记应付</NvDialogTitle
          ><NvDialogDescription class="sr-only"
            >登记供应商应付款项。</NvDialogDescription
          ></NvDialogHeader
        >
        <form class="grid gap-4" @submit.prevent="submitPayable">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="erp-ap-source">
                来源单据 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-ap-source"
                v-model="payableForm.sourceDocumentNo"
                :options="payableSourceOptions"
                title="选择来源单据"
                placeholder="选择采购订单"
                source-text="数据来自采购订单"
                empty-text="暂无采购订单，请先在「采购 · 采购订单」下达"
                :loading="payableSourcesPending"
                aria-label="来源单据"
                :class="pickerInvalidClass(payableShowErrors && payableInvalid.sourceDocumentNo)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-ap-supplier">
                供应商 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-ap-supplier"
                v-model="payableForm.supplierCode"
                :options="supplierOptions"
                title="选择供应商"
                placeholder="选择供应商"
                source-text="数据来自基础数据业务伙伴（供应商角色）"
                empty-text="暂无供应商，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="供应商"
                :class="pickerInvalidClass(payableShowErrors && payableInvalid.supplierCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-ap-amount">
                金额（元） <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-ap-amount"
                v-model="payableForm.amount"
                type="number"
                min="0"
                step="0.01"
                :data-invalid="payableShowErrors && payableInvalid.amount ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p
            v-if="payableShowErrors && !canSubmitPayable"
            class="text-sm text-destructive"
            role="alert"
          >
            请选择来源单据与供应商，并给出正数金额。
          </p>
          <NvDialogFooter
            ><NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            ><NvButton type="submit" :disabled="payables.createPayablePending.value"
              ><Spinner
                v-if="payables.createPayablePending.value"
                aria-hidden="true"
              />登记应付</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
