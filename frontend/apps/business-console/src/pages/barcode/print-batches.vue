<script setup lang="ts">
import type { BusinessConsoleBarcodePrintBatchItem, BusinessConsoleBarcodePrintItemDetail } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useBarcodePrintBatches } from '@/composables/useBusinessBarcode'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  ButtonPro,
  DataTablePro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  FieldPro,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { EyeIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '打印批次', requiredPermissions: ['business.barcodes.templates.manage'] } })

const SOURCE_OPTIONS = [
  { value: 'production.report', label: '生产报工' },
  { value: 'wms.receiving', label: '仓储收货' },
  { value: 'inventory.receipt', label: '库存入库' },
  { value: 'inventory.issue', label: '库存出库' },
  { value: 'inventory.count', label: '库存盘点' },
  { value: 'quality.inspection', label: '质量检验' },
  { value: 'work-order', label: '生产工单' },
]

const STATUS_OPTIONS = [
  { value: 'requested', label: '已请求' },
  { value: 'completed', label: '已完成' },
  { value: 'failed', label: '失败' },
]

const route = useRoute()
const {
  createPrintBatch,
  createPrintBatchPending,
  filters,
  printBatchDetail,
  printBatchDetailError,
  printBatchDetailPending,
  printBatches,
  printBatchesError,
  printBatchesPending,
  printBatchesTotal,
  refreshPrintBatches,
} = useBarcodePrintBatches()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.sourceDocumentType, () => filters.sourceDocumentId, () => filters.status],
})

const open = shallowRef(false)
const showErrors = shallowRef(false)
const sourceFilter = shallowRef('all')
const statusFilter = shallowRef('all')
const createIdempotencyKey = shallowRef('')
const form = reactive({
  labelTemplateId: '',
  sourceDocumentType: '',
  sourceDocumentId: '',
  requestedQuantity: '1',
})

const batchColumns: DataTableProColumn<BusinessConsoleBarcodePrintBatchItem>[] = [
  { key: 'printBatchId', header: '批次', cellClass: 'font-medium', accessor: (r) => r.printBatchId ?? '无' },
  { key: 'source', header: '业务对象' },
  { key: 'labelTemplateId', header: '标签模板', accessor: (r) => r.labelTemplateId ?? '无' },
  { key: 'requestedQuantity', header: '数量', align: 'end', width: 'w-20', accessor: (r) => formatQuantity(r.requestedQuantity) },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'createdAtUtc', header: '创建时间', accessor: (r) => formatDateTime(r.createdAtUtc) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-32' },
]

const itemColumns: DataTableProColumn<BusinessConsoleBarcodePrintItemDetail>[] = [
  { key: 'sequenceNo', header: '序号', width: 'w-20', accessor: (r) => String(r.sequenceNo ?? '无') },
  { key: 'labelValue', header: '标签内容', cellClass: 'font-mono text-xs', accessor: (r) => r.labelValue ?? '无' },
  { key: 'fileId', header: '标签文件', accessor: (r) => r.fileId ?? '未生成文件' },
]

watch(
  () => route.query,
  (query) => {
    const sourceDocumentType = firstQuery(query.sourceDocumentType)
    const sourceDocumentId = firstQuery(query.sourceDocumentId)
    const printBatchId = firstQuery(query.printBatchId)
    if (sourceDocumentType) {
      filters.sourceDocumentType = sourceDocumentType
      sourceFilter.value = sourceDocumentType
    }
    if (sourceDocumentId) filters.sourceDocumentId = sourceDocumentId
    if (printBatchId) filters.selectedPrintBatchId = printBatchId
  },
  { immediate: true },
)

watch(sourceFilter, (value) => {
  filters.sourceDocumentType = value === 'all' ? undefined : value
})

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const listErrorMessage = computed(() => formatError(printBatchesError.value))
const detailErrorMessage = computed(() => formatError(printBatchDetailError.value))
const selectedItems = computed(() => printBatchDetail.value?.items ?? [])
const canCreate = computed(() =>
  form.labelTemplateId.trim().length > 0
  && form.sourceDocumentType.trim().length > 0
  && form.sourceDocumentId.trim().length > 0
  && Number(form.requestedQuantity) > 0,
)

