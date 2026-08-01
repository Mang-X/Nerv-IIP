<script setup lang="ts">
import type { BusinessConsoleErpDeliveryOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpDeliveryOrders } from '@/composables/useBusinessErp'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
import { buildKpiTrend, seriesFromDatedItems } from '@/utils/kpiTrend'
import PartnerNameCell from '@/components/erp/PartnerNameCell.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvMetricStrip,
  NvPageHeader,
  NvStatusBadge,
  NvToolbar,
  NvInput,
} from '@nerv-iip/ui'
import { RefreshCwIcon, TruckIcon } from '@lucide/vue'
import { computed } from 'vue'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { erpReadState, formatDateTime, readCount } from '../shared'

definePage({
  meta: { requiresAuth: true, title: '销售发货', requiredPermissions: ['business.erp.sales.read'] },
})

const deliveries = useErpDeliveryOrders()
// 客户列显名称：读面只回 customerCode，中文名在主数据业务伙伴里，前端按编码 join。
const { resolvePartner } = useBusinessPartnerNames()
const { page, pageSize } = usePagedList(deliveries.filters, {
  resetOn: [() => deliveries.filters.keyword],
})

const columns: NvDataTableColumn<BusinessConsoleErpDeliveryOrderItem>[] = [
  {
    key: 'deliveryOrderNo',
    header: '发货单号',
    cellClass: 'font-medium',
    accessor: (r) => r.deliveryOrderNo ?? '-',
  },
  { key: 'salesOrderNo', header: '销售单', accessor: (r) => r.salesOrderNo ?? '-' },
  {
    key: 'customerCode',
    header: '客户',
    accessor: (r) => resolvePartner(r.customerCode) ?? r.customerCode ?? '-',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  {
    key: 'releasedAtUtc',
    header: '发货时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.releasedAtUtc),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

const releasedCount = computed(
  () => deliveries.items.value.filter((d) => (d.status ?? '').toLowerCase() === 'released').length,
)
const customerCount = computed(
  () => new Set(deliveries.items.value.map((d) => d.customerCode).filter(Boolean)).size,
)
const readState = computed(() =>
  erpReadState({
    noun: '发货单',
    unit: '张',
    ready: deliveries.ready.value,
    pending: deliveries.pending.value,
    error: deliveries.error.value,
    total: deliveries.total.value,
    filtered: Boolean(deliveries.filters.keyword || deliveries.filters.status),
    emptyHint: '还没有发货单。销售订单履约出货后会在这里生成。',
  }),
)

const deliveryCells = computed<NvMetricStripCell[]>(() => {
  // 发货单带 releasedAtUtc，已释放发货走真实出货节奏（没释放的行没有时间戳，自然不进桶）；
  // 涉及客户是去重口径，按天累加会把同一客户重复计进桶里，只能补形状。
  const released = readState.value.trustworthy
    ? buildKpiTrend('erp.sales.deliveries', releasedCount.value, {
        kind: 'count',
        realSeries: seriesFromDatedItems(deliveries.items.value, {
          date: (d) => d.releasedAtUtc,
          value: () => 1,
          mode: 'cumulative',
        }),
      })
    : undefined
  const customers = readState.value.trustworthy
    ? buildKpiTrend('erp.sales.deliveryCustomers', customerCount.value, { kind: 'count' })
    : undefined

  return [
    {
      key: 'released',
      label: '已释放发货',
      value: readCount(readState.value, releasedCount.value),
      unit: readState.value.trustworthy ? '单' : '',
      meta: readState.value.trustworthy ? undefined : readState.value.emptyMessage,
      delta: released?.delta,
      series: released?.series,
      seriesLabels: released?.seriesLabels,
      seriesUnit: '单',
    },
    {
      key: 'customers',
      label: '涉及客户',
      value: readCount(readState.value, customerCount.value),
      unit: readState.value.trustworthy ? '家' : '',
      meta: readState.value.trustworthy ? undefined : readState.value.emptyMessage,
      delta: customers?.delta,
      series: customers?.series,
      seriesLabels: customers?.seriesLabels,
      seriesUnit: '家',
    },
  ]
})

function isReleasable(row: BusinessConsoleErpDeliveryOrderItem) {
  return !!row.deliveryOrderNo && (row.status ?? '').toLowerCase() !== 'released'
}

async function release(row: BusinessConsoleErpDeliveryOrderItem) {
  if (!row.deliveryOrderNo || !isReleasable(row)) return
  try {
    await deliveries.releaseDeliveryOrder(row.deliveryOrderNo)
    notifySuccess(`发货单 ${row.deliveryOrderNo} 已释放`)
  } catch (error) {
    notifyOperationFailure(
      '释放发货单失败',
      deliveries.releaseDeliveryOrderError.value ?? error,
      '释放发货单失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售发货"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="readState.count"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="deliveries.pending.value"
          @click="deliveries.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvMetricStrip :cells="deliveryCells" />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="deliveries.filters.keyword"
          class="h-9 w-64"
          placeholder="发货单 / 销售单 / 客户"
          aria-label="发货关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="deliveries.total.value"
      :columns="columns"
      :rows="deliveries.items.value"
      :row-key="(r: BusinessConsoleErpDeliveryOrderItem) => r.deliveryOrderNo ?? '销售发货'"
      :loading="deliveries.pending.value"
      :searchable="false"
      :column-settings="false"
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="deliveries.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-customerCode="{ row }">
        <PartnerNameCell :code="row.customerCode" />
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="!isReleasable(row) || deliveries.releaseDeliveryOrderPending.value"
          @click="release(row)"
        >
          <TruckIcon aria-hidden="true" />
          释放发货
        </NvButton>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
