<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { mesHandoverStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { labelFor, MES_HANDOVER_STATUS_LABELS } from '@/data/businessLabels'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { useMesShiftHandovers } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { inlineErrorMessage } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '班次交接',
    requiredPermissions: ['business.mes.handovers.read'],
  },
})

const { filters, handovers, handoversError, handoversPending, handoversTotal, refreshHandovers } =
  useMesShiftHandovers()
const { keyword } = useMesKeywordFilter(filters)
// 班次 / 班组名称优先用 DTO 与主数据目录；目录查不到时显占位，不回吐内部标识。
const { resolveShiftLabel } = useMesDisplayNames({ shifts: true })
const { resolveTeam } = useMasterDataDisplayNames({ teams: true })
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const openIssueTotal = computed(() =>
  handovers.value.reduce((s, r) => s + (r.openIssueCount ?? 0), 0),
)
const acceptedCount = computed(
  () => handovers.value.filter((r) => (r.handoverStatus ?? '').toLowerCase() === 'accepted').length,
)
// 交接的决策点是「还有几张没接班」——构成卡按接班状态拆分，未结事项另作提示。
const handoverSegments = computed(() =>
  pagedBreakdownSegments(handoversTotal.value, [
    {
      key: 'open',
      label: '待接班',
      value: handovers.value.length - acceptedCount.value,
      tone: 'warning',
    },
    { key: 'accepted', label: '已接班', value: acceptedCount.value, tone: 'success' },
  ]),
)
const errorMessage = computed(() => formatError(handoversError.value))

type HandoverRow = (typeof handovers)['value'][number]
const columns: NvDataTableColumn<HandoverRow>[] = [
  {
    key: 'handoverId',
    header: '交接单',
    cellClass: 'font-medium',
    accessor: () => '—',
  },
  { key: 'shiftId', header: '班次', accessor: (r) => resolveShiftLabel(r.shiftId) },
  {
    key: 'teamId',
    header: '班组',
    accessor: (r) => r.teamName?.trim() || resolveTeam(r.teamId) || '未指派',
  },
  { key: 'handoverStatus', header: '状态', width: 'w-24' },
  { key: 'openIssueCount', header: '未结事项', align: 'end', width: 'w-24' },
  { key: 'createdAtUtc', header: '创建时间', width: 'w-44' },
]

function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="班次交接"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${handoversTotal} 条交接`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="handoversPending"
          @click="refreshHandovers"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
      <NvMetricCard
        variant="breakdown"
        label="交接单"
        :value="handoversTotal"
        unit="张"
        :segments="handoverSegments"
      />
      <NvMetricCard
        variant="alert"
        label="未结事项"
        :value="openIssueTotal"
        unit="项"
        :tone="openIssueTotal > 0 ? 'warning' : 'neutral'"
        :status="
          openIssueTotal > 0
            ? { label: '需接班人跟进', tone: 'warning' }
            : { label: '无遗留', tone: 'success' }
        "
        :foot-start="
          openIssueTotal > 0
            ? '接班前逐项确认，未闭环的问题会带进下一班。'
            : '上一班的问题已全部闭环。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="交接单 / 班次 / 班组"
          aria-label="搜索班次交接"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="交接状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesHandoverStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="handoversTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="handovers"
      row-key="handoverId"
      :loading="handoversPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无班次交接。先在班次结束时创建交接单登记未完成事项，再由接班人在这里确认接收。"
    >
      <template #cell-handoverStatus="{ row }">
        <NvStatusBadge
          :value="row.handoverStatus"
          :label="labelFor(MES_HANDOVER_STATUS_LABELS, row.handoverStatus) || '未知'"
        />
      </template>
      <template #cell-openIssueCount="{ row }"
        ><span class="tabular-nums">{{ row.openIssueCount ?? 0 }}</span></template
      >
      <template #cell-createdAtUtc="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
    </NvDataTable>
  </BusinessLayout>
</template>
