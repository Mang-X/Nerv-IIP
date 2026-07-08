<script setup lang="ts">
import type { BusinessConsoleErpPurchaseRequisitionItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useErpPurchaseRequisitions } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  StatusBadgePro,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { FileSearchIcon, RefreshCwIcon, ShoppingCartIcon } from 'lucide-vue-next'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { firstQueryParam, formatDate, formatError, formatQuantity } from './shared'

definePage({ meta: { requiresAuth: true, title: '采购申请', requiredPermissions: ['business.erp.procurement.read'] } })

const route = useRoute()
const requisitions = useErpPurchaseRequisitions()
const { page, pageSize } = usePagedList(requisitions.filters, {
  resetOn: [() => requisitions.filters.status, () => requisitions.filters.keyword],
})

watch(
  () => route.query.keyword,
  (keyword) => {
    requisitions.filters.keyword = firstQueryParam(keyword)
  },
  { immediate: true },
)

const statusFilter = computed({
  get: () => requisitions.filters.status || 'all',
  set: (value: string) => { requisitions.filters.status = value === 'all' ? undefined : value },
})

const openCount = computed(() => requisitions.items.value.filter((r) => r.status === 'Open').length)
const convertedCount = computed(() => requisitions.items.value.filter((r) => r.status === 'Converted').length)
const requestedQuantity = computed(() => requisitions.items.value.reduce((sum, r) => sum + (r.quantity ?? 0), 0))

const columns: DataTableProColumn<BusinessConsoleErpPurchaseRequisitionItem>[] = [
  { key: 'requisitionNo', header: '采购申请', cellClass: 'font-medium', accessor: (r) => r.requisitionNo ?? '-' },
  { key: 'skuCode', header: '物料', accessor: (r) => r.skuCode ?? '-' },
  { key: 'quantity', header: '申请数量', align: 'end', width: 'w-28', accessor: (r) => r.quantity ?? 0 },
  { key: 'uomCode', header: '单位', width: 'w-20', accessor: (r) => r.uomCode ?? '-' },
  { key: 'requiredDate', header: '需求日期', width: 'w-32', accessor: (r) => formatDate(r.requiredDate) },
  { key: 'siteCode', header: '工厂', width: 'w-28', accessor: (r) => r.siteCode ?? '-' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'convertedPurchaseOrderNo', header: '采购订单', width: 'w-36', accessor: (r) => r.convertedPurchaseOrderNo ?? '-' },
  { key: 'suggestionId', header: 'MRP 建议', width: 'w-40', accessor: (r) => r.suggestionId ?? '-' },
  { key: 'actions', header: '', align: 'end', width: 'w-56' },
]

function statusLabel(value?: string | null) {
  return ({ Open: '待询价/转单', Converted: '已转单', Cancelled: '已取消' } as Record<string, string>)[value ?? ''] ?? value ?? '-'
}

function canConvert(row: BusinessConsoleErpPurchaseRequisitionItem) {
  return row.status === 'Open' && !!row.requisitionNo
}

async function convertToPurchaseOrder(row: BusinessConsoleErpPurchaseRequisitionItem) {
  if (!canConvert(row)) return
  try {
    const response = await requisitions.convertToPurchaseOrder([row.requisitionNo!])
    const data = response?.success ? response.data : undefined
    if (data?.status === 'PurchaseOrderCreated' || data?.status === 'AlreadyConverted') {
      toast.success(data.purchaseOrderNo ? `已转采购订单 ${data.purchaseOrderNo}` : '采购申请已转采购订单')
      return
    }
    if (data?.status === 'RfqCreated') {
      toast.success(data.rfqNo ? `已生成 RFQ ${data.rfqNo}` : '已进入 RFQ 流程')
      return
    }
    toast.warning('缺少有效价源，请先发起 RFQ')
  } catch {
    toast.error(formatError(requisitions.convertToPurchaseOrderError.value) || '转单失败，请稍后重试。')
  }
}

function parseSupplierCodes(input: string | null): string[] {
  return (input ?? '')
    .split(',')
    .map((value) => value.trim())
    .filter((value, index, values) => value.length > 0 && values.indexOf(value) === index)
}

async function startRfq(row: BusinessConsoleErpPurchaseRequisitionItem) {
  if (!canConvert(row)) return
  const supplierCodes = parseSupplierCodes(window.prompt('供应商编码，多个用逗号分隔'))
  if (supplierCodes.length === 0) {
    toast.warning('请先输入供应商编码')
    return
  }

  try {
    const response = await requisitions.convertToPurchaseOrder([row.requisitionNo!], { rfqSupplierCodes: supplierCodes })
    const data = response?.success ? response.data : undefined
    if (data?.status === 'RfqCreated') {
      toast.success(data.rfqNo ? `已生成 RFQ ${data.rfqNo}` : '已进入 RFQ 流程')
      return
    }

    if (data?.status === 'PurchaseOrderCreated' || data?.status === 'AlreadyConverted') {
      toast.success(data.purchaseOrderNo ? `已转采购订单 ${data.purchaseOrderNo}` : '采购申请已转采购订单')
      return
    }

    toast.warning('缺少有效价源，请检查供应商候选')
  } catch {
    toast.error(formatError(requisitions.convertToPurchaseOrderError.value) || '发起 RFQ 失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="采购申请" :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]" :count="`${requisitions.total.value} 张申请`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="requisitions.pending.value" @click="requisitions.refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="3">
      <SectionCard description="待处理申请" :value="openCount" hint="可进入 RFQ 或采购订单流程" />
      <SectionCard description="已转单申请" :value="convertedCount" hint="已进入后续采购执行" />
      <SectionCard description="本页申请数量" :value="formatQuantity(requestedQuantity)" hint="按当前筛选页汇总" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <InputPro v-model="requisitions.filters.keyword" class="h-9 w-64" placeholder="申请单 / 物料 / 工厂 / MRP 建议" aria-label="采购申请关键字" />
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-36" aria-label="申请状态"><SelectProValue placeholder="申请状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部申请</SelectProItem>
            <SelectProItem value="Open">待处理</SelectProItem>
            <SelectProItem value="Converted">已转单</SelectProItem>
            <SelectProItem value="Cancelled">已取消</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="requisitions.total.value"
      :columns="columns"
      :rows="requisitions.items.value"
      :row-key="(r: BusinessConsoleErpPurchaseRequisitionItem) => r.requisitionNo ?? r.purchaseRequisitionId ?? '采购申请'"
      :loading="requisitions.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="未找到采购申请。采购类 MRP 建议接受后会在这里形成真实申请。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-quantity="{ row }"><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template>
      <template #cell-status="{ row }"><StatusBadgePro :value="statusLabel(row.status)" /></template>
      <template #cell-actions="{ row }">
        <div v-if="canConvert(row)" class="flex justify-end gap-2">
          <ButtonPro
            size="sm"
            type="button"
            variant="outline"
            :disabled="requisitions.convertToPurchaseOrderPending.value"
            @click="startRfq(row)"
          >
            <FileSearchIcon aria-hidden="true" />
            发起 RFQ
          </ButtonPro>
          <ButtonPro
            size="sm"
            type="button"
            variant="outline"
            :disabled="requisitions.convertToPurchaseOrderPending.value"
            @click="convertToPurchaseOrder(row)"
          >
            <ShoppingCartIcon aria-hidden="true" />
            转采购订单
          </ButtonPro>
        </div>
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
