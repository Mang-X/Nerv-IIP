<script setup lang="ts">
import type { BusinessConsoleWmsOutboundOrderItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useWmsOutboundOrders } from '@/composables/useBusinessWms'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Checkbox,
  DataTable,
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
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'

definePage({ meta: { requiresAuth: true, title: '出库发货' } })

const {
  outboundOrders,
  outboundError,
  outboundPending,
  refreshOutbound,
  completeOutbound,
  completeOutboundPending,
  completeOutboundError,
  createOutbound,
  createOutboundPending,
  createOutboundError,
} = useWmsOutboundOrders()

const errorMessage = computed(() => formatError(outboundError.value ?? completeOutboundError.value ?? createOutboundError.value))

const ORG = 'org-001'
const ENV = 'env-dev'
interface OutboundLine {
  skuCode: string
  uomCode: string
  requestedQuantity: string
  pickLocationCode: string
  lotNo: string
}
function emptyLine(): OutboundLine {
  return { skuCode: '', uomCode: '', requestedQuantity: '', pickLocationCode: '', lotNo: '' }
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
  if (!createForm.outboundOrderNo.trim() || !createForm.sourceDocumentType.trim()
    || !createForm.sourceDocumentId.trim() || !createForm.siteCode.trim()) {
    createError.value = '请填写出库单号、来源类型、来源单据与工厂。'
    return
  }
  const lines = createForm.lines
    .filter((l) => l.skuCode.trim())
    .map((l, i) => ({
      lineNo: String(i + 1),
      skuCode: l.skuCode.trim(),
      uomCode: l.uomCode.trim() || undefined,
      requestedQuantity: l.requestedQuantity ? Number(l.requestedQuantity) : undefined,
      pickLocationCode: l.pickLocationCode.trim() || undefined,
      lotNo: l.lotNo.trim() || undefined,
    }))
  if (lines.length === 0) {
    createError.value = '至少填写一行明细（物料必填）。'
    return
  }
  try {
    await createOutbound({
      organizationId: ORG,
      environmentId: ENV,
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
const columns: DataTableColumn<OutboundRow>[] = [
  { key: 'outboundOrderNo', header: '出库单号', cellClass: 'font-medium', accessor: (r) => r.outboundOrderNo ?? '无' },
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
    <PageHeader title="出库发货" :breadcrumbs="[{ label: '仓储作业' }]" :count="`${outboundOrders.length} 张出库单`">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="outboundPending" @click="refreshOutbound">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Button size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建出库单
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
      <template #cell-actions="{ row }">
        <Button
          size="sm"
          type="button"
          variant="outline"
          :aria-label="`完成复核 ${row.outboundOrderNo ?? ''}`"
          :disabled="isCompleted(row) || !row.outboundOrderId"
          @click="openReview(row)"
        >
          完成复核
        </Button>
      </template>
    </DataTable>

    <Dialog v-model:open="reviewOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>出库复核</DialogTitle>
          <DialogDescription>
            对出库单 {{ pendingOrder?.outboundOrderNo ?? '' }} 进行发货前复核。
          </DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitReview">
          <FieldGroup>
            <Field>
              <FieldLabel for="wms-pack-review-no">复核单号</FieldLabel>
              <Input id="wms-pack-review-no" v-model="form.packReviewNo" :aria-invalid="Boolean(formError)" autocomplete="off" />
              <FieldError v-if="formError" :errors="[formError]" />
            </Field>
            <Field orientation="horizontal" class="items-center justify-between rounded-lg border p-3">
              <FieldLabel for="wms-pack-passed">复核通过</FieldLabel>
              <Checkbox id="wms-pack-passed" v-model:checked="form.passed" />
            </Field>
          </FieldGroup>
          <DialogFooter show-close-button>
            <Button type="submit" :disabled="completeOutboundPending">提交复核</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="createOpen">
      <DialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>新建出库单</DialogTitle>
          <DialogDescription>登记出库发货单的来源与明细，提交后进入拣货/复核流程。</DialogDescription>
        </DialogHeader>
        <form class="grid gap-4" @submit.prevent="submitCreate">
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field>
              <FieldLabel for="wms-out-no">出库单号</FieldLabel>
              <Input id="wms-out-no" v-model="createForm.outboundOrderNo" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-out-site">工厂</FieldLabel>
              <Input id="wms-out-site" v-model="createForm.siteCode" autocomplete="off" />
            </Field>
            <Field>
              <FieldLabel for="wms-out-srctype">来源类型</FieldLabel>
              <Input id="wms-out-srctype" v-model="createForm.sourceDocumentType" autocomplete="off" placeholder="如 销售发货" />
            </Field>
            <Field>
              <FieldLabel for="wms-out-srcid">来源单据</FieldLabel>
              <Input id="wms-out-srcid" v-model="createForm.sourceDocumentId" autocomplete="off" />
            </Field>
          </FieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <span class="text-sm font-medium">发货明细</span>
              <Button type="button" size="sm" variant="outline" @click="addLine">
                <PlusIcon aria-hidden="true" />
                添加行
              </Button>
            </div>
            <div v-for="(line, index) in createForm.lines" :key="index" class="flex flex-wrap items-end gap-2 rounded-md border p-2">
              <Input v-model="line.skuCode" class="h-9 w-32" placeholder="物料" :aria-label="`第 ${index + 1} 行物料`" />
              <Input v-model="line.uomCode" class="h-9 w-20" placeholder="单位" :aria-label="`第 ${index + 1} 行单位`" />
              <Input v-model="line.requestedQuantity" class="h-9 w-24" type="number" placeholder="需求数量" :aria-label="`第 ${index + 1} 行需求数量`" />
              <Input v-model="line.pickLocationCode" class="h-9 w-24" placeholder="拣货库位" :aria-label="`第 ${index + 1} 行拣货库位`" />
              <Input v-model="line.lotNo" class="h-9 w-28" placeholder="批次" :aria-label="`第 ${index + 1} 行批次`" />
              <Button type="button" size="icon-sm" variant="ghost" :aria-label="`删除第 ${index + 1} 行`" @click="removeLine(index)">
                <Trash2Icon class="size-4" aria-hidden="true" />
              </Button>
            </div>
          </div>

          <FieldError v-if="createError" :errors="[createError]" />

          <DialogFooter show-close-button>
            <Button type="submit" :disabled="createOutboundPending">创建出库单</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
