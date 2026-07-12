<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { mesHandoverStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesShiftHandovers } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '班次交接',
    requiredPermissions: ['business.mes.handovers.read'],
  },
})

const { filters, handovers, handoversError, handoversPending, handoversTotal, refreshHandovers } =
  useMesShiftHandovers()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const statusFilter = computed({
  get: () => filters.status || 'all',
  set: (value: string) => {
    filters.status = value === 'all' ? undefined : value
  },
})
const openIssueTotal = computed(() =>
  handovers.value.reduce((s, r) => s + (r.openIssueCount ?? 0), 0),
)
const errorMessage = computed(() => formatError(handoversError.value))

type HandoverRow = (typeof handovers)['value'][number]
const columns: NvDataTableColumn<HandoverRow>[] = [
  {
    key: 'handoverId',
    header: '交接单',
    cellClass: 'font-medium',
    accessor: (r) => r.handoverId ?? '无',
  },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '无' },
  { key: 'teamId', header: '班组', accessor: (r) => r.teamId ?? '无' },
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
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
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

    <NvSectionCards :columns="3">
      <NvSectionCard description="交接单" :value="handoversTotal" hint="后端筛选总数" />
      <NvSectionCard description="本页未结事项" :value="openIssueTotal" hint="当前页统计" />
      <NvSectionCard description="本页当前班次" :value="handovers.length" hint="当前页统计" />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
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
      empty-message="暂无班次交接。班次结束时创建交接单，记录未完成事项。"
    >
      <template #cell-handoverStatus="{ row }"
        ><NvStatusBadge :value="row.handoverStatus"
      /></template>
      <template #cell-openIssueCount="{ row }"
        ><span class="tabular-nums">{{ row.openIssueCount ?? 0 }}</span></template
      >
      <template #cell-createdAtUtc="{ row }">{{ formatDateTime(row.createdAtUtc) }}</template>
    </NvDataTable>
  </BusinessLayout>
</template>
