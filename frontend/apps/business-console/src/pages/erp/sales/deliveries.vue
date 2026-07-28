<script setup lang="ts">
import type { BusinessConsoleErpDeliveryOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpDeliveryOrders } from '@/composables/useBusinessErp'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
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
  toast,
} from '@nerv-iip/ui'
import { RefreshCwIcon, TruckIcon } from '@lucide/vue'
import { computed } from 'vue'
import { formatDateTime, formatError } from '../shared'

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
const deliveryCells = computed<NvMetricStripCell[]>(() => [
  { key: 'released', label: '已释放发货', value: releasedCount.value, unit: '单' },
  { key: 'customers', label: '涉及客户', value: customerCount.value, unit: '家' },
])

function isReleasable(row: BusinessConsoleErpDeliveryOrderItem) {
  return !!row.deliveryOrderNo && (row.status ?? '').toLowerCase() !== 'released'
}

async function release(row: BusinessConsoleErpDeliveryOrderItem) {
  if (!row.deliveryOrderNo || !isReleasable(row)) return
  try {
    await deliveries.releaseDeliveryOrder(row.deliveryOrderNo)
    toast.success(`发货单 ${row.deliveryOrderNo} 已释放`)
  } catch {
    toast.error(formatError(deliveries.releaseDeliveryOrderError.value) || '释放失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售发货"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="`${deliveries.total.value} 张发货单`"
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
      empty-message="暂无发货单。销售订单履约出货后会在这里生成。"
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
