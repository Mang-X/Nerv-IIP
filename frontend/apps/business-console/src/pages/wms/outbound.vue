<script setup lang="ts">
import type { BusinessConsoleWmsOutboundOrderItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import WmsInventoryContextPanel from '@/components/wms/WmsInventoryContextPanel.vue'
import { useWmsOutboundOrders } from '@/composables/useBusinessWms'
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
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
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
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '出库发货',
    requiredPermissions: ['business.wms.shipments.read'],
  },
})

const {
  filters,
  outboundOrders,
  outboundOrdersError,
  outboundOrdersPending,
  outboundOrdersTotal,
  refreshOutboundOrders,
  completeOutbound,
  completeOutboundPending,
  completeOutboundError,
  createOutbound,
  createOutboundPending,
  createOutboundError,
} = useWmsOutboundOrders()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })

const errorMessage = computed(() =>
  formatError(
    outboundOrdersError.value ?? completeOutboundError.value ?? createOutboundError.value,
  ),
)

// 后端 WMS OutboundOrderLine 要求 uomCode/正数 requestedQuantity/pickLocationCode/qualityStatus/ownerType 均非空。
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
interface OutboundLine {
  skuCode: string
  uomCode: string
  requestedQuantity: string
  pickLocationCode: string
  lotNo: string
  qualityStatus: string
  ownerType: string
}
function emptyLine(): OutboundLine {
  return {
    skuCode: '',
    uomCode: '',
    requestedQuantity: '',
    pickLocationCode: '',
    lotNo: '',
    qualityStatus: 'available',
    ownerType: 'owned',
  }
}
const createOpen = shallowRef(false)
const createError = shallowRef('')
const createForm = reactive({
  outboundOrderNo: '',
  sourceDocumentType: '',
  sourceDocumentId: '',
  siteCode: '',
  lines: [emptyLine()] as OutboundLine[],
})

function openCreate() {
  createForm.outboundOrderNo = ''
  createForm.sourceDocumentType = ''
  createForm.sourceDocumentId = ''
  createForm.siteCode = ''
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
    !createForm.outboundOrderNo.trim() ||
    !createForm.sourceDocumentType.trim() ||
    !createForm.sourceDocumentId.trim() ||
    !createForm.siteCode.trim()
  ) {
    createError.value = '请填写出库单号、来源类型、来源单据与工厂。'
    return
  }
  const filled = createForm.lines.filter(
    (l) => l.skuCode.trim() || l.uomCode.trim() || l.requestedQuantity || l.pickLocationCode.trim(),
  )
  if (filled.length === 0) {
    createError.value = '至少填写一行明细。'
    return
  }
  for (const [i, l] of filled.entries()) {
    if (!l.skuCode.trim() || !l.uomCode.trim() || !l.pickLocationCode.trim()) {
      createError.value = `第 ${i + 1} 行：物料、单位、拣货库位均必填。`
      return
    }
    if (!(Number(l.requestedQuantity) > 0)) {
      createError.value = `第 ${i + 1} 行：需求数量需为正数。`
      return
    }
  }
  const lines = filled.map((l, i) => ({
    lineNo: String(i + 1),
    skuCode: l.skuCode.trim(),
    uomCode: l.uomCode.trim(),
    requestedQuantity: Number(l.requestedQuantity),
    pickLocationCode: l.pickLocationCode.trim(),
    lotNo: l.lotNo.trim() || undefined,
    qualityStatus: l.qualityStatus,
    ownerType: l.ownerType,
  }))
  try {
    await createOutbound({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      outboundOrderNo: createForm.outboundOrderNo.trim(),
      sourceDocumentType: createForm.sourceDocumentType.trim(),
      sourceDocumentId: createForm.sourceDocumentId.trim(),
      siteCode: createForm.siteCode.trim(),
      lines,
    })
    createOpen.value = false
    toast.success('出库单已创建')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}

const reviewOpen = shallowRef(false)
const pendingOrder = shallowRef<OutboundRow>()
const form = reactive({ packReviewNo: '', passed: true })
const formError = shallowRef('')