function openCreate() {
  const sourceDocumentId = filters.sourceDocumentId ?? 'manual'
  Object.assign(form, {
    labelTemplateId: '',
    sourceDocumentType: filters.sourceDocumentType ?? '',
    sourceDocumentId: sourceDocumentId === 'manual' ? '' : sourceDocumentId,
    requestedQuantity: '1',
  })
  createIdempotencyKey.value = newPrintBatchIdempotencyKey(sourceDocumentId)
  showErrors.value = false
  open.value = true
}

function selectBatch(row: BusinessConsoleBarcodePrintBatchItem) {
  if (row.printBatchId) filters.selectedPrintBatchId = row.printBatchId
}

async function submitCreate() {
  if (!canCreate.value) {
    showErrors.value = true
    return
  }
  const sourceDocumentId = form.sourceDocumentId.trim()
  try {
    const response = await createPrintBatch({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      labelTemplateId: form.labelTemplateId.trim(),
      sourceDocumentType: form.sourceDocumentType.trim(),
      sourceDocumentId,
      requestedQuantity: Number(form.requestedQuantity),
      idempotencyKey: createIdempotencyKey.value || newPrintBatchIdempotencyKey(sourceDocumentId),
    })
    const nextId = response?.data?.printBatchId
    if (nextId) filters.selectedPrintBatchId = nextId
    notifySuccess('打印批次已提交。')
    open.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

function scanWorkflowForPrintBatch(sourceDocumentType?: string | null) {
  if (sourceDocumentType === 'work-order') return 'production.report'
  const supported = new Set(['production.report', 'wms.receiving', 'inventory.count', 'quality.inspection', 'inventory.adjustment'])
  return sourceDocumentType && supported.has(sourceDocumentType) ? sourceDocumentType : undefined
}

function scanRecordRoute(batch: BusinessConsoleBarcodePrintBatchItem) {
  return {
    path: '/barcode/scans',
    query: {
      sourceWorkflow: scanWorkflowForPrintBatch(batch.sourceDocumentType),
      sourceDocumentId: batch.sourceDocumentId ?? undefined,
    },
  }
}

function newPrintBatchIdempotencyKey(sourceDocumentId: string) {
  return `print-${sourceDocumentId}-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`
}

function sourceLabel(value?: string | null) {
  if (!value) return '未标注来源'
  return SOURCE_OPTIONS.find((option) => option.value === value)?.label ?? value
}

function statusLabel(value?: string | null) {
  if (!value) return '未知'
  return STATUS_OPTIONS.find((option) => option.value === value)?.label ?? value
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatQuantity(value?: number | null) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 3 }).format(value ?? 0)
}

