<script setup lang="ts">
import type { BusinessConsoleErpJournalVoucherItem, BusinessConsoleErpReceivableItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useErpFinance, useErpJournalVouchers } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '财务' } })

const {
  summary,
  summaryError,
  summaryPending,
  refreshSummary,
  filters,
  receivables,
  receivablesTotal,
  receivablesError,
  receivablesPending,
  refreshReceivables,
} = useErpFinance()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status, () => filters.keyword] })

const vouchers = useErpJournalVouchers()
const vouchersPaged = usePagedList(vouchers.filters, { resetOn: [() => vouchers.filters.status, () => vouchers.filters.keyword] })

const activeTab = shallowRef<'receivables' | 'vouchers'>('receivables')

const receivablesErrorMessage = computed(() => formatError(summaryError.value ?? receivablesError.value))
const vouchersErrorMessage = computed(() => formatError(vouchers.error.value))
const pending = computed(() => summaryPending.value || receivablesPending.value)

function refreshActive() {
  if (activeTab.value === 'receivables') {
    void refreshSummary()
    void refreshReceivables()
  } else {
    void vouchers.refresh()
  }
}

const columns: DataTableColumn<BusinessConsoleErpReceivableItem>[] = [
  { key: 'receivableNo', header: '应收单号', cellClass: 'font-medium', accessor: (r) => r.receivableNo ?? '无' },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'openAmount', header: '未结', align: 'end', width: 'w-32', accessor: (r) => r.openAmount ?? 0 },
  { key: 'status', header: '状态', width: 'w-28' },
]
const voucherColumns: DataTableColumn<BusinessConsoleErpJournalVoucherItem>[] = [
  { key: 'voucherNo', header: '凭证号', cellClass: 'font-medium', accessor: (r) => r.voucherNo ?? '无' },
  { key: 'postingDate', header: '过账日期', width: 'w-32', accessor: (r) => formatDate(r.postingDate) },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'totalDebitAmount', header: '借方', align: 'end', width: 'w-32', accessor: (r) => r.totalDebitAmount ?? 0 },
  { key: 'totalCreditAmount', header: '贷方', align: 'end', width: 'w-32', accessor: (r) => r.totalCreditAmount ?? 0 },
]

function receivableRowKey(row: BusinessConsoleErpReceivableItem) {
  return row.receivableNo ?? row.sourceDocumentNo ?? '应收'
}
function voucherRowKey(row: BusinessConsoleErpJournalVoucherItem) {
  return row.voucherNo ?? '凭证'
}
function formatAmount(value?: number | null, currency = 'CNY') {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value ?? 0)
}
function formatDate(value?: string) {
  if (!value) return '无'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '无' : d.toLocaleDateString('zh-CN')
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="财务" :breadcrumbs="[{ label: '经营管理' }]" :count="`${receivablesTotal} 笔应收`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="pending" @click="refreshActive">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="应付未结" :value="formatAmount(summary?.openPayableAmount)" hint="待付供应商" />
      <SectionCard description="应收未结" :value="formatAmount(summary?.openReceivableAmount)" hint="待收客户" />
      <SectionCard description="成本候选" :value="formatAmount(summary?.costCandidateAmount)" hint="待入账成本" />
      <SectionCard description="已过账凭证" :value="summary?.postedVoucherCount ?? 0" hint="累计凭证数" />
    </SectionCards>

    <Tabs v-model="activeTab">
      <TabsList>
        <TabsTrigger value="receivables">应收账款</TabsTrigger>
        <TabsTrigger value="vouchers">会计凭证</TabsTrigger>
      </TabsList>

      <TabsContent value="receivables" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="filters.keyword" class="h-9 w-44" placeholder="应收单号/客户（可选）" aria-label="应收关键字" />
            <Input v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="应收状态" />
          </template>
        </Toolbar>
        <p v-if="receivablesErrorMessage" class="text-sm text-destructive" role="alert">{{ receivablesErrorMessage }}</p>
        <DataTable
          :columns="columns"
          :rows="receivables"
          :row-key="receivableRowKey"
          :loading="receivablesPending"
          empty-message="暂无应收账款。销售发货过账后会在这里生成应收。"
        >
          <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-openAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.openAmount, row.currencyCode ?? 'CNY') }}</span></template>
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="receivablesTotal" />
      </TabsContent>

      <TabsContent value="vouchers" class="grid gap-4">
        <Toolbar :show-search="false">
          <template #filters>
            <Input v-model="vouchers.filters.keyword" class="h-9 w-44" placeholder="凭证号（可选）" aria-label="凭证关键字" />
            <Input v-model="vouchers.filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="凭证状态" />
          </template>
        </Toolbar>
        <p v-if="vouchersErrorMessage" class="text-sm text-destructive" role="alert">{{ vouchersErrorMessage }}</p>
        <DataTable
          :columns="voucherColumns"
          :rows="vouchers.items.value"
          :row-key="voucherRowKey"
          :loading="vouchers.pending.value"
          empty-message="暂无会计凭证。成本/收入过账后会在这里生成凭证。"
        >
          <template #cell-totalDebitAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalDebitAmount) }}</span></template>
          <template #cell-totalCreditAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.totalCreditAmount) }}</span></template>
          <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
        </DataTable>
        <DataTablePagination v-model:page="vouchersPaged.page.value" v-model:page-size="vouchersPaged.pageSize.value" :total-items="vouchers.total.value" />
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
