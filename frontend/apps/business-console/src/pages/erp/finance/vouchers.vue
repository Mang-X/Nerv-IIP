<script setup lang="ts">
import { usePermission } from '@/composables/usePermission'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useQuery } from '@pinia/colada'
import {
  listBusinessConsoleErpGlAccountsQueryOptions,
  type BusinessConsoleErpJournalVoucherItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { unwrapData } from '@/composables/erp/erpDocumentList'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useErpJournalVouchers } from '@/composables/erp/useBusinessErp'
import { usePagedList } from '@/composables/shared/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { i18n } from '@/i18n'
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
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSpinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  pickerInvalidClass,
  UNAVAILABLE_TEXT,
  erpReadState,
  formatAmount,
  formatDate,
  stableRowKey,
} from '../shared'

const { locale } = i18n.global

const { can } = usePermission()

definePage({
  meta: {
    requiresAuth: true,
    title: '会计凭证',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

const vouchers = useErpJournalVouchers()
const { page, pageSize } = usePagedList(vouchers.filters, {
  resetOn: [() => vouchers.filters.keyword],
})

const columns: NvDataTableColumn<BusinessConsoleErpJournalVoucherItem>[] = [
  {
    key: 'voucherNo',
    header: '凭证号',
    cellClass: 'font-medium',
    accessor: (r) => r.voucherNo ?? '-',
  },
  {
    key: 'postingDate',
    header: '过账日期',
    width: 'w-32',
    accessor: (r) => formatDate(r.postingDate),
  },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'totalDebitAmount',
    header: '借方',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.totalDebitAmount ?? 0,
  },
  {
    key: 'totalCreditAmount',
    header: '贷方',
    align: 'end',
    width: 'w-32',
    accessor: (r) => r.totalCreditAmount ?? 0,
  },
]

const readState = computed(() =>
  erpReadState({
    noun: '会计凭证',
    unit: '张',
    ready: vouchers.ready.value,
    pending: vouchers.pending.value,
    error: vouchers.error.value,
    total: vouchers.total.value,
    filtered: Boolean(vouchers.filters.keyword || vouchers.filters.status),
    emptyHint: '还没有会计凭证。成本候选结转或手工过账后会在这里形成凭证。',
  }),
)

const debitAmount = computed(() =>
  vouchers.items.value.reduce((sum, v) => sum + (v.totalDebitAmount ?? 0), 0),
)
const creditAmount = computed(() =>
  vouchers.items.value.reduce((sum, v) => sum + (v.totalCreditAmount ?? 0), 0),
)
// 借贷是同一句话的两半：并排一条才能一眼看出是否平衡；不平衡时把差额标红提示。
const balanced = computed(() => debitAmount.value === creditAmount.value)
const voucherCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'debit',
    label: '借方合计',
    // 取不到凭证时不能报 ¥0.00 合计，更不能据此断言"借贷平衡"。
    value: readState.value.trustworthy ? formatAmount(debitAmount.value) : UNAVAILABLE_TEXT,
    meta: readState.value.trustworthy
      ? `当前列表 ${vouchers.items.value.length} 张凭证合计`
      : readState.value.emptyMessage,
  },
  {
    key: 'credit',
    label: '贷方合计',
    value: readState.value.trustworthy ? formatAmount(creditAmount.value) : UNAVAILABLE_TEXT,
    valueTone: !readState.value.trustworthy || balanced.value ? undefined : 'danger',
    meta: !readState.value.trustworthy
      ? readState.value.emptyMessage
      : balanced.value
        ? '与借方平衡'
        : `与借方相差 ${formatAmount(Math.abs(debitAmount.value - creditAmount.value))}`,
  },
])

