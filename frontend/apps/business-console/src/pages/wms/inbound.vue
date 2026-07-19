<script setup lang="ts">
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import WmsReceivingQualityFlow from '@/components/wms/WmsReceivingQualityFlow.vue'
import { useWmsInboundOrders } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
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
import { PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '收货入库',
    requiredPermissions: ['business.wms.receipts.read'],
  },
})

const {
  filters,
  inboundOrders,
  inventoryContext,
  inboundOrdersError,
  inboundOrdersPending,
  inboundOrdersTotal,
  refreshInboundOrders,
  completeInbound,
  completeInboundPending,
  completeInboundError,
  createInbound,
  createInboundPending,
  createInboundError,
  receivingQualityGates,
  receivingQualityGatesPending,
  receivingQualityGatesError,
  supplierReturns,
  supplierReturnsPending,
  supplierReturnsError,
  refreshReceivingQuality,
} = useWmsInboundOrders()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [
    () => filters.status,
    () => filters.skuCode,
    () => filters.siteCode,
    () => filters.locationCode,
    () => filters.lotNo,
  ],
})

const completeOpen = shallowRef(false)
const pendingOrder = shallowRef<InboundRow>()

// 后端 WMS InboundOrderLine 要求 uomCode/正数 receivedQuantity/stagingLocationCode/qualityStatus/ownerType 均非空。
const QUALITY_OPTIONS = [
  { label: '可用', value: 'available' },
  { label: '待检', value: 'inspection' },
  { label: '冻结', value: 'blocked' },
  { label: '不合格', value: 'rejected' },
]
const OWNER_OPTIONS = [
  { label: '自有', value: 'owned' },
  { label: '客户', value: 'customer' },
  { label: '供应商', value: 'supplier' },
  { label: '寄售', value: 'consignment' },
]
interface InboundLine {
  skuCode: string
  uomCode: string
  receivedQuantity: string
  stagingLocationCode: string
  lotNo: string
  qualityStatus: string
  ownerType: string
}
function emptyLine(): InboundLine {
  return {
    skuCode: '',
    uomCode: '',
    receivedQuantity: '',
    stagingLocationCode: '',
    lotNo: '',
    qualityStatus: 'available',
    ownerType: 'owned',
  }
}
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  inboundOrderNo: '',
  sourceDocumentType: '',
  sourceDocumentId: '',
  siteCode: '',
  lines: [emptyLine()] as InboundLine[],
})

function openCreate() {
  createForm.inboundOrderNo = ''
  createForm.sourceDocumentType = ''
  createForm.sourceDocumentId = ''
  createForm.siteCode = filters.siteCode ?? ''
  createForm.lines = [emptyLine()]
  createError.value = ''
  createOpen.value = true
}
function addLine() {
  createForm.lines.push(emptyLine())
}
function removeLine(index: number) {
  createForm.lines.splice(index, 1)
  if (createForm.lines.length === 0) createForm.lines.push(emptyLine())
}
async function submitCreate() {
  if (
    !createForm.inboundOrderNo.trim() ||
    !createForm.sourceDocumentType.trim() ||
    !createForm.sourceDocumentId.trim() ||
    !createForm.siteCode.trim()
  ) {
    createError.value = '请填写入库单号、来源类型、来源单据与工厂。'
    return
  }
  const filled = createForm.lines.filter(
    (l) =>
      l.skuCode.trim() || l.uomCode.trim() || l.receivedQuantity || l.stagingLocationCode.trim(),
  )
  if (filled.length === 0) {
    createError.value = '至少填写一行明细。'
    return
  }
  for (const [i, l] of filled.entries()) {
    if (!l.skuCode.trim() || !l.uomCode.trim() || !l.stagingLocationCode.trim()) {
      createError.value = `第 ${i + 1} 行：物料、单位、暂存库位均必填。`
      return
    }
    if (!(Number(l.receivedQuantity) > 0)) {
      createError.value = `第 ${i + 1} 行：收货数量需为正数。`
      return
    }
  }
  const lines = filled.map((l, i) => ({
    lineNo: String(i + 1),
    skuCode: l.skuCode.trim(),
    uomCode: l.uomCode.trim(),
    receivedQuantity: Number(l.receivedQuantity),
    stagingLocationCode: l.stagingLocationCode.trim(),
    lotNo: l.lotNo.trim() || undefined,
    qualityStatus: l.qualityStatus,
    ownerType: l.ownerType,
  }))
  try {
    await createInbound({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      inboundOrderNo: createForm.inboundOrderNo.trim(),
      sourceDocumentType: createForm.sourceDocumentType.trim(),
      sourceDocumentId: createForm.sourceDocumentId.trim(),
      siteCode: createForm.siteCode.trim(),
      lines,
    })
    createOpen.value = false
    toast.success('入库单已创建')
  } catch (error) {
    toast.error(formatError(error) || '创建入库单失败，请稍后重试。')
  }
}

