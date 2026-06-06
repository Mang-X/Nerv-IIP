<script setup lang="ts">
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsInboundOrders } from '@/composables/useBusinessWms'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '收货入库' } })

const { filters, inboundOrders, inventoryContext, inboundError, inboundPending, refreshInbound } = useWmsInboundOrders()

const errorMessage = computed(() => formatError(inboundError.value))
const onHandQuantity = computed(() => inventoryContext.value?.onHandQuantity ?? 0)
const availableQuantity = computed(() => inventoryContext.value?.availableQuantity ?? 0)
const reservedQuantity = computed(() => inventoryContext.value?.reservedQuantity ?? 0)
// 库存上下文不可用时（后端未支持该维度），给出业务可读提示而非空白。
const contextUnavailable = computed(() => {
  const status = (inventoryContext.value?.status ?? '').toLowerCase()
  return !!inventoryContext.value && status !== '' && status !== 'ok' && status !== 'available'
})

type InboundRow = BusinessConsoleWmsInboundOrderItem
const columns: DataTableColumn<InboundRow>[] = [
  { key: 'inboundOrderNo', header: '入库单号', cellClass: 'font-medium', accessor: (r) => r.inboundOrderNo ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', align: 'end', width: 'w-44', accessor: (r) => formatDateTime(r.createdAtUtc) },
]

function rowKey(row: InboundRow) {
  return row.inboundOrderId ?? row.inboundOrderNo ?? '入库单'
}
function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
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
    <PageHeader title="收货入库" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${inboundOrders.length} 张入库单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="inboundPending" @click="refreshInbound">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="现存量" :value="formatQuantity(onHandQuantity)" :hint="filters.skuCode || '按下方条件查询库存'" />
      <SectionCard description="可用量" :value="formatQuantity(availableQuantity)" :hint="filters.siteCode || '工厂/库位可细化'" />
      <SectionCard description="预留量" :value="formatQuantity(reservedQuantity)" hint="已被占用" />
    </SectionCards>

    <p v-if="contextUnavailable" class="text-sm text-warning" role="status">
      当前条件暂无法获取库存可用量上下文。请补充物料、工厂或库位等条件后再试。
    </p>

    <Toolbar :show-search="false">
      <template #filters>
        <Input v-model="filters.skuCode" class="h-9 w-32" placeholder="物料" aria-label="物料" />
        <Input v-model="filters.siteCode" class="h-9 w-20" placeholder="工厂" aria-label="工厂" />
        <Input v-model="filters.locationCode" class="h-9 w-24" placeholder="库位" aria-label="库位" />
        <Input v-model="filters.lotNo" class="h-9 w-28" placeholder="批次" aria-label="批次" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="inboundOrders"
      :row-key="rowKey"
      :loading="inboundPending"
      empty-message="暂无入库单。收货作业产生入库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
    </DataTable>
  </BusinessLayout>
</template>