function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="打印批次" :breadcrumbs="[{ label: '条码标签' }]" :count="`${printBatchesTotal} 个批次`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="printBatchesPending" @click="refreshPrintBatches">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="open">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建打印批次
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>新建打印批次</DialogProTitle>
              <DialogProDescription>提交标签模板和业务对象，生成可追溯的打印批次；本页不做打印机驱动或版面渲染。</DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-4" @submit.prevent="submitCreate">
              <p v-if="showErrors && !canCreate" class="text-sm text-destructive" role="alert">
                请填写标签模板、业务对象类型、业务对象编号，并确保打印数量大于 0。
              </p>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !form.labelTemplateId.trim()">
                  <FieldProLabel for="barcode-print-template">标签模板 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-print-template" v-model="form.labelTemplateId" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !(Number(form.requestedQuantity) > 0)">
                  <FieldProLabel for="barcode-print-quantity">打印数量 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-print-quantity" v-model="form.requestedQuantity" type="number" min="1" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.sourceDocumentType.trim()">
                  <FieldProLabel for="barcode-print-source-type">业务对象类型 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-print-source-type" v-model="form.sourceDocumentType" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.sourceDocumentId.trim()">
                  <FieldProLabel for="barcode-print-source-id">业务对象编号 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-print-source-id" v-model="form.sourceDocumentId" autocomplete="off" />
                </FieldPro>
              </FieldProGroup>
              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="open = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="createPrintBatchPending">
                  <Spinner v-if="createPrintBatchPending" aria-hidden="true" />
                  提交打印
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar :show-search="false">
      <template #filters>
        <SelectPro v-model="sourceFilter">
          <SelectProTrigger class="h-9 w-36" aria-label="业务对象类型"><SelectProValue placeholder="全部对象" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部对象</SelectProItem>
            <SelectProItem v-for="option in SOURCE_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <InputPro v-model="filters.sourceDocumentId" class="h-9 w-40" placeholder="业务对象编号" aria-label="业务对象编号" />
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-28" aria-label="批次状态"><SelectProValue placeholder="全部状态" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部状态</SelectProItem>
            <SelectProItem v-for="option in STATUS_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(24rem,0.8fr)]">
      <DataTablePro
        manual
        :page="page"
        :page-size="pageSize"
        :total-items="printBatchesTotal"
        @update:page="page = $event"
        @update:page-size="(value) => (pageSize = String(value))"
        :columns="batchColumns"
        :rows="printBatches"
        row-key="printBatchId"
        :loading="printBatchesPending"
        empty-message="暂无打印批次。请从工单、收货、盘点或质量检验上下文发起打印。"
        :searchable="false"
        :column-settings="false"
      >
        <template #cell-source="{ row }">
          <div class="grid gap-1">
            <span>{{ sourceLabel(row.sourceDocumentType) }}</span>
            <span class="text-xs text-muted-foreground">{{ row.sourceDocumentId ?? '无业务对象' }}</span>
          </div>
        </template>
        <template #cell-status="{ row }">
          <StatusBadgePro :value="row.status" :label="statusLabel(row.status)" />
        </template>
        <template #cell-actions="{ row }">
          <ButtonPro size="sm" variant="ghost" type="button" :disabled="!row.printBatchId" @click="selectBatch(row)">
            <EyeIcon aria-hidden="true" />
            详情
          </ButtonPro>
        </template>
      </DataTablePro>

      <section class="grid content-start gap-3 rounded-md border bg-card p-4">
        <div class="flex items-start justify-between gap-3">
          <div>
            <h2 class="text-base font-semibold">批次详情</h2>
            <p class="text-sm text-muted-foreground">
              {{ printBatchDetail?.printBatchId ?? '选择左侧批次查看标签明细' }}
            </p>
          </div>
          <ButtonPro v-if="printBatchDetail?.sourceDocumentId" size="sm" variant="outline" as-child>
            <RouterLink :to="scanRecordRoute(printBatchDetail)">
              扫码记录
            </RouterLink>
          </ButtonPro>
        </div>
        <p v-if="detailErrorMessage" class="text-sm text-destructive" role="alert">{{ detailErrorMessage }}</p>
        <div v-if="printBatchDetail" class="grid gap-2 text-sm sm:grid-cols-2">
          <div><span class="text-muted-foreground">业务对象：</span>{{ sourceLabel(printBatchDetail.sourceDocumentType) }} · {{ printBatchDetail.sourceDocumentId ?? '无' }}</div>
          <div><span class="text-muted-foreground">标签模板：</span>{{ printBatchDetail.labelTemplateId ?? '无' }}</div>
          <div><span class="text-muted-foreground">数量：</span>{{ formatQuantity(printBatchDetail.requestedQuantity) }}</div>
          <div><span class="text-muted-foreground">状态：</span>{{ statusLabel(printBatchDetail.status) }}</div>
        </div>
        <DataTablePro
          :columns="itemColumns"
          :rows="selectedItems"
          :loading="printBatchDetailPending"
          :row-key="(row) => row.sequenceNo ?? row.labelValue ?? 'label'"
          empty-message="当前批次未返回标签明细。"
          :searchable="false"
          :column-settings="false"
        />
      </section>
    </div>
  </BusinessLayout>
</template>