function isCompleted(row: InboundRow) {
  return (row.status ?? '').toLowerCase() === 'completed'
}
function openComplete(row: InboundRow) {
  pendingOrder.value = row
  completeOpen.value = true
}
async function confirmComplete() {
  const id = pendingOrder.value?.inboundOrderId
  if (!id) return
  try {
    await completeInbound(id)
    completeOpen.value = false
    toast.success('入库单已完成')
  } catch (error) {
    toast.error(formatError(error) || '完成入库失败，请稍后重试。')
  }
}

const errorMessage = computed(() =>
  formatError(
    inboundOrdersError.value ??
      completeInboundError.value ??
      createInboundError.value ??
      receivingQualityGatesError.value ??
      supplierReturnsError.value,
  ),
)
// 库存上下文不可用时（后端未支持该维度），给出业务可读提示而非空白。
const contextUnavailable = computed(() => {
  const status = (inventoryContext.value?.status ?? '').toLowerCase()
  return !!inventoryContext.value && status !== '' && status !== 'ok' && status !== 'available'
})

function refreshAll() {
  void refreshInboundOrders()
  void refreshReceivingQuality()
}

type InboundRow = BusinessConsoleWmsInboundOrderItem
const columns: NvDataTableColumn<InboundRow>[] = [
  {
    key: 'inboundOrderNo',
    header: '入库单号',
    cellClass: 'font-medium',
    accessor: (r) => r.inboundOrderNo ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'quality', header: '质检门禁', width: 'min-w-[22rem]' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

function rowKey(row: InboundRow) {
  return row.inboundOrderId ?? row.inboundOrderNo ?? '入库单'
}
function scanRecordRoute(row: InboundRow) {
  return {
    path: '/barcode/scans',
    query: {
      sourceWorkflow: 'wms.receiving',
      sourceDocumentId: row.inboundOrderNo ?? row.inboundOrderId ?? undefined,
    },
  }
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
    <NvPageHeader
      title="收货入库"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${inboundOrdersTotal} 张入库单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="inboundOrdersPending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建入库单
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="contextUnavailable" class="text-sm text-warning" role="status">
      当前条件暂无法获取库存可用量上下文。请补充物料、工厂或库位等条件后再试。
    </p>

    <WmsInventoryContextPanel
      :context="inventoryContext"
      gap-message="后端缺口：收货入库只有在物料、单位、工厂等库存范围足够时才返回 Inventory 可用量上下文；未返回时不在 WMS 页面伪造库存余额。"
    />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput v-model="filters.skuCode" class="h-9 w-32" placeholder="物料" aria-label="物料" />
        <NvInput v-model="filters.siteCode" class="h-9 w-20" placeholder="工厂" aria-label="工厂" />
        <NvInput
          v-model="filters.locationCode"
          class="h-9 w-24"
          placeholder="库位"
          aria-label="库位"
        />
        <NvInput v-model="filters.lotNo" class="h-9 w-28" placeholder="批次" aria-label="批次" />
        <NvInput
          v-model="filters.status"
          class="h-9 w-28"
          placeholder="状态（可选）"
          aria-label="入库单状态"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="inboundOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="inboundOrders"
      :row-key="rowKey"
      :loading="inboundOrdersPending || receivingQualityGatesPending || supplierReturnsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无入库单。收货作业产生入库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-quality="{ row }">
        <WmsReceivingQualityFlow
          v-if="row.inboundOrderNo"
          :inbound-order-no="row.inboundOrderNo"
          :gates="receivingQualityGates"
          :supplier-returns="supplierReturns"
          :loading="receivingQualityGatesPending || supplierReturnsPending"
          :error="receivingQualityGatesError || supplierReturnsError"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-2">
          <NvButton size="sm" type="button" variant="ghost" as-child>
            <RouterLink :to="scanRecordRoute(row)">扫码记录</RouterLink>
          </NvButton>
          <NvButton
            size="sm"
            type="button"
            variant="outline"
            :aria-label="`完成入库 ${row.inboundOrderNo ?? ''}`"
            :disabled="isCompleted(row) || !row.inboundOrderId"
            @click="openComplete(row)"
          >
            完成入库
          </NvButton>
        </div>
      </template>
    </NvDataTable>

    <NvAlertDialog v-model:open="completeOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>完成入库</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            确认完成入库单 {{ pendingOrder?.inboundOrderNo ?? '' }}？完成后将按已收货明细过账入库。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel :disabled="completeInboundPending">取消</NvAlertDialogCancel>
          <NvButton type="button" :disabled="completeInboundPending" @click="confirmComplete"
            >完成入库</NvButton
          >
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>新建入库单</NvDialogTitle>
          <NvDialogDescription
            >登记收货入库单的来源与明细，提交后进入入库待处理。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="wms-in-no">入库单号</NvFieldLabel>
              <NvInput id="wms-in-no" v-model="createForm.inboundOrderNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-in-site">工厂</NvFieldLabel>
              <NvInput id="wms-in-site" v-model="createForm.siteCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-in-srctype">来源类型</NvFieldLabel>
              <NvInput
                id="wms-in-srctype"
                v-model="createForm.sourceDocumentType"
                autocomplete="off"
                placeholder="如 采购收货"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-in-srcid">来源单据</NvFieldLabel>
              <NvInput id="wms-in-srcid" v-model="createForm.sourceDocumentId" autocomplete="off" />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">收货明细</span>
              <NvButton type="button" size="sm" variant="outline" @click="addLine">
                <PlusIcon aria-hidden="true" />
                添加行
              </NvButton>
            </div>
            <div
              v-for="(line, index) in createForm.lines"
              :key="index"
              class="flex flex-wrap items-end gap-2 rounded-md border p-2"
            >
              <NvInput
                v-model="line.skuCode"
                class="h-9 w-28"
                placeholder="物料*"
                :aria-label="`第 ${index + 1} 行物料`"
              />
              <NvInput
                v-model="line.uomCode"
                class="h-9 w-16"
                placeholder="单位*"
                :aria-label="`第 ${index + 1} 行单位`"
              />
              <NvInput
                v-model="line.receivedQuantity"
                class="h-9 w-24"
                type="number"
                min="0"
                step="any"
                placeholder="收货数量*"
                :aria-label="`第 ${index + 1} 行收货数量`"
              />
              <NvInput
                v-model="line.stagingLocationCode"
                class="h-9 w-24"
                placeholder="暂存库位*"
                :aria-label="`第 ${index + 1} 行暂存库位`"
              />
              <NvInput
                v-model="line.lotNo"
                class="h-9 w-24"
                placeholder="批次"
                :aria-label="`第 ${index + 1} 行批次`"
              />
              <NvSelect v-model="line.qualityStatus">
                <NvSelectTrigger class="h-9 w-24" :aria-label="`第 ${index + 1} 行质量状态`"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in QUALITY_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <NvSelect v-model="line.ownerType">
                <NvSelectTrigger class="h-9 w-24" :aria-label="`第 ${index + 1} 行货主类型`"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem v-for="o in OWNER_OPTIONS" :key="o.value" :value="o.value">{{
                    o.label
                  }}</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
              <NvButton
                type="button"
                size="icon-sm"
                variant="ghost"
                :aria-label="`删除第 ${index + 1} 行`"
                @click="removeLine(index)"
              >
                <Trash2Icon class="size-4" aria-hidden="true" />
              </NvButton>
            </div>
          </div>

          <NvFieldError v-if="createError" :errors="[createError]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="createInboundPending">创建入库单</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