type VoucherLine = NonNullable<BusinessConsoleErpJournalVoucherItem['lines']>[number] & {
  lineNo: number
}
const lineColumns: NvDataTableColumn<VoucherLine>[] = [
  { key: 'accountCode', header: '科目' },
  {
    key: 'debitAmount',
    header: '借方',
    align: 'end',
    accessor: (row) => formatAmount(row.debitAmount),
  },
  {
    key: 'creditAmount',
    header: '贷方',
    align: 'end',
    accessor: (row) => formatAmount(row.creditAmount),
  },
  { key: 'memo', header: '摘要' },
]
const selectedVoucher = shallowRef<BusinessConsoleErpJournalVoucherItem>()
const linesOpen = shallowRef(false)
const voucherLines = computed(() =>
  (selectedVoucher.value?.lines ?? []).map((line, index) => ({ ...line, lineNo: index + 1 })),
)

function openLines(voucher: BusinessConsoleErpJournalVoucherItem) {
  selectedVoucher.value = voucher
  linesOpen.value = true
}

let lineSequence = 0
function newEntry(side: 'debit' | 'credit') {
  return { id: ++lineSequence, side, accountCode: '', amount: '', memo: '' }
}

const open = shallowRef(false)
const businessContext = useBusinessContextStore()
const accounts = useQuery(() => ({
  ...listBusinessConsoleErpGlAccountsQueryOptions({
    query: {
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
    },
  }),
  enabled: open.value && Boolean(businessContext.organizationId && businessContext.environmentId),
}))
const accountOptions = computed(() => unwrapData(accounts.data.value)?.items ?? [])
const form = reactive({
  postingDate: '',
  lines: [newEntry('debit'), newEntry('credit')],
})
const showErrors = shallowRef(false)
const entryInvalid = (line: ReturnType<typeof newEntry>) =>
  !line.accountCode.trim() || !(Number(line.amount) > 0) || !line.memo.trim()
const postingLines = computed(() =>
  form.lines.map((line) => ({
    accountCode: line.accountCode.trim(),
    debitAmount: line.side === 'debit' ? Number(line.amount) : 0,
    creditAmount: line.side === 'credit' ? Number(line.amount) : 0,
    memo: line.memo.trim(),
  })),
)
const balanceInCents = computed(() =>
  postingLines.value.reduce(
    (sum, line) => sum + Math.round(line.debitAmount * 100) - Math.round(line.creditAmount * 100),
    0,
  ),
)
const canSubmit = computed(
  () =>
    Boolean(form.postingDate) &&
    form.lines.length >= 2 &&
    !form.lines.some(entryInvalid) &&
    balanceInCents.value === 0,
)

/** 本地当天，作为过账日期默认值（凭证绝大多数当天过账）。 */
function todayInputValue() {
  const now = new Date()
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10)
}

function openDialog() {
  form.postingDate = todayInputValue()
  form.lines = [newEntry('debit'), newEntry('credit')]
  showErrors.value = false
  open.value = true
}

