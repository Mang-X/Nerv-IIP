<script setup lang="ts">
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpFinanceSummary } from '@/composables/useBusinessErp'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvCard, NvDataTable, NvMetricStrip, NvPageHeader } from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { UNAVAILABLE_TEXT, formatAmount, formatError } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '财务摘要',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

const { ready, summary, summaryError, summaryPending, refreshSummary } = useErpFinanceSummary()

/**
 * 财务读数是否可信。
 *
 * 曾踩坑：这页在摘要接口失败 / 尚未返回时把应收、应付、待入账成本全部渲染成 **¥0.00**，
 * 相当于向经营层报「零余额」——性质与「谎称现场无阻塞」一样。现在只要不是真的取到了数据，
 * 金额与张数一律显 `—`，并在页头与表体说清「取不到，无法判断」。
 */
const trustworthy = computed(
  () => ready.value && summaryError.value == null && summary.value !== undefined,
)

/** 缺值传 `undefined` 给 formatAmount，由它统一输出 `—`；绝不用 0 顶。 */
function amountOf(value: number | null | undefined) {
  return trustworthy.value ? value : undefined
}

const rows = computed(() => [
  { item: '应收未结', amount: amountOf(summary.value?.openReceivableAmount), scope: '客户应收' },
  { item: '应付未结', amount: amountOf(summary.value?.openPayableAmount), scope: '供应商应付' },
  { item: '待入账成本', amount: amountOf(summary.value?.costCandidateAmount), scope: '成本候选' },
  {
    item: '已过账凭证',
    amount: amountOf(summary.value?.postedVoucherCount),
    scope: '凭证数量',
  },
])

/** 页头与摘要行的状态说明：只说「为什么没有数字」，不说任何"暂无 / 正常"式结论。 */
const summaryStateNote = computed(() => {
  if (!ready.value) return '尚未选择业务范围，还没有发起查询。'
  if (summaryError.value != null) return '财务摘要读取失败，当前无法判断应收应付余额。'
  if (summary.value === undefined) return '正在读取财务摘要…'
  return ''
})

const summaryCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'receivable',
    label: '应收未结',
    value: formatAmount(amountOf(summary.value?.openReceivableAmount)),
  },
  {
    key: 'payable',
    label: '应付未结',
    value: formatAmount(amountOf(summary.value?.openPayableAmount)),
  },
  {
    key: 'cost',
    label: '待入账成本',
    value: formatAmount(amountOf(summary.value?.costCandidateAmount)),
  },
  {
    key: 'vouchers',
    label: '已过账凭证',
    value: trustworthy.value ? (summary.value?.postedVoucherCount ?? 0) : UNAVAILABLE_TEXT,
    unit: trustworthy.value ? '张' : '',
    meta: trustworthy.value ? '已记账、可用于对账的凭证' : summaryStateNote.value,
  },
])

const headerCount = computed(() => {
  if (!ready.value) return UNAVAILABLE_TEXT
  if (summaryError.value != null) return '财务摘要读取失败'
  if (summary.value === undefined) return undefined
  return '应收应付概览'
})

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
      :count="headerCount"
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

    <!-- 失败时明说取不到、无法判断，并给重试；不留一条只有技术消息的红字。 -->
    <NvCard
      v-if="summaryError && !summaryPending"
      class="flex items-center justify-between gap-4 border-destructive/30 bg-destructive/[0.04] px-5 py-4"
      role="alert"
    >
      <div>
        <p class="text-sm font-medium text-destructive-strong">财务摘要读取失败</p>
        <p class="mt-1 text-sm text-muted-foreground">
          没有取到应收、应付与待入账成本，现在无法判断余额，下面的金额一律显「—」。{{
            formatError(summaryError)
          }}
        </p>
      </div>
      <NvButton size="sm" type="button" variant="outline" @click="refreshSummary">
        <RefreshCwIcon aria-hidden="true" />
        重试
      </NvButton>
    </NvCard>

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
      :error="summaryError"
      error-message="没有取到财务摘要，当前无法判断应收应付余额。请重试，或稍后再看。"
      :awaiting-scope="!ready"
      awaiting-scope-message="尚未选择业务范围，还没有发起查询——请先在顶部选择。"
      empty-message="财务摘要没有返回任何科目。"
      @retry="refreshSummary"
    >
      <template #cell-amount="{ row }">
        <span class="tabular-nums">{{
          row.item === '已过账凭证' ? (row.amount ?? UNAVAILABLE_TEXT) : formatAmount(row.amount)
        }}</span>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
