<script setup lang="ts">
import type { BusinessConsoleErpJournalVoucherItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import type { BuildKpiTrendOptions } from '@/utils/kpiTrend'
import { useErpFinanceSummary, useErpJournalVouchers } from '@/composables/useBusinessErp'
import { buildKpiTrend } from '@/utils/kpiTrend'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCard,
  NvDataTable,
  NvMetricStrip,
  NvPageHeader,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { ArrowRightIcon, RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'
import { UNAVAILABLE_TEXT, erpReadState, formatAmount, formatDate, formatError } from '../shared'

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

/**
 * 趋势与 `—` 同一把闸：读数不可信时连线带幅度一起不给。
 *
 * 摘要接口只回当前值、没有历史读面，所以这四条线都是由当前真值反推的形状
 * （末点恒等于卡片数字）；一旦读数本身不可信，画出来的就是一条凭空的线。
 */
function trendOf(key: string, value: number | null | undefined, options: BuildKpiTrendOptions) {
  return trustworthy.value ? buildKpiTrend(key, value, options) : undefined
}

/** 页头与摘要卡的状态说明：只说「为什么没有数字」，不说任何"暂无 / 正常"式结论。 */
const summaryStateNote = computed(() => {
  if (!ready.value) return '尚未选择业务范围，还没有发起查询。'
  if (summaryError.value != null) return '财务摘要读取失败，当前无法判断应收应付余额。'
  if (summary.value === undefined) return '正在读取财务摘要…'
  return ''
})

// 原来的「范围」列并进卡片副行——四个数字只出现一次（#1418 B3：下方表格曾逐行复读
// 同样四个数，无增量信息；那张表已换成「最近凭证」流水）。
const summaryCells = computed<NvMetricStripCell[]>(() => {
  // 应收/应付本身无好坏（欠款多不等于经营差），交给财务判断，只报方向不配色；
  // 待入账成本涨了是坏事，lower-better 让升箭头读作告警。
  const receivable = trendOf('erp.finance.receivable', summary.value?.openReceivableAmount, {
    kind: 'amount',
    polarity: 'neutral',
  })
  const payable = trendOf('erp.finance.payable', summary.value?.openPayableAmount, {
    kind: 'amount',
    polarity: 'neutral',
  })
  const cost = trendOf('erp.finance.costCandidate', summary.value?.costCandidateAmount, {
    kind: 'amount',
    polarity: 'lower-better',
  })
  const vouchers = trendOf('erp.finance.vouchers', summary.value?.postedVoucherCount, {
    kind: 'count',
  })

  return [
    {
      key: 'receivable',
      label: '应收未结',
      value: formatAmount(amountOf(summary.value?.openReceivableAmount)),
      meta: trustworthy.value ? '客户应收 · 全库口径' : summaryStateNote.value,
      delta: receivable?.delta,
      series: receivable?.series,
      seriesLabels: receivable?.seriesLabels,
    },
    {
      key: 'payable',
      label: '应付未结',
      value: formatAmount(amountOf(summary.value?.openPayableAmount)),
      meta: trustworthy.value ? '供应商应付 · 全库口径' : summaryStateNote.value,
      delta: payable?.delta,
      series: payable?.series,
      seriesLabels: payable?.seriesLabels,
    },
    {
      key: 'cost',
      label: '待入账成本',
      value: formatAmount(amountOf(summary.value?.costCandidateAmount)),
      meta: trustworthy.value ? '成本候选 · 等待生成凭证' : summaryStateNote.value,
      delta: cost?.delta,
      series: cost?.series,
      seriesLabels: cost?.seriesLabels,
    },
    {
      key: 'vouchers',
      label: '已过账凭证',
      value: trustworthy.value ? (summary.value?.postedVoucherCount ?? 0) : UNAVAILABLE_TEXT,
      unit: trustworthy.value ? '张' : '',
      meta: trustworthy.value ? '已记账、可用于对账的凭证' : summaryStateNote.value,
      delta: vouchers?.delta,
      series: vouchers?.series,
      seriesLabels: vouchers?.seriesLabels,
      seriesUnit: '张',
    },
  ]
})

const headerCount = computed(() => {
  if (!ready.value) return UNAVAILABLE_TEXT
  if (summaryError.value != null) return '财务摘要读取失败'
  if (summary.value === undefined) return undefined
  return '应收应付概览'
})

/**
 * 最近凭证流水：给总览页第二眼的**增量信息**（哪些账在动、金额多大），
 * 而不是把上面四个总数再抄一遍。口径天然自洽——凭证读面按记账时间倒序，
 * 取第一页就是「最近的凭证」，标题如实写明，完整分页在会计凭证页。
 */
const vouchers = useErpJournalVouchers()
const voucherColumns: NvDataTableColumn<BusinessConsoleErpJournalVoucherItem>[] = [
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
    width: 'w-36',
    accessor: (r) => r.totalDebitAmount ?? 0,
  },
  {
    key: 'totalCreditAmount',
    header: '贷方',
    align: 'end',
    width: 'w-36',
    accessor: (r) => r.totalCreditAmount ?? 0,
  },
]
const voucherReadState = computed(() =>
  erpReadState({
    noun: '会计凭证',
    unit: '张',
    ready: vouchers.ready.value,
    pending: vouchers.pending.value,
    error: vouchers.error.value,
    total: vouchers.total.value,
    filtered: false,
    emptyHint: '还没有已过账的凭证。业务单据入账后会在这里形成流水。',
  }),
)
/** 只取最近 8 张——总览页给趋势感，不替代凭证页的完整分页。 */
const recentVouchers = computed(() => vouchers.items.value.slice(0, 8))

function refreshAll() {
  refreshSummary()
  void vouchers.refresh()
}
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
          @click="refreshAll"
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
          没有取到应收、应付与待入账成本，现在无法判断余额，上面的金额一律显「—」。{{
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

    <section aria-label="最近凭证">
      <div class="mb-2 flex items-center justify-between gap-4">
        <div>
          <h2 class="text-sm font-medium text-foreground">最近凭证</h2>
          <p class="mt-0.5 text-xs text-muted-foreground">最近过账的凭证，按记账时间倒序。</p>
        </div>
        <NvButton as-child size="sm" variant="ghost">
          <RouterLink to="/erp/finance/vouchers">
            查看全部凭证
            <ArrowRightIcon aria-hidden="true" />
          </RouterLink>
        </NvButton>
      </div>
      <NvDataTable
        :columns="voucherColumns"
        :rows="recentVouchers"
        :row-key="(r: BusinessConsoleErpJournalVoucherItem) => r.voucherNo ?? '凭证'"
        :loading="vouchers.pending.value"
        :searchable="false"
        :column-settings="false"
        :pagination="false"
        :error="voucherReadState.error"
        :error-message="voucherReadState.errorMessage"
        :awaiting-scope="voucherReadState.awaitingScope"
        :awaiting-scope-message="voucherReadState.awaitingScopeMessage"
        :empty-message="voucherReadState.emptyMessage"
        @retry="vouchers.refresh"
      >
        <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
        <template #cell-totalDebitAmount="{ row }"
          ><span class="tabular-nums">{{ formatAmount(row.totalDebitAmount) }}</span></template
        >
        <template #cell-totalCreditAmount="{ row }"
          ><span class="tabular-nums">{{ formatAmount(row.totalCreditAmount) }}</span></template
        >
      </NvDataTable>
    </section>
  </BusinessLayout>
</template>
