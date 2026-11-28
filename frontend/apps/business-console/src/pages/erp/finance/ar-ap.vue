<script setup lang="ts">
import { useMutation } from '@pinia/colada'
import {
  approveBusinessConsoleErpPaymentExecutionMutationOptions,
  executeBusinessConsoleErpPaymentExecutionMutationOptions,
  matchBusinessConsoleErpCashReceiptMutationOptions,
  registerBusinessConsoleErpCashReceiptMutationOptions,
} from '@nerv-iip/api-client'
import { makeIdempotencyKey, unwrapData } from '@/composables/erp/erpDocumentList'
import { useBusinessContextStore } from '@/stores/businessContext'
import { usePermission } from '@/composables/usePermission'
import { useRoute, useRouter } from 'vue-router'
import { queryText } from '@/components/erp/costRecovery'
import type {
  BusinessConsoleErpPayableItem,
  BusinessConsoleErpReceivableItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import {
  useErpFinanceSummary,
  useErpPayables,
  useErpReceivables,
} from '@/composables/erp/useBusinessErp'
import {
  useErpPartnerCatalog,
  useErpPayableSourceCatalog,
  useErpReceivableSourceCatalog,
} from '@/composables/erp/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/master-data/useBusinessPartnerNames'
import { usePagedList } from '@/composables/shared/usePagedList'
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
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { UNAVAILABLE_TEXT, erpReadState, formatAmount, pickerInvalidClass } from '../shared'

const { can } = usePermission()
const canManage = computed(() => can('business.erp.finance.manage'))

definePage({
  meta: { requiresAuth: true, title: 'AR/AP', requiredPermissions: ['business.erp.finance.read'] },
})

const receivables = useErpReceivables()
const route = useRoute()
const router = useRouter()
watch(
  () => route.query.keyword,
  () => {
    receivables.filters.keyword = queryText(route.query, 'keyword') || undefined
  },
  { immediate: true },
)
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
  { key: 'settlement', header: '操作' },
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
  { key: 'settlement', header: '操作' },
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

function clearReceivableFilters() {
  receivables.filters.keyword = ''
  receivables.filters.status = undefined
  if (route.query.keyword !== undefined)
    void router.replace({ query: { ...route.query, keyword: undefined } })
}

/** 页头两路读数各自独立：一路挂了只说这一路取不到，不把另一路也抹掉。 */
const headerCount = computed(() => {
  if (receivableState.value.count === undefined && payableState.value.count === undefined) {
    return undefined
  }
  return `${receivableState.value.count ?? '应收读取中'} / ${payableState.value.count ?? '应付读取中'}`
})

/**
 * 顶部两张卡用**全库口径**的财务摘要（#1418 B5，owner 亲验点名）。
 *
 * 曾踩坑：这里原本对「当前页 10 行」求和冒充「应收/应付未结」——翻页数字乱跳，
 * 登记一笔新应付反而让卡上的「应付未结」下降（新行把旧行挤出第一页）。
 * KPI 卡位只许放全局读数；页内合计不是余额，不许再占卡位。
 */
const { ready: summaryReady, summary, summaryError, refreshSummary } = useErpFinanceSummary()
const summaryTrustworthy = computed(
  () => summaryReady.value && summaryError.value == null && summary.value !== undefined,
)
const summaryUnavailableNote = computed(() => {
  if (!summaryReady.value) return '尚未选择业务范围，还没有发起查询。'
  if (summaryError.value != null) return '财务摘要读取失败，当前无法判断未结余额。'
  return '正在读取财务摘要…'
})
const settlementCells = computed<NvMetricStripCell[]>(() =>
  [
    { key: 'receivable', label: '应收未结', balances: summary.value?.receivablesByCurrency },
    { key: 'payable', label: '应付未结', balances: summary.value?.payablesByCurrency },
  ].flatMap(({ key, label, balances }) =>
    summaryTrustworthy.value && balances?.length
      ? balances.map((balance) => ({
          key: `${key}-${balance.currencyCode}`,
          label: `${label} (${balance.currencyCode})`,
          value: formatAmount(balance.openAmount, balance.currencyCode),
        }))
      : [
          {
            key,
            label,
            value: UNAVAILABLE_TEXT,
            meta: summaryTrustworthy.value ? '无未结余额记录' : summaryUnavailableNote.value,
          },
        ],
  ),
)

const businessContext = useBusinessContextStore()
const registerCashReceipt = useMutation(registerBusinessConsoleErpCashReceiptMutationOptions())
const matchCashReceipt = useMutation(matchBusinessConsoleErpCashReceiptMutationOptions())
const approvePayment = useMutation(approveBusinessConsoleErpPaymentExecutionMutationOptions())
const executePayment = useMutation(executeBusinessConsoleErpPaymentExecutionMutationOptions())

const settlementOpen = shallowRef(false)
const settlement = reactive({
  kind: 'receivable' as 'receivable' | 'payable',
  sourceNo: '',
  maxAmount: 0,
  currencyCode: '',
  amount: '',
  date: '',
  cashAccountCode: '',
  executionNo: '',
  idempotencyKey: '',
})
const settlementPending = computed(
  () =>
    registerCashReceipt.isLoading.value ||
    matchCashReceipt.isLoading.value ||
    approvePayment.isLoading.value ||
    executePayment.isLoading.value,
)
const settlementLabel = computed(() =>
  settlement.kind === 'receivable'
    ? settlement.executionNo
      ? '核销收款'
      : '登记收款'
    : settlement.executionNo
      ? '执行付款'
      : '批准付款',
)

function openSettlement(
  kind: typeof settlement.kind,
  sourceNo: string,
  maxAmount: number,
  currencyCode: string,
) {
  Object.assign(settlement, {
    kind,
    sourceNo,
    maxAmount,
    currencyCode,
    amount: String(maxAmount),
    date: '',
    cashAccountCode: '',
    executionNo: '',
    idempotencyKey: makeIdempotencyKey(),
  })
  settlementOpen.value = true
}

async function submitSettlement() {
  if (!canManage.value || settlementPending.value) return
  const label = settlementLabel.value
  const scope = {
    organizationId: businessContext.organizationId,
    environmentId: businessContext.environmentId,
  }
  try {
    if (!settlement.executionNo) {
      const amount = Number(settlement.amount)
      if (
        !(amount > 0 && amount <= settlement.maxAmount) ||
        !settlement.date ||
        !settlement.cashAccountCode.trim()
      )
        return
      const body = {
        ...scope,
        amount,
        cashAccountCode: settlement.cashAccountCode.trim(),
        idempotencyKey: settlement.idempotencyKey,
      }
      const result =
        settlement.kind === 'receivable'
          ? await registerCashReceipt.mutateAsync({
              body: { ...body, receivableNo: settlement.sourceNo, collectionDate: settlement.date },
            })
          : await approvePayment.mutateAsync({
              body: { ...body, payableNo: settlement.sourceNo, paymentDate: settlement.date },
            })
      settlement.executionNo = unwrapData(result) ?? ''
      if (!settlement.executionNo) throw new Error(`${label}未返回单号`)
      notifySuccess(`${label}成功，单号 ${settlement.executionNo}`)
      return
    }
    if (settlement.kind === 'receivable') {
      await matchCashReceipt.mutateAsync({
        path: { cashReceiptNo: settlement.executionNo },
        body: scope,
      })
      void receivables.refresh()
    } else {
      await executePayment.mutateAsync({
        path: { paymentExecutionNo: settlement.executionNo },
        body: scope,
      })
      void payables.refresh()
    }
    void refreshSummary()
    settlementOpen.value = false
    notifySuccess(`${label}成功`)
  } catch (error) {
    notifyOperationFailure(`${label}失败`, error, `${label}失败，请稍后重试。`)
  }
}

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
  if (!canManage.value) return
  receivableForm.sourceDocumentNo = ''
  receivableForm.customerCode = ''
  receivableForm.amount = '0'
  receivableShowErrors.value = false
  receivableOpen.value = true
}
function openPayableDialog() {
  if (!canManage.value) return
  payableForm.sourceDocumentNo = ''
  payableForm.supplierCode = ''
  payableForm.amount = '0'
  payableShowErrors.value = false
  payableOpen.value = true
}

