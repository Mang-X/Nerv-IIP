<script setup lang="ts">
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useWmsInboundOrders } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialogPro,
  AlertDialogProCancel,
  AlertDialogProContent,
  AlertDialogProDescription,
  AlertDialogProFooter,
  AlertDialogProHeader,
  AlertDialogProTitle,
  ButtonPro,
  DataTablePaginationPro,
  DataTablePro,
  DialogPro,
  DialogProClose,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  FieldPro,
  FieldProError,
  FieldProGroup,
  FieldProLabel,
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
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '收货入库' } })

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
} = useWmsInboundOrders()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.skuCode, () => filters.siteCode, () => filters.locationCode, () => filters.lotNo],
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
  return { skuCode: '', uomCode: '', receivedQuantity: '', stagingLocationCode: '', lotNo: '', qualityStatus: 'available', ownerType: 'owned' }
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
  if (!createForm.inboundOrderNo.trim() || !createForm.sourceDocumentType.trim()
    || !createForm.sourceDocumentId.trim() || !createForm.siteCode.trim()) {
    createError.value = '请填写入库单号、来源类型、来源单据与工厂。'
    return
  }
  const filled = createForm.lines.filter(
    (l) => l.skuCode.trim() || l.uomCode.trim() || l.receivedQuantity || l.stagingLocationCode.trim(),
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
  } catch {
    // 失败信息由页面错误区呈现。
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
  } catch {
    // 失败信息由页面错误区呈现。
  }
}

const errorMessage = computed(() => formatError(inboundOrdersError.value ?? completeInboundError.value ?? createInboundError.value))
const onHandQuantity = computed(() => inventoryContext.value?.onHandQuantity ?? 0)
const availableQuantity = computed(() => inventoryContext.value?.availableQuantity ?? 0)
const reservedQuantity = computed(() => inventoryContext.value?.reservedQuantity ?? 0)
// 库存上下文不可用时（后端未支持该维度），给出业务可读提示而非空白。
const contextUnavailable = computed(() => {
  const status = (inventoryContext.value?.status ?? '').toLowerCase()
  return !!inventoryContext.value && status !== '' && status !== 'ok' && status !== 'available'
})

