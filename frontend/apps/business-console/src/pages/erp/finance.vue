<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useErpFinanceSummary } from '@/composables/useBusinessErp'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { ButtonPro, DataTablePro, PageHeader, SectionCard, SectionCards } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import { formatAmount, formatError } from './shared'

definePage({ meta: { requiresAuth: true, title: '财务摘要', requiredPermissions: ['business.erp.finance.read'] } })

const { summary, summaryError, summaryPending, refreshSummary } = useErpFinanceSummary()

const rows = computed(() => [
  { item: '应收未结', amount: summary.value?.openReceivableAmount ?? 0, scope: '客户应收' },
  { item: '应付未结', amount: summary.value?.openPayableAmount ?? 0, scope: '供应商应付' },
  { item: '待入账成本', amount: summary.value?.costCandidateAmount ?? 0, scope: '成本候选' },
  { item: '已过账凭证', amount: summary.value?.postedVoucherCount ?? 0, scope: '凭证数量' },
])

const columns: DataTableProColumn<(typeof rows.value)[number]>[] = [
  { key: 'item', header: '指标', cellClass: 'font-medium' },
  { key: 'scope', header: '范围' },
  { key: 'amount', header: '数值', align: 'end', width: 'w-40' },
]
</script>

<template>
  <BusinessLayout>
    <PageHeader title="财务摘要" :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]" count="当前最小财务读面">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="summaryPending" @click="refreshSummary">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <p v-if="formatError(summaryError)" class="text-sm text-destructive" role="alert">{{ formatError(summaryError) }}</p>

    <SectionCards :columns="4">
      <SectionCard description="应收未结" :value="formatAmount(summary?.openReceivableAmount)" hint="客户待收款项" />
      <SectionCard description="应付未结" :value="formatAmount(summary?.openPayableAmount)" hint="供应商待付款项" />
      <SectionCard description="待入账成本" :value="formatAmount(summary?.costCandidateAmount)" hint="待结转成本候选" />
      <SectionCard description="已过账凭证" :value="summary?.postedVoucherCount ?? 0" hint="最小子分类账凭证数" />
    </SectionCards>

    <DataTablePro
      :columns="columns"
      :rows="rows"
      :row-key="(r) => r.item"
      :loading="summaryPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无财务摘要。"
    >
      <template #cell-amount="{ row }">
        <span class="tabular-nums">{{ row.item === '已过账凭证' ? row.amount : formatAmount(row.amount) }}</span>
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
