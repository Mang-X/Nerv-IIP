<script setup lang="ts">
import type {
  BusinessConsoleErpPurchaseRequisitionItem,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment } from '@nerv-iip/ui'
import { useErpPurchaseRequisitions } from '@/composables/useBusinessErp'
import { useBusinessPartners } from '@/composables/useBusinessMasterData'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { usePagedList } from '@/composables/usePagedList'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
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
  NvMetricCard,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { FileSearchIcon, RefreshCwIcon, ShoppingCartIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { firstQueryParam, formatDate, formatQuantity } from './shared'

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
const cancelledCount = computed(
  () => requisitions.items.value.filter((r) => r.status === 'Cancelled').length,
)
// 采购申请的决策点是「还有多少没转出去」——总量与流转进度放在一张构成卡里，
// 分段只能按已取回的行统计，差额由 pagedBreakdownSegments 补齐，保证分母守恒。
const requisitionSegments = computed(() => {
  const segments: NvMetricSegment[] = [
    { key: 'open', label: '待处理', value: openCount.value, tone: 'warning' },
    { key: 'converted', label: '已转单', value: convertedCount.value, tone: 'success' },
  ]
  if (cancelledCount.value > 0) {
    segments.push({
      key: 'cancelled',
      label: '已取消',
      value: cancelledCount.value,
      tone: 'neutral',
    })
  }
  return pagedBreakdownSegments(requisitions.total.value, segments)
})
const rfqDialogOpen = shallowRef(false)
const rfqRow = shallowRef<BusinessConsoleErpPurchaseRequisitionItem | null>(null)
const rfqSupplierSelection = reactive<Record<string, boolean>>({})
// 点「生成 RFQ」后才标红/给汇总提示，打开弹窗时不预先报错。
const rfqShowErrors = shallowRef(false)

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
// 询价对象完全由所选申请行带出，弹窗里不再让用户重填单号/物料/数量。
const rfqContextItems = computed(() => {
  const row = rfqRow.value
  if (!row) return []
  return [
    { label: '采购申请', value: row.requisitionNo },
    { label: '物料', value: row.skuCode },
    {
      label: '申请数量',
      value:
        row.quantity === null || row.quantity === undefined
          ? undefined
          : `${formatQuantity(row.quantity)}${row.uomCode ? ` ${row.uomCode}` : ''}`,
    },
    { label: '需求日期', value: row.requiredDate ? formatDate(row.requiredDate) : undefined },
    { label: '工厂', value: row.siteCode },
  ]
})

async function convertToPurchaseOrder(row: BusinessConsoleErpPurchaseRequisitionItem) {
  if (!canConvert(row)) return
  try {
    const response = await requisitions.convertToPurchaseOrder([row.requisitionNo!])
    const data = response?.success ? response.data : undefined
    if (data?.status === 'PurchaseOrderCreated' || data?.status === 'AlreadyConverted') {
      notifySuccess(
        data.purchaseOrderNo ? `已转采购订单 ${data.purchaseOrderNo}` : '采购申请已转采购订单',
      )
      return
    }
    if (data?.status === 'RfqCreated') {
      notifySuccess(data.rfqNo ? `已生成 RFQ ${data.rfqNo}` : '已进入 RFQ 流程')
      return
    }
    toast.warning('缺少有效价源，请先发起 RFQ')
  } catch (error) {
    notifyError(requisitions.convertToPurchaseOrderError.value ?? error, '转单失败，请稍后重试。')
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
  rfqShowErrors.value = false
  rfqDialogOpen.value = true
}

function closeRfqDialog() {
  rfqDialogOpen.value = false
  rfqRow.value = null
  rfqShowErrors.value = false
  resetRfqSelection()
}

async function submitRfq() {
  const row = rfqRow.value
  if (!row || !canConvert(row)) return
  rfqShowErrors.value = true
  const supplierCodes = selectedRfqSupplierCodes.value
  if (supplierCodes.length === 0) return

  try {
    const response = await requisitions.convertToPurchaseOrder([row.requisitionNo!], {
      rfqSupplierCodes: supplierCodes,
    })
    const data = response?.success ? response.data : undefined
    if (data?.status === 'RfqCreated') {
      notifySuccess(data.rfqNo ? `已生成 RFQ ${data.rfqNo}` : '已进入 RFQ 流程')
      closeRfqDialog()
      return
    }

    if (data?.status === 'PurchaseOrderCreated' || data?.status === 'AlreadyConverted') {
      notifySuccess(
        data.purchaseOrderNo ? `已转采购订单 ${data.purchaseOrderNo}` : '采购申请已转采购订单',
      )
      closeRfqDialog()
      return
    }

    toast.warning('缺少有效价源，请检查供应商候选')
  } catch (error) {
    notifyError(
      requisitions.convertToPurchaseOrderError.value ?? error,
      '发起 RFQ 失败，请稍后重试。',
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

    <div class="grid gap-4 sm:grid-cols-2">
      <NvMetricCard
        variant="breakdown"
        label="采购申请"
        :value="requisitions.total.value"
        unit="张"
        :segments="requisitionSegments"
      />
      <NvMetricCard
        variant="alert"
        label="待转采购订单"
        :value="openCount"
        unit="张"
        :tone="openCount > 0 ? 'warning' : 'neutral'"
        :status="
          openCount > 0
            ? { label: '待采购处理', tone: 'warning' }
            : { label: '无待办', tone: 'success' }
        "
        :foot-start="openCount > 0 ? '确认供应策略后转为采购订单。' : '当前采购申请均已完成流转。'"
      />
    </div>

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
          <!-- 询价对象已在下方只读区呈现，描述仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            询价对象：采购申请 {{ rfqRow?.requisitionNo ?? '' }}。
          </NvDialogDescription>
        </NvDialogHeader>
        <div class="grid gap-4">
          <CarriedContextSummary label="询价对象" :items="rfqContextItems" />
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
          <p
            v-if="rfqShowErrors && selectedRfqSupplierCodes.length === 0"
            class="text-sm text-destructive"
            role="alert"
          >
            请至少选择一家供应商。
          </p>
        </div>
        <NvDialogFooter>
          <NvDialogClose as-child>
            <NvButton type="button" variant="outline" @click="closeRfqDialog">取消</NvButton>
          </NvDialogClose>
          <NvButton
            type="button"
            :disabled="requisitions.convertToPurchaseOrderPending.value"
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
