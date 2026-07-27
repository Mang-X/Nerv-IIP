<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesCapacityImpacts } from '@/composables/useBusinessMes'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { mesCapacityStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
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
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '产能影响',
    requiredPermissions: ['business.mes.capacity.read'],
  },
})

const {
  capacityImpacts,
  capacityImpactsError,
  capacityImpactsPending,
  capacityImpactsTotal,
  filters,
  refreshCapacityImpacts,
} = useMesCapacityImpacts()
const { keyword } = useMesKeywordFilter(filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})
const statusFilter = shallowRef('all')

const openCount = computed(
  () => capacityImpacts.value.filter((item) => item.status?.toLowerCase() === 'open').length,
)
// 产能影响的决策点是「还有多少没恢复」——一张构成卡把总量与恢复进度放在一起。
const impactSegments = computed(() =>
  pagedBreakdownSegments(capacityImpactsTotal.value, [
    { key: 'open', label: '未恢复', value: openCount.value, tone: 'danger' },
    {
      key: 'recovered',
      label: '已恢复',
      value: capacityImpacts.value.length - openCount.value,
      tone: 'success',
    },
  ]),
)
const errorMessage = computed(() => formatError(capacityImpactsError.value))
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

type ImpactRow = (typeof capacityImpacts)['value'][number]
const columns: NvDataTableColumn<ImpactRow>[] = [
  {
    key: 'impactId',
    header: '影响编号',
    cellClass: 'font-medium',
    accessor: (r) => r.impactId ?? '无',
  },
  {
    key: 'workCenterId',
    header: '工作中心',
    accessor: (r) => r.workCenterName ?? r.workCenterCode ?? r.workCenterId ?? '无',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定',
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveFromUtc', header: '开始', width: 'w-44' },
  { key: 'effectiveToUtc', header: '结束', width: 'w-44' },
  { key: 'reasonCode', header: '原因', accessor: (r) => r.reasonCode ?? '无' },
]

function formatDateTime(value?: string | null) {
  if (!value) return '无'
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
      title="产能影响"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${capacityImpactsTotal} 条影响`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="capacityImpactsPending"
          @click="refreshCapacityImpacts"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <div class="grid gap-4 sm:grid-cols-2">
      <NvMetricCard
        variant="breakdown"
        label="影响记录"
        :value="capacityImpactsTotal"
        unit="条"
        :segments="impactSegments"
      />
      <NvMetricCard
        variant="alert"
        label="未恢复影响"
        :value="openCount"
        unit="条"
        :tone="openCount > 0 ? 'danger' : 'neutral'"
        :status="
          openCount > 0
            ? { label: '影响产能', tone: 'danger' }
            : { label: '全部恢复', tone: 'success' }
        "
        :foot-start="
          openCount > 0 ? '优先处理仍在影响排产的设备与停机事件。' : '当前没有未恢复的产能影响。'
        "
      />
    </div>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="工作中心 / 设备 / 原因"
          aria-label="搜索产能影响"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="影响状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesCapacityStatusOptions"
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
      :total-items="capacityImpactsTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="capacityImpacts"
      row-key="impactId"
      :loading="capacityImpactsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无产能影响。先在设备与停机登记异常或维护占用，再回到这里跟踪对产线产能的影响。"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-effectiveFromUtc="{ row }">{{
        formatDateTime(row.effectiveFromUtc)
      }}</template>
      <template #cell-effectiveToUtc="{ row }">{{ formatDateTime(row.effectiveToUtc) }}</template>
    </NvDataTable>
  </BusinessLayout>
</template>