type InboundRow = BusinessConsoleWmsInboundOrderItem
const columns: DataTableProColumn<InboundRow>[] = [
  { key: 'inboundOrderNo', header: '入库单号', cellClass: 'font-medium', accessor: (r) => r.inboundOrderNo ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
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
    <PageHeader title="收货入库" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${inboundOrdersTotal} 张入库单`">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="inboundOrdersPending" @click="refreshInboundOrders">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建入库单
        </ButtonPro>
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
        <InputPro v-model="filters.skuCode" class="h-9 w-32" placeholder="物料" aria-label="物料" />
        <InputPro v-model="filters.siteCode" class="h-9 w-20" placeholder="工厂" aria-label="工厂" />
        <InputPro v-model="filters.locationCode" class="h-9 w-24" placeholder="库位" aria-label="库位" />
        <InputPro v-model="filters.lotNo" class="h-9 w-28" placeholder="批次" aria-label="批次" />
        <InputPro v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="入库单状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro :pagination="false"
      :columns="columns"
      :rows="inboundOrders"
      :row-key="rowKey"
      :loading="inboundOrdersPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无入库单。收货作业产生入库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadgePro :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <ButtonPro
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`完成入库 ${row.inboundOrderNo ?? ''}`"
          :disabled="isCompleted(row) || !row.inboundOrderId"
          @click="openComplete(row)"
        >
          完成入库
        </ButtonPro>
      </template>
    </DataTablePro>

    <DataTablePaginationPro
      v-model:page="page"
      :page-size="pageSize"
      :total-items="inboundOrdersTotal"
      @update:page-size="(v) => (pageSize = String(v))"
    />

    <AlertDialogPro v-model:open="completeOpen">
      <AlertDialogProContent>
        <AlertDialogProHeader>
          <AlertDialogProTitle>完成入库</AlertDialogProTitle>
          <AlertDialogProDescription>
            确认完成入库单 {{ pendingOrder?.inboundOrderNo ?? '' }}？完成后将按已收货明细过账入库。
          </AlertDialogProDescription>
        </AlertDialogProHeader>
        <AlertDialogProFooter>
          <AlertDialogProCancel :disabled="completeInboundPending">取消</AlertDialogProCancel>
          <ButtonPro type="button" :disabled="completeInboundPending" @click="confirmComplete">完成入库</ButtonPro>
        </AlertDialogProFooter>
      </AlertDialogProContent>
    </AlertDialogPro>

    <DialogPro v-model:open="createOpen">
      <DialogProContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <DialogProHeader>
          <DialogProTitle>新建入库单</DialogProTitle>
          <DialogProDescription>登记收货入库单的来源与明细，提交后进入入库待处理。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldProGroup class="grid gap-3 sm:grid-cols-2">
            <FieldPro>
              <FieldProLabel for="wms-in-no">入库单号</FieldProLabel>
              <InputPro id="wms-in-no" v-model="createForm.inboundOrderNo" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="wms-in-site">工厂</FieldProLabel>
              <InputPro id="wms-in-site" v-model="createForm.siteCode" autocomplete="off" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="wms-in-srctype">来源类型</FieldProLabel>
              <InputPro id="wms-in-srctype" v-model="createForm.sourceDocumentType" autocomplete="off" placeholder="如 采购收货" />
            </FieldPro>
            <FieldPro>
              <FieldProLabel for="wms-in-srcid">来源单据</FieldProLabel>
              <InputPro id="wms-in-srcid" v-model="createForm.sourceDocumentId" autocomplete="off" />
            </FieldPro>
          </FieldProGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">收货明细</span>
              <ButtonPro type="button" size="sm" variant="outline" @click="addLine">
                <PlusIcon aria-hidden="true" />
                添加行
              </ButtonPro>
            </div>
            <div v-for="(line, index) in createForm.lines" :key="index" class="flex flex-wrap items-end gap-2 rounded-md border p-2">
              <InputPro v-model="line.skuCode" class="h-9 w-28" placeholder="物料*" :aria-label="`第 ${index + 1} 行物料`" />
              <InputPro v-model="line.uomCode" class="h-9 w-16" placeholder="单位*" :aria-label="`第 ${index + 1} 行单位`" />
              <InputPro v-model="line.receivedQuantity" class="h-9 w-24" type="number" min="0" step="any" placeholder="收货数量*" :aria-label="`第 ${index + 1} 行收货数量`" />
              <InputPro v-model="line.stagingLocationCode" class="h-9 w-24" placeholder="暂存库位*" :aria-label="`第 ${index + 1} 行暂存库位`" />
              <InputPro v-model="line.lotNo" class="h-9 w-24" placeholder="批次" :aria-label="`第 ${index + 1} 行批次`" />
              <SelectPro v-model="line.qualityStatus">
                <SelectProTrigger class="h-9 w-24" :aria-label="`第 ${index + 1} 行质量状态`"><SelectProValue /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem v-for="o in QUALITY_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                </SelectProContent>
              </SelectPro>
              <SelectPro v-model="line.ownerType">
                <SelectProTrigger class="h-9 w-24" :aria-label="`第 ${index + 1} 行货主类型`"><SelectProValue /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem v-for="o in OWNER_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                </SelectProContent>
              </SelectPro>
              <ButtonPro type="button" size="icon-sm" variant="ghost" :aria-label="`删除第 ${index + 1} 行`" @click="removeLine(index)">
                <Trash2Icon class="size-4" aria-hidden="true" />
              </ButtonPro>
            </div>
          </div>

          <FieldProError v-if="createError" :errors="[createError]" />

          <DialogProFooter>
            <DialogProClose as-child>
              <ButtonPro type="button" variant="outline">取消</ButtonPro>
            </DialogProClose>
            <ButtonPro type="submit" :disabled="createInboundPending">创建入库单</ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
