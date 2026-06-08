<script setup lang="ts">
import type { BusinessConsoleErpReceivableItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useErpFinance } from '@/composables/useBusinessErp'
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
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

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

const errorMessage = computed(() => formatError(summaryError.value ?? receivablesError.value))
const pending = computed(() => summaryPending.value || receivablesPending.value)

function refreshAll() {
  void refreshSummary()
  void refreshReceivables()
}

type ReceivableRow = BusinessConsoleErpReceivableItem
const columns: DataTableColumn<ReceivableRow>[] = [
  { key: 'receivableNo', header: '应收单号', cellClass: 'font-medium', accessor: (r) => r.receivableNo ?? '无' },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '无' },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '无' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'openAmount', header: '未结', align: 'end', width: 'w-32', accessor: (r) => r.openAmount ?? 0 },
  { key: 'status', header: '状态', width: 'w-28' },
]

function rowKey(row: ReceivableRow) {
  return row.receivableNo ?? row.sourceDocumentNo ?? '应收'
}
function formatAmount(value?: number | null, currency = 'CNY') {
  return new Intl.NumberFormat('zh-CN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value ?? 0)
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="财务" :breadcrumbs="[{ label: '经营管理' }]" :count="`${receivablesTotal} 笔应收`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="pending" @click="refreshAll">
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

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.keyword" class="h-9 w-44" placeholder="应收单号/客户（可选）" aria-label="关键字" />
        <Input v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="应收状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="receivables"
      :row-key="rowKey"
      :loading="receivablesPending"
      empty-message="暂无应收账款。销售发货过账后会在这里生成应收。"
    >
      <template #cell-amount="{ row }"><span class="tabular-nums">{{ formatAmount(row.amount, row.currencyCode ?? 'CNY') }}</span></template>
      <template #cell-openAmount="{ row }"><span class="tabular-nums">{{ formatAmount(row.openAmount, row.currencyCode ?? 'CNY') }}</span></template>
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="receivablesTotal" />
  </BusinessLayout>
</template>