async function submitReceivable() {
  if (!canManage.value) return
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
    // 顶卡是全库口径的摘要，登记成功后必须跟着刷新，否则卡片数字停在旧余额上。
    void refreshSummary()
  } catch (error) {
    notifyOperationFailure(
      '登记应收失败',
      receivables.createReceivableError.value ?? error,
      '登记应收失败，请稍后重试。',
    )
  }
}

async function submitPayable() {
  if (!canManage.value) return
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
    void refreshSummary()
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
              refreshSummary()
            }
          "
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton v-if="canManage" size="sm" type="button" @click="openReceivableDialog"
          ><PlusIcon aria-hidden="true" />登记应收</NvButton
        >
        <NvButton v-if="canManage" size="sm" type="button" @click="openPayableDialog"
          ><PlusIcon aria-hidden="true" />登记应付</NvButton
        >
      </template>
    </NvPageHeader>

    <div v-if="summaryError" role="alert" class="flex items-center gap-3">
      <p class="text-sm text-destructive">{{ summaryUnavailableNote }}</p>
      <NvButton size="sm" variant="outline" @click="refreshSummary">重试摘要</NvButton>
    </div>
    <NvMetricStrip v-else :cells="settlementCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="receivables.filters.keyword"
          class="h-9 w-48"
          placeholder="应收单号 / 来源单据 / 客户"
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
      :rows="receivableState.error ? [] : receivables.items.value"
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
      <template v-if="receivableState.kind === 'filtered'" #empty>
        <p>{{ receivableState.emptyMessage }}</p>
        <NvButton size="sm" variant="outline" @click="clearReceivableFilters"
          >清空应收筛选</NvButton
        >
      </template>
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
      <template #cell-settlement="{ row }">
        <NvButton
          v-if="canManage && row.receivableNo && row.currencyCode && (row.openAmount ?? 0) > 0"
          size="sm"
          variant="outline"
          @click="openSettlement('receivable', row.receivableNo, row.openAmount!, row.currencyCode)"
        >
          登记收款
        </NvButton>
      </template>
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
      :rows="payableState.error ? [] : payables.items.value"
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
      <template #cell-settlement="{ row }">
        <NvButton
          v-if="canManage && row.payableNo && row.currencyCode && (row.openAmount ?? 0) > 0"
          size="sm"
          variant="outline"
          @click="openSettlement('payable', row.payableNo, row.openAmount!, row.currencyCode)"
        >
          执行付款
        </NvButton>
      </template>
    </NvDataTable>

    <NvDialog v-if="canManage" v-model:open="receivableOpen">
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
              ><NvSpinner
                v-if="receivables.createReceivablePending.value"
                aria-hidden="true"
              />登记应收</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-if="canManage" v-model:open="payableOpen">
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
              ><NvSpinner
                v-if="payables.createPayablePending.value"
                aria-hidden="true"
              />登记应付</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>
    <NvDialog v-if="canManage" v-model:open="settlementOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>{{ settlementLabel }}</NvDialogTitle>
          <NvDialogDescription
            >{{ settlement.sourceNo }} · {{ settlement.currencyCode }}</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitSettlement">
          <template v-if="!settlement.executionNo">
            <NvField>
              <NvFieldLabel for="settlement-amount">金额</NvFieldLabel>
              <NvInput
                id="settlement-amount"
                v-model="settlement.amount"
                type="number"
                min="0.01"
                :max="settlement.maxAmount"
                step="0.01"
                required
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="settlement-date">业务日期</NvFieldLabel>
              <NvInput id="settlement-date" v-model="settlement.date" type="date" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="settlement-account">资金科目</NvFieldLabel>
              <NvInput id="settlement-account" v-model="settlement.cashAccountCode" required />
            </NvField>
          </template>
          <p v-else>
            单号 {{ settlement.executionNo }} ·
            {{ formatAmount(Number(settlement.amount), settlement.currencyCode) }}
          </p>
          <NvDialogFooter>
            <NvButton type="submit" :disabled="settlementPending">{{ settlementLabel }}</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
