<script setup lang="ts">
import type { BusinessConsoleErpJournalVoucherItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
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
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { formatAmount, formatDate, formatError } from '../shared'

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

const debitAmount = computed(() =>
  vouchers.items.value.reduce((sum, v) => sum + (v.totalDebitAmount ?? 0), 0),
)
const creditAmount = computed(() =>
  vouchers.items.value.reduce((sum, v) => sum + (v.totalCreditAmount ?? 0), 0),
)

const open = shallowRef(false)
const form = reactive({
  postingDate: '',
  debitAccount: '',
  creditAccount: '',
  amount: '0',
  memo: '',
})
const formError = shallowRef('')

function openDialog() {
  form.postingDate = ''
  form.debitAccount = ''
  form.creditAccount = ''
  form.amount = '0'
  form.memo = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  const amount = Number(form.amount)
  if (
    !form.postingDate ||
    !form.debitAccount.trim() ||
    !form.creditAccount.trim() ||
    !form.memo.trim() ||
    !(amount > 0)
  ) {
    formError.value = '请填写过账日期、借贷科目、摘要和正数金额。'
    return
  }
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
    toast.success('会计凭证已过账')
  } catch {
    formError.value = formatError(vouchers.postVoucherError.value) || '过账失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="会计凭证"
      :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]"
      :count="`${vouchers.total.value} 张凭证`"
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

    <NvSectionCards :columns="2">
      <NvSectionCard
        description="借方合计"
        :value="formatAmount(debitAmount)"
        hint="本页凭证借方金额"
      />
      <NvSectionCard
        description="贷方合计"
        :value="formatAmount(creditAmount)"
        hint="本页凭证贷方金额"
      />
    </NvSectionCards>

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
      empty-message="暂无会计凭证。"
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
          ><NvDialogDescription
            >登记一借一贷分录，后端校验借贷平衡。</NvDialogDescription
          ></NvDialogHeader
        >
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField
              ><NvFieldLabel for="erp-jv-date">过账日期</NvFieldLabel
              ><NvInput id="erp-jv-date" v-model="form.postingDate" type="date"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-jv-amount">金额（元）</NvFieldLabel
              ><NvInput id="erp-jv-amount" v-model="form.amount" type="number" min="0" step="0.01"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-jv-debit">借方科目</NvFieldLabel
              ><NvInput id="erp-jv-debit" v-model="form.debitAccount"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-jv-credit">贷方科目</NvFieldLabel
              ><NvInput id="erp-jv-credit" v-model="form.creditAccount"
            /></NvField>
            <NvField class="sm:col-span-2"
              ><NvFieldLabel for="erp-jv-memo">摘要</NvFieldLabel
              ><NvInput id="erp-jv-memo" v-model="form.memo"
            /></NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter
            ><NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            ><NvButton type="submit" :disabled="vouchers.postVoucherPending.value"
              ><Spinner v-if="vouchers.postVoucherPending.value" />过账</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
