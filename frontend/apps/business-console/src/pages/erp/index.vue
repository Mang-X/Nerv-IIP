<script setup lang="ts">
import type {
  BusinessConsoleErpPurchaseRequisitionItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpPurchaseRequisitions } from '@/composables/useBusinessErp'
import { useBusinessPartners } from '@/composables/useBusinessMasterData'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCheckbox,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvInput,
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
  toast,
} from '@nerv-iip/ui'
import { FileSearchIcon, RefreshCwIcon, ShoppingCartIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { firstQueryParam, formatDate, formatError, formatQuantity } from './shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '采购申请',
    requiredPermissions: ['business.erp.procurement.read'],
  },
})

const route = useRoute()
const requisitions = useErpPurchaseRequisitions()
const suppliers = useBusinessPartners()
const { page, pageSize } = usePagedList(requisitions.filters, {
  resetOn: [() => requisitions.filters.status, () => requisitions.filters.keyword],
})
suppliers.filters.includeDisabled = false

watch(
  () => route.query.keyword,
  (keyword) => {
    requisitions.filters.keyword = firstQueryParam(keyword)
  },
  { immediate: true },
)

const statusFilter = computed({
  get: () => requisitions.filters.status || 'all',
  set: (value: string) => {
    requisitions.filters.status = value === 'all' ? undefined : value
  },
})

const openCount = computed(() => requisitions.items.value.filter((r) => r.status === 'Open').length)
const convertedCount = computed(
  () => requisitions.items.value.filter((r) => r.status === 'Converted').length,
)
const requestedQuantity = computed(() =>
  requisitions.items.value.reduce((sum, r) => sum + (r.quantity ?? 0), 0),
)
const rfqDialogOpen = shallowRef(false)
const rfqRow = shallowRef<BusinessConsoleErpPurchaseRequisitionItem | null>(null)
const rfqSupplierSelection = reactive<Record<string, boolean>>({})

const columns: NvDataTableColumn<BusinessConsoleErpPurchaseRequisitionItem>[] = [
  {
    key: 'requisitionNo',
    header: '采购申请',
    cellClass: 'font-medium',
    accessor: (r) => r.requisitionNo ?? '-',
  },
  { key: 'skuCode', header: '物料', accessor: (r) => r.skuCode ?? '-' },
  {
    key: 'quantity',
    header: '申请数量',
    align: 'end',
    width: 'w-28',
    accessor: (r) => r.quantity ?? 0,
  },
  { key: 'uomCode', header: '单位', width: 'w-20', accessor: (r) => r.uomCode ?? '-' },
  {
    key: 'requiredDate',
    header: '需求日期',
    width: 'w-32',
    accessor: (r) => formatDate(r.requiredDate),
  },
  { key: 'siteCode', header: '工厂', width: 'w-28', accessor: (r) => r.siteCode ?? '-' },
  { key: 'status', header: '状态', width: 'w-28' },
  {
    key: 'convertedPurchaseOrderNo',
    header: '采购订单',
    width: 'w-36',
    accessor: (r) => r.convertedPurchaseOrderNo ?? '-',
  },
  {
    key: 'suggestionId',
    header: 'MRP 建议',
    width: 'w-40',
    accessor: (r) => r.suggestionId ?? '-',
  },
  { key: 'actions', header: '', align: 'end', width: 'w-56' },
]

function statusLabel(value?: string | null) {
  return (
    ({ Open: '待询价/转单', Converted: '已转单', Cancelled: '已取消' } as Record<string, string>)[
      value ?? ''
    ] ??
    value ??
    '-'
  )
}

function canConvert(row: BusinessConsoleErpPurchaseRequisitionItem) {
  return row.status === 'Open' && !!row.requisitionNo
}
function partnerRoles(row: BusinessConsoleResourceItem): string[] {
  return [row.partnerType, ...(row.partnerRoles ?? [])]
    .map((role) => (role ?? '').trim())
    .filter(Boolean)
}
const supplierCandidates = computed(() =>
  suppliers.partners.value
    .filter((row) => row.active !== false && !!row.code && partnerRoles(row).includes('supplier'))
    .sort((a, b) =>
      String(a.displayName ?? a.code).localeCompare(String(b.displayName ?? b.code), 'zh-Hans-CN'),
    ),
)
const selectedRfqSupplierCodes = computed(() =>
  supplierCandidates.value
    .map((row) => row.code!)
    .filter((code) => rfqSupplierSelection[code])
    .sort((a, b) => a.localeCompare(b, 'en')),
)

async function convertToPurchaseOrder(row: BusinessConsoleErpPurchaseRequisitionItem) {
  if (!canConvert(row)) return
  try {
    const response = await requisitions.convertToPurchaseOrder([row.requisitionNo!])
    const data = response?.success ? response.data : undefined
    if (data?.status === 'PurchaseOrderCreated' || data?.status === 'AlreadyConverted') {
      toast.success(
        data.purchaseOrderNo ? `已转采购订单 ${data.purchaseOrderNo}` : '采购申请已转采购订单',
      )
      return
    }
    if (data?.status === 'RfqCreated') {
      toast.success(data.rfqNo ? `已生成 RFQ ${data.rfqNo}` : '已进入 RFQ 流程')
      return
    }
    toast.warning('缺少有效价源，请先发起 RFQ')
  } catch {
    toast.error(
      formatError(requisitions.convertToPurchaseOrderError.value) || '转单失败，请稍后重试。',
    )
  }
}

