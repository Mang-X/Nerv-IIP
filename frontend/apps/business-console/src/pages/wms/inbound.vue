<script setup lang="ts">
import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsInboundOrders } from '@/composables/useBusinessWms'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialog,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  Button,
  DataTable,
  DataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  SectionCard,
  SectionCards,
  StatusBadge,
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

interface InboundLine {
  skuCode: string
  uomCode: string
  receivedQuantity: string
  stagingLocationCode: string
  lotNo: string
}
function emptyLine(): InboundLine {
  return { skuCode: '', uomCode: '', receivedQuantity: '', stagingLocationCode: '', lotNo: '' }
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
  const lines = createForm.lines
    .filter((l) => l.skuCode.trim())
    .map((l, i) => ({
      lineNo: String(i + 1),
      skuCode: l.skuCode.trim(),
      uomCode: l.uomCode.trim() || undefined,
      receivedQuantity: l.receivedQuantity ? Number(l.receivedQuantity) : undefined,
      stagingLocationCode: l.stagingLocationCode.trim() || undefined,
      lotNo: l.lotNo.trim() || undefined,
    }))
  if (lines.length === 0) {
    createError.value = '至少填写一行明细（物料必填）。'
    return
  }
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
const columns: DataTableColumn<InboundRow>[] = [
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
        <Button size="sm" type="button" variant="outline" :disabled="inboundOrdersPending" @click="refreshInboundOrders">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Button size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建入库单
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
        <Input v-model="filters.status" class="h-9 w-28" placeholder="状态（可选）" aria-label="入库单状态" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="inboundOrders"
      :row-key="rowKey"
      :loading="inboundOrdersPending"
      empty-message="暂无入库单。收货作业产生入库单后会出现在这里。"
    >
      <template #cell-status="{ row }"><StatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <Button
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`完成入库 ${row.inboundOrderNo ?? ''}`"
          :disabled="isCompleted(row) || !row.inboundOrderId"
          @click="openComplete(row)"
        >
          完成入库
        </Button>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="inboundOrdersTotal" />

    <AlertDialog v-model:open="completeOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>完成入库</AlertDialogTitle>
          <AlertDialogDescription>
            确认完成入库单 {{ pendingOrder?.inboundOrderNo ?? '' }}？完成后将按已收货明细过账入库。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel :disabled="completeInboundPending">取消</AlertDialogCancel>
          <Button type="button" :disabled="completeInboundPending" @click="confirmComplete">完成入库</Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="createOpen">
      <DialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>新建入库单</DialogTitle>
          <DialogDescription>登记收货入库单的来源与明细，提交后进入入库待处理。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="wms-in-no">入库单号</FieldLabel>
              <Input id="wms-in-no" v-model="createForm.inboundOrderNo" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-in-site">工厂</FieldLabel>
              <Input id="wms-in-site" v-model="createForm.siteCode" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-in-srctype">来源类型</FieldLabel>
              <Input id="wms-in-srctype" v-model="createForm.sourceDocumentType" autocomplete="off" placeholder="如 采购收货" />
            </Field>
            <Field>
              <FieldLabel for="wms-in-srcid">来源单据</FieldLabel>
              <Input id="wms-in-srcid" v-model="createForm.sourceDocumentId" autocomplete="off" />
            </Field>
          </FieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">收货明细</span>
              <Button type="button" size="sm" variant="outline" @click="addLine">
                <PlusIcon aria-hidden="true" />
                添加行
              </Button>
            </div>
            <div v-for="(line, index) in createForm.lines" :key="index" class="flex flex-wrap items-end gap-2 rounded-md border p-2">
              <Input v-model="line.skuCode" class="h-9 w-32" placeholder="物料" :aria-label="`第 ${index + 1} 行物料`" />
              <Input v-model="line.uomCode" class="h-9 w-20" placeholder="单位" :aria-label="`第 ${index + 1} 行单位`" />
              <Input v-model="line.receivedQuantity" class="h-9 w-24" type="number" placeholder="收货数量" :aria-label="`第 ${index + 1} 行收货数量`" />
              <Input v-model="line.stagingLocationCode" class="h-9 w-24" placeholder="暂存库位" :aria-label="`第 ${index + 1} 行暂存库位`" />
              <Input v-model="line.lotNo" class="h-9 w-28" placeholder="批次" :aria-label="`第 ${index + 1} 行批次`" />
              <Button type="button" size="icon-sm" variant="ghost" :aria-label="`删除第 ${index + 1} 行`" @click="removeLine(index)">
                <Trash2Icon class="size-4" aria-hidden="true" />
              </Button>
            </div>
          </div>

          <FieldError v-if="createError" :errors="[createError]" />

          <DialogFooter show-close-button>
            <Button type="submit" :disabled="createInboundPending">创建入库单</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
