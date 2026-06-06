<script setup lang="ts">
import type { BusinessConsoleWmsOutboundOrderItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsOutboundOrders } from '@/composables/useBusinessWms'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, DataTable, PageHeader, SectionCard, SectionCards, StatusBadge } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '出库发货' } })

const { outboundOrders, outboundError, outboundPending, refreshOutbound } = useWmsOutboundOrders()

const errorMessage = computed(() => formatError(outboundError.value))
const openCount = computed(
  () => outboundOrders.value.filter((r) => (r.status ?? '').toLowerCase() !== 'completed').length,
)

type OutboundRow = BusinessConsoleWmsOutboundOrderItem
const columns: DataTableColumn<OutboundRow>[] = [
  { key: 'outboundOrderNo', header: '出库单号', cellClass: 'font-medium', accessor: (r) => r.outboundOrderNo ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', align: 'end', width: 'w-44', accessor: (r) => formatDateTime(r.createdAtUtc) },
]

function rowKey(row: OutboundRow) {
  return row.outboundOrderId ?? row.outboundOrderNo ?? '出库单'
}
function formatDateTime(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="出库发货" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${outboundOrders.length} 张出库单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="outboundPending" @click="refreshOutbound">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="出库单" :value="outboundOrders.length" hint="当前返回总数" />
      <SectionCard description="未完成" :value="openCount" hint="待拣货/复核/发运" />
    </SectionCards>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="outboundOrders"
      :row-key="rowKey"
      :loading="outboundPending"
      empty-message="暂无出库单。发货作业产生出库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
    </DataTable>
  </BusinessLayout>
</template>