function isCompleted(row: OutboundRow) {
  return (row.status ?? '').toLowerCase() === 'completed'
}
function openReview(row: OutboundRow) {
  pendingOrder.value = row
  form.packReviewNo = ''
  form.passed = true
  formError.value = ''
  reviewOpen.value = true
}
async function submitReview() {
  const id = pendingOrder.value?.outboundOrderId
  if (!id) return
  if (!form.packReviewNo.trim()) {
    formError.value = '请输入复核单号。'
    return
  }
  try {
    await completeOutbound(id, { packReviewNo: form.packReviewNo.trim(), passed: form.passed })
    reviewOpen.value = false
    toast.success('出库复核已提交')
  } catch {
    // 失败信息由页面错误区呈现。
  }
}
const openCount = computed(
  () => outboundOrders.value.filter((r) => (r.status ?? '').toLowerCase() !== 'completed').length,
)

type OutboundRow = BusinessConsoleWmsOutboundOrderItem
const columns: NvDataTableColumn<OutboundRow>[] = [
  {
    key: 'outboundOrderNo',
    header: '出库单号',
    cellClass: 'font-medium',
    accessor: (r) => r.outboundOrderNo ?? '无',
  },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
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
    <NvPageHeader
      title="出库发货"
      :breadcrumbs="[{ label: '仓储作业' }]"
      :count="`${outboundOrders.length} 张出库单`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="outboundOrdersPending"
          @click="refreshOutboundOrders"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建出库单
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="出库单" :value="outboundOrdersTotal" hint="后端返回总数" />
      <NvSectionCard description="本页未完成" :value="openCount" hint="待拣货/复核/发运" />
    </NvSectionCards>

    <WmsInventoryContextPanel
      title="出库库存上下文"
      gap-message="后端缺口：出库单列表暂未返回 SKU、批次/序列号、预留、冻结或来源单据字段；本页不空跳、不伪造库存余额。请从拣货任务行进入 Inventory 查看具体库存上下文。"
    />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.status"
          class="h-9 w-32"
          placeholder="状态（可选）"
          aria-label="出库单状态"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="outboundOrdersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="outboundOrders"
      :row-key="rowKey"
      :loading="outboundOrdersPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无出库单。发货作业产生出库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`完成复核 ${row.outboundOrderNo ?? ''}`"
          :disabled="isCompleted(row) || !row.outboundOrderId"
          @click="openReview(row)"
        >
          完成复核
        </NvButton>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="reviewOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>出库复核</NvDialogTitle>
          <NvDialogDescription>
            对出库单 {{ pendingOrder?.outboundOrderNo ?? '' }} 进行发货前复核。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReview">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="wms-pack-review-no">复核单号</NvFieldLabel>
              <NvInput
                id="wms-pack-review-no"
                v-model="form.packReviewNo"
                :aria-invalid="Boolean(formError)"
                autocomplete="off"
              />
              <NvFieldError v-if="formError" :errors="[formError]" />
            </NvField>
            <NvField
              orientation="horizontal"
              class="items-center justify-between rounded-lg border p-3"
            >
              <NvFieldLabel for="wms-pack-passed">复核通过</NvFieldLabel>
              <NvCheckbox id="wms-pack-passed" v-model:checked="form.passed" />
            </NvField>
          </NvFieldGroup>
          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="completeOutboundPending">提交复核</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <NvDialog v-model:open="createOpen">
      <NvDialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>新建出库单</NvDialogTitle>
          <NvDialogDescription
            >登记出库发货单的来源与明细，提交后进入拣货/复核流程。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="wms-out-no">出库单号</NvFieldLabel>
              <NvInput id="wms-out-no" v-model="createForm.outboundOrderNo" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-out-site">工厂</NvFieldLabel>
              <NvInput id="wms-out-site" v-model="createForm.siteCode" autocomplete="off" />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-out-srctype">来源类型</NvFieldLabel>
              <NvInput
                id="wms-out-srctype"
                v-model="createForm.sourceDocumentType"
                autocomplete="off"
                placeholder="如 销售发货"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="wms-out-srcid">来源单据</NvFieldLabel>
              <NvInput
                id="wms-out-srcid"
                v-model="createForm.sourceDocumentId"
                autocomplete="off"
              />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">发货明细</span>
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
                v-model="line.requestedQuantity"
                class="h-9 w-24"
                type="number"
                min="0"
                step="any"
                placeholder="需求数量*"
                :aria-label="`第 ${index + 1} 行需求数量`"
              />
              <NvInput
                v-model="line.pickLocationCode"
                class="h-9 w-24"
                placeholder="拣货库位*"
                :aria-label="`第 ${index + 1} 行拣货库位`"
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
            <NvButton type="submit" :disabled="createOutboundPending">创建出库单</NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
