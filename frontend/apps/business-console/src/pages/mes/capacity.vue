<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useMesCapacityImpacts } from '@/composables/useBusinessMes'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import {
  mesCapacityStatusOptions,
  useMesReferenceLabels,
} from '@/composables/mes/useMesReferenceLabels'
import { describeEquipmentReason } from '@/composables/useBusinessEquipment'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
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
import { inlineErrorMessage } from '@/utils/notify'

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
const { statusLabel } = useMesReferenceLabels()
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

// 产能影响读面只回工作中心 / 设备编码，中文名在主数据里，按编码 join 出来。
const { resolveDevice, resolveWorkCenter } = useMasterDataDisplayNames({
  devices: true,
  workCenters: true,
})

type ImpactRow = (typeof capacityImpacts)['value'][number]

function workCenterCode(row: ImpactRow) {
  return row.workCenterCode ?? row.workCenterId ?? ''
}
function workCenterName(row: ImpactRow) {
  return row.workCenterName ?? resolveWorkCenter(workCenterCode(row))
}
function deviceCode(row: ImpactRow) {
  return row.deviceAssetCode ?? row.deviceAssetId ?? ''
}
function deviceName(row: ImpactRow) {
  return row.deviceAssetName ?? resolveDevice(deviceCode(row))
}
/** 「名称 编码」纯文本，供排序 / 导出用；名录查不到就只有编码，不编名字。 */
function codeText(code: string, name: string | undefined, fallback: string) {
  if (!code) return fallback
  return name ? `${name} ${code}` : code
}

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
    accessor: (r) => codeText(workCenterCode(r), workCenterName(r), '无'),
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => codeText(deviceCode(r), deviceName(r), '未指定'),
  },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'effectiveFromUtc', header: '开始', width: 'w-44' },
  { key: 'effectiveToUtc', header: '结束', width: 'w-44' },
  {
    key: 'reasonCode',
    header: '原因',
    // 产能影响的原因来自设备不可用/停机口径（equipment.*），与设备页同一份说法；
    // 非标准码（工厂自定义文本）原样显示。
    accessor: (r) => (r.reasonCode ? describeEquipmentReason(r.reasonCode).label : '无'),
  },
]

function formatDateTime(value?: string | null) {
  if (!value) return '无'
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
      <template #cell-workCenterId="{ row }">
        <CodeWithNameCell :code="workCenterCode(row)" :name="workCenterName(row)" fallback="无" />
      </template>
      <template #cell-deviceAssetId="{ row }">
        <CodeWithNameCell :code="deviceCode(row)" :name="deviceName(row)" fallback="未指定" />
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge :value="row.status" :label="statusLabel(row.status)" />
      </template>
      <template #cell-effectiveFromUtc="{ row }">{{
        formatDateTime(row.effectiveFromUtc)
      }}</template>
      <template #cell-effectiveToUtc="{ row }">{{ formatDateTime(row.effectiveToUtc) }}</template>
    </NvDataTable>
  </BusinessLayout>
</template>
