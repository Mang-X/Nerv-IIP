<script setup lang="ts">
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpFinanceSummary } from '@/composables/useBusinessErp'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvDataTable, NvMetricStrip, NvPageHeader } from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { formatAmount, formatError } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '财务摘要',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

const { summary, summaryError, summaryPending, refreshSummary } = useErpFinanceSummary()

const rows = computed(() => [
  { item: '应收未结', amount: summary.value?.openReceivableAmount ?? 0, scope: '客户应收' },
  { item: '应付未结', amount: summary.value?.openPayableAmount ?? 0, scope: '供应商应付' },
  { item: '待入账成本', amount: summary.value?.costCandidateAmount ?? 0, scope: '成本候选' },
  { item: '已过账凭证', amount: summary.value?.postedVoucherCount ?? 0, scope: '凭证数量' },
])

const summaryCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'receivable',
    label: '应收未结',
    value: formatAmount(summary.value?.openReceivableAmount),
  },
  { key: 'payable', label: '应付未结', value: formatAmount(summary.value?.openPayableAmount) },
  { key: 'cost', label: '待入账成本', value: formatAmount(summary.value?.costCandidateAmount) },
  {
    key: 'vouchers',
    label: '已过账凭证',
    value: summary.value?.postedVoucherCount ?? 0,
    unit: '张',
    meta: '已记账、可用于对账的凭证',
  },
])

const columns: NvDataTableColumn<(typeof rows.value)[number]>[] = [
  { key: 'item', header: '指标', cellClass: 'font-medium' },
  { key: 'scope', header: '范围' },
  { key: 'amount', header: '数值', align: 'end', width: 'w-40' },
]
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="财务摘要"
      :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]"
      count="应收应付概览"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="summaryPending"
          @click="refreshSummary"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="formatError(summaryError)" class="text-sm text-destructive" role="alert">
      {{ formatError(summaryError) }}
    </p>

    <NvMetricStrip :cells="summaryCells" />

    <!-- 固定四行的摘要表，翻页器只会是一条永远停在第 1 页的空控件。 -->
    <NvDataTable
      :columns="columns"
      :rows="rows"
      :row-key="(r) => r.item"
      :loading="summaryPending"
      :searchable="false"
      :column-settings="false"
      :pagination="false"
      empty-message="暂无财务摘要。"
    >
      <template #cell-amount="{ row }">
        <span class="tabular-nums">{{
          row.item === '已过账凭证' ? row.amount : formatAmount(row.amount)
        }}</span>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
