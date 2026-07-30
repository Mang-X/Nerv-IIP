<script setup lang="ts">
import type { BusinessConsoleErpJournalVoucherItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpJournalVouchers } from '@/composables/useBusinessErp'
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
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { UNAVAILABLE_TEXT, erpReadState, formatAmount, formatDate } from '../shared'

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

const open = shallowRef(false)
const form = reactive({
  postingDate: '',
  debitAccount: '',
  creditAccount: '',
  amount: '0',
  memo: '',
})
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  postingDate: !form.postingDate,
  debitAccount: !form.debitAccount.trim(),
  creditAccount: !form.creditAccount.trim(),
  memo: !form.memo.trim(),
  amount: !(Number(form.amount) > 0),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

/** 本地当天，作为过账日期默认值（凭证绝大多数当天过账）。 */
function todayInputValue() {
  const now = new Date()
  return new Date(now.getTime() - now.getTimezoneOffset() * 60_000).toISOString().slice(0, 10)
}

function openDialog() {
  form.postingDate = todayInputValue()
  form.debitAccount = ''
  form.creditAccount = ''
  form.amount = '0'
  form.memo = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  const amount = Number(form.amount)
  try {
    await vouchers.postVoucher({
      postingDate: form.postingDate,
      lines: [
        {
          accountCode: form.debitAccount.trim(),
          debitAmount: amount,
          creditAmount: 0,
          memo: form.memo.trim(),
        },
        {
          accountCode: form.creditAccount.trim(),
          debitAmount: 0,
          creditAmount: amount,
          memo: form.memo.trim(),
        },
      ],
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
        <NvButton size="sm" type="button" @click="openDialog"
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
      :row-key="(r: BusinessConsoleErpJournalVoucherItem) => r.voucherNo ?? '凭证'"
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
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader
          ><NvDialogTitle>过账会计凭证</NvDialogTitle
          ><NvDialogDescription class="sr-only"
            >登记一借一贷分录。</NvDialogDescription
          ></NvDialogHeader
        >
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="erp-jv-date">
                过账日期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-jv-date"
                v-model="form.postingDate"
                type="date"
                :data-invalid="showErrors && invalid.postingDate ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-jv-amount">
                金额（元） <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-jv-amount"
                v-model="form.amount"
                type="number"
                min="0"
                step="0.01"
                :data-invalid="showErrors && invalid.amount ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-jv-debit">
                借方科目 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-jv-debit"
                v-model="form.debitAccount"
                :data-invalid="showErrors && invalid.debitAccount ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-jv-credit">
                贷方科目 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-jv-credit"
                v-model="form.creditAccount"
                :data-invalid="showErrors && invalid.creditAccount ? '' : undefined"
              />
            </NvField>
            <NvField class="sm:col-span-2">
              <NvFieldLabel for="erp-jv-memo">
                摘要 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-jv-memo"
                v-model="form.memo"
                :data-invalid="showErrors && invalid.memo ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请填写过账日期、借贷科目、摘要，并给出正数金额。
          </p>
          <NvDialogFooter
            ><NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            ><NvButton type="submit" :disabled="vouchers.postVoucherPending.value"
              ><Spinner
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
