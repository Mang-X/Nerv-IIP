<script setup lang="ts">
import type { BusinessConsoleErpCostCandidateItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpCostCandidates } from '@/composables/useBusinessErp'
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
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { formatAmount, formatError } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '成本候选',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

const costs = useErpCostCandidates()
const { page, pageSize } = usePagedList(costs.filters, { resetOn: [() => costs.filters.keyword] })

const columns: NvDataTableColumn<BusinessConsoleErpCostCandidateItem>[] = [
  {
    key: 'candidateNo',
    header: '候选编号',
    cellClass: 'font-medium',
    accessor: (r) => r.candidateNo ?? '-',
  },
  { key: 'sourceType', header: '来源类型', accessor: (r) => sourceTypeLabel(r.sourceType) },
  { key: 'sourceDocumentNo', header: '来源单据', accessor: (r) => r.sourceDocumentNo ?? '-' },
  { key: 'amount', header: '金额', align: 'end', width: 'w-32', accessor: (r) => r.amount ?? 0 },
  { key: 'status', header: '状态', width: 'w-24' },
]

const SOURCE_TYPE_LABELS: Record<string, string> = {
  production: '生产成本',
  procurement: '采购成本',
  maintenance: '维护成本',
  logistics: '物流成本',
}
function sourceTypeLabel(value?: string | null) {
  return SOURCE_TYPE_LABELS[(value ?? '').toLowerCase()] ?? value ?? '-'
}

const amount = computed(() => costs.items.value.reduce((sum, c) => sum + (c.amount ?? 0), 0))
const pendingCount = computed(
  () => costs.items.value.filter((c) => (c.status ?? '').toLowerCase() === 'pending').length,
)

const open = shallowRef(false)
const form = reactive({ sourceType: 'production', sourceDocumentNo: '', amount: '0' })
const formError = shallowRef('')

function openDialog() {
  form.sourceType = 'production'
  form.sourceDocumentNo = ''
  form.amount = '0'
  formError.value = ''
  open.value = true
}

async function submit() {
  const value = Number(form.amount)
  if (!form.sourceDocumentNo.trim() || !(value > 0)) {
    formError.value = '请填写来源单据和正数金额。'
    return
  }
  try {
    await costs.createCostCandidate({
      sourceType: form.sourceType,
      sourceDocumentNo: form.sourceDocumentNo.trim(),
      amount: value,
      currencyCode: 'CNY',
    })
    open.value = false
    toast.success('成本候选已登记')
  } catch {
    formError.value = formatError(costs.createCostCandidateError.value) || '登记失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="成本候选"
      :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]"
      :count="`${costs.total.value} 条候选`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="costs.pending.value"
          @click="costs.refresh"
          ><RefreshCwIcon aria-hidden="true" />刷新</NvButton
        >
        <NvButton size="sm" type="button" @click="openDialog"
          ><PlusIcon aria-hidden="true" />登记成本</NvButton
        >
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="待入账候选" :value="pendingCount" hint="等待凭证或成本结转" />
      <NvSectionCard description="本页候选金额" :value="formatAmount(amount)" hint="按当前页汇总" />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters
        ><NvInput
          v-model="costs.filters.keyword"
          class="h-9 w-64"
          placeholder="候选编号 / 来源单据"
          aria-label="成本候选关键字"
      /></template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="costs.total.value"
      :columns="columns"
      :rows="costs.items.value"
      :row-key="
        (r: BusinessConsoleErpCostCandidateItem) =>
          r.candidateNo ?? r.sourceDocumentNo ?? '成本候选'
      "
      :loading="costs.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无成本候选。生产、采购或维护成本归集后在这里入账。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-amount="{ row }"
        ><span class="tabular-nums">{{
          formatAmount(row.amount, row.currencyCode ?? 'CNY')
        }}</span></template
      >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader
          ><NvDialogTitle>登记成本候选</NvDialogTitle
          ><NvDialogDescription
            >归集待入账成本；完整成本核算与月结仍不在当前范围。</NvDialogDescription
          ></NvDialogHeader
        >
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="erp-cc-type">来源类型</NvFieldLabel>
              <NvSelect v-model="form.sourceType">
                <NvSelectTrigger id="erp-cc-type" aria-label="来源类型"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="production">生产成本</NvSelectItem>
                  <NvSelectItem value="procurement">采购成本</NvSelectItem>
                  <NvSelectItem value="maintenance">维护成本</NvSelectItem>
                  <NvSelectItem value="logistics">物流成本</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField
              ><NvFieldLabel for="erp-cc-source">来源单据</NvFieldLabel
              ><NvInput id="erp-cc-source" v-model="form.sourceDocumentNo"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-cc-amount">金额（元）</NvFieldLabel
              ><NvInput id="erp-cc-amount" v-model="form.amount" type="number" min="0" step="0.01"
            /></NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter
            ><NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            ><NvButton type="submit" :disabled="costs.createCostCandidatePending.value"
              ><Spinner v-if="costs.createCostCandidatePending.value" />登记</NvButton
            ></NvDialogFooter
          >
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