function resetRfqSelection() {
  for (const code of Object.keys(rfqSupplierSelection)) {
    delete rfqSupplierSelection[code]
  }
}

function openRfqDialog(row: BusinessConsoleErpPurchaseRequisitionItem) {
  if (!canConvert(row)) return
  rfqRow.value = row
  resetRfqSelection()
  rfqDialogOpen.value = true
}

function closeRfqDialog() {
  rfqDialogOpen.value = false
  rfqRow.value = null
  resetRfqSelection()
}

async function submitRfq() {
  const row = rfqRow.value
  if (!row || !canConvert(row)) return
  const supplierCodes = selectedRfqSupplierCodes.value
  if (supplierCodes.length === 0) {
    toast.warning('请先选择供应商')
    return
  }

  try {
    const response = await requisitions.convertToPurchaseOrder([row.requisitionNo!], {
      rfqSupplierCodes: supplierCodes,
    })
    const data = response?.success ? response.data : undefined
    if (data?.status === 'RfqCreated') {
      toast.success(data.rfqNo ? `已生成 RFQ ${data.rfqNo}` : '已进入 RFQ 流程')
      closeRfqDialog()
      return
    }

    if (data?.status === 'PurchaseOrderCreated' || data?.status === 'AlreadyConverted') {
      toast.success(
        data.purchaseOrderNo ? `已转采购订单 ${data.purchaseOrderNo}` : '采购申请已转采购订单',
      )
      closeRfqDialog()
      return
    }

    toast.warning('缺少有效价源，请检查供应商候选')
  } catch {
    toast.error(
      formatError(requisitions.convertToPurchaseOrderError.value) || '发起 RFQ 失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="采购申请"
      :breadcrumbs="[{ label: '经营管理' }, { label: '采购' }]"
      :count="`${requisitions.total.value} 张申请`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="requisitions.pending.value"
          @click="requisitions.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="3">
      <NvSectionCard description="待处理申请" :value="openCount" hint="可进入 RFQ 或采购订单流程" />
      <NvSectionCard description="已转单申请" :value="convertedCount" hint="已进入后续采购执行" />
      <NvSectionCard
        description="本页申请数量"
        :value="formatQuantity(requestedQuantity)"
        hint="按当前筛选页汇总"
      />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="requisitions.filters.keyword"
          class="h-9 w-64"
          placeholder="申请单 / 物料 / 工厂 / MRP 建议"
          aria-label="采购申请关键字"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="申请状态"
            ><NvSelectValue placeholder="申请状态"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部申请</NvSelectItem>
            <NvSelectItem value="Open">待处理</NvSelectItem>
            <NvSelectItem value="Converted">已转单</NvSelectItem>
            <NvSelectItem value="Cancelled">已取消</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="requisitions.total.value"
      :columns="columns"
      :rows="requisitions.items.value"
      :row-key="
        (r: BusinessConsoleErpPurchaseRequisitionItem) =>
          r.requisitionNo ?? r.purchaseRequisitionId ?? '采购申请'
      "
      :loading="requisitions.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="未找到采购申请。采购类 MRP 建议接受后会在这里形成真实申请。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-quantity="{ row }"
        ><span class="tabular-nums">{{ formatQuantity(row.quantity) }}</span></template
      >
      <template #cell-status="{ row }"><NvStatusBadge :value="statusLabel(row.status)" /></template>
      <template #cell-actions="{ row }">
        <div v-if="canConvert(row)" class="flex justify-end gap-2">
          <NvButton
            size="sm"
            type="button"
            variant="outline"
            :disabled="requisitions.convertToPurchaseOrderPending.value"
            @click="openRfqDialog(row)"
          >
            <FileSearchIcon aria-hidden="true" />
            发起 RFQ
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            variant="outline"
            :disabled="requisitions.convertToPurchaseOrderPending.value"
            @click="convertToPurchaseOrder(row)"
          >
            <ShoppingCartIcon aria-hidden="true" />
            转采购订单
          </NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvDialog
      :open="rfqDialogOpen"
      @update:open="
        (value) => {
          if (!value) closeRfqDialog()
        }
      "
    >
      <NvDialogContent class="sm:max-w-lg">
        <NvDialogHeader>
          <NvDialogTitle>选择询价供应商</NvDialogTitle>
          <NvDialogDescription>{{ rfqRow?.requisitionNo ?? '' }}</NvDialogDescription>
        </NvDialogHeader>
        <div class="grid gap-2">
          <label
            v-for="supplier in supplierCandidates"
            :key="supplier.code"
            class="flex items-center justify-between rounded-md border px-3 py-2 text-sm"
          >
            <span>
              <span class="font-medium">{{ supplier.displayName ?? supplier.code }}</span>
              <span class="ml-2 text-muted-foreground">{{ supplier.code }}</span>
            </span>
            <NvCheckbox v-model="rfqSupplierSelection[supplier.code!]" />
          </label>
          <p v-if="supplierCandidates.length === 0" class="text-sm text-muted-foreground">
            未找到可用供应商。
          </p>
        </div>
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline" @click="closeRfqDialog">取消</NvButton>
          </NvDialogClose>
          <NvButton
            type="button"
            :disabled="
              selectedRfqSupplierCodes.length === 0 ||
              requisitions.convertToPurchaseOrderPending.value
            "
            @click="submitRfq"
          >
            <FileSearchIcon aria-hidden="true" />
            生成 RFQ
          </NvButton>
        </NvDialogFooter>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