async function submit() {
  if (!can(P.erpFinanceManage)) return
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await vouchers.postVoucher({
      postingDate: form.postingDate,
      lines: postingLines.value,
    })
    open.value = false
    notifySuccess('会计凭证已过账')
  } catch (error) {
    notifyOperationFailure(
      '过账凭证失败',
      vouchers.postVoucherError.value ?? error,
      '过账凭证失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="会计凭证"
      :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]"
      :count="readState.count"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="vouchers.pending.value"
          @click="vouchers.refresh"
          ><RefreshCwIcon aria-hidden="true" />刷新</NvButton
        >
        <NvButton v-if="can(P.erpFinanceManage)" size="sm" type="button" @click="openDialog"
          ><PlusIcon aria-hidden="true" />过账凭证</NvButton
        >
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="voucherCells" />

    <NvToolbar :show-search="false">
      <template #filters
        ><NvInput
          v-model="vouchers.filters.keyword"
          class="h-9 w-56"
          placeholder="凭证号"
          aria-label="凭证关键字"
      /></template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="vouchers.total.value"
      :columns="columns"
      :rows="vouchers.items.value"
      :row-key="stableRowKey"
      :loading="vouchers.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="vouchers.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-totalDebitAmount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.totalDebitAmount) }}</span></template
      >
      <template #cell-totalCreditAmount="{ row }"
        ><span class="tabular-nums">{{ formatAmount(row.totalCreditAmount) }}</span></template
      >
      <template #cell-voucherNo="{ row }">
        <NvButton
          variant="link"
          class="h-auto p-0"
          type="button"
          :aria-label="`查看分录 ${row.voucherNo ?? ''}`"
          @click="openLines(row)"
        >
          {{ row.voucherNo ?? '-' }}
        </NvButton>
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="linesOpen">
      <NvDialogContent class="sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>凭证分录 {{ selectedVoucher?.voucherNo }}</NvDialogTitle>
          <NvDialogDescription
            >过账日期 {{ formatDate(selectedVoucher?.postingDate) }}</NvDialogDescription
          >
        </NvDialogHeader>
        <NvDataTable
          :rows="voucherLines"
          row-key="lineNo"
          :columns="lineColumns"
          :searchable="false"
          :column-settings="false"
          empty-message="凭证没有返回分录。"
        />
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-if="can(P.erpFinanceManage)" v-model:open="open">
      <NvDialogContent class="max-h-[90vh] overflow-y-auto">
        <NvDialogHeader
          ><NvDialogTitle>过账会计凭证</NvDialogTitle
          ><NvDialogDescription class="sr-only">登记借贷分录。</NvDialogDescription></NvDialogHeader
        >
        <form class="grid gap-4" @submit.prevent="submit">
          <datalist id="voucher-account-catalog">
            <option
              v-for="account in accountOptions"
              :key="account.code"
              :value="account.code"
              :label="account.name"
            />
          </datalist>
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="erp-jv-date">
                过账日期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvDatePicker
                id="erp-jv-date"
                v-model="form.postingDate"
                :locale="locale"
                clearable
                :aria-invalid="showErrors && !form.postingDate"
                :class="pickerInvalidClass(showErrors && !form.postingDate)"
              />
            </NvField>
          </NvFieldGroup>
          <div
            v-for="(line, index) in form.lines"
            :key="line.id"
            class="grid gap-2 rounded-md border p-3"
          >
            <div class="flex items-center justify-between">
              <span>{{ index + 1 }} · {{ line.side === 'debit' ? '借方' : '贷方' }}</span>
              <NvButton type="button" size="sm" variant="ghost" @click="form.lines.splice(index, 1)"
                >删除分录</NvButton
              >
            </div>
            <NvField>
              <NvFieldLabel :for="'erp-jv-account-' + line.id">科目</NvFieldLabel>
              <NvInput
                :id="'erp-jv-account-' + line.id"
                v-model="line.accountCode"
                list="voucher-account-catalog"
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="'erp-jv-amount-' + line.id">金额</NvFieldLabel>
              <NvInput
                :id="'erp-jv-amount-' + line.id"
                v-model="line.amount"
                type="number"
                min="0.01"
                step="0.01"
              />
            </NvField>
            <NvField>
              <NvFieldLabel :for="'erp-jv-memo-' + line.id">摘要</NvFieldLabel>
              <NvInput :id="'erp-jv-memo-' + line.id" v-model="line.memo" />
            </NvField>
          </div>
          <div class="flex gap-2">
            <NvButton type="button" variant="outline" @click="form.lines.push(newEntry('debit'))"
              >新增借方</NvButton
            >
            <NvButton type="button" variant="outline" @click="form.lines.push(newEntry('credit'))"
              >新增贷方</NvButton
            >
          </div>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请填写过账日期及各行科目、摘要与正数金额，并确认借贷合计相等。
          </p>
          <NvDialogFooter
            ><NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            ><NvButton type="submit" :disabled="vouchers.postVoucherPending.value"
              ><NvSpinner
                v-if="vouchers.postVoucherPending.value"
                aria-hidden="true"
              />过账凭证</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
