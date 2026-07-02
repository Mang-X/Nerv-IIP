<script setup lang="ts">
import type { BusinessConsoleBarcodeScanRecordItem } from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useBarcodeScans } from '@/composables/useBusinessBarcode'
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
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { BARCODE_SCAN_WORKFLOW_OPTIONS, barcodeScanWorkflowLabel } from './workflow-options'

definePage({ meta: { requiresAuth: true, title: '扫码记录', requiredPermissions: ['business.barcodes.templates.manage'] } })

const WORKFLOW_OPTIONS = BARCODE_SCAN_WORKFLOW_OPTIONS

const RESULT_OPTIONS = [
  { value: 'accepted', label: '已接受' },
  { value: 'rejected', label: '已拒绝' },
  { value: 'failed', label: '失败' },
]

const route = useRoute()
const {
  filters,
  recordScan,
  recordScanPending,
  refreshScans,
  scans,
  scansError,
  scansPending,
  scansTotal,
} = useBarcodeScans()
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.deviceCode, () => filters.scannedValue, () => filters.sourceWorkflow, () => filters.sourceDocumentId],
})

const open = shallowRef(false)
const showErrors = shallowRef(false)
const workflowFilter = shallowRef('all')
const form = reactive({
  deviceCode: '',
  scannedValue: '',
  sourceWorkflow: '',
  sourceDocumentId: '',
  result: 'accepted',
  rejectionReason: '',
})

const columns: DataTableProColumn<BusinessConsoleBarcodeScanRecordItem>[] = [
  { key: 'scannedValue', header: '扫码原文', cellClass: 'font-mono text-xs', accessor: (r) => r.scannedValue ?? '无' },
  { key: 'parsed', header: '解析结果' },
  { key: 'sourceWorkflow', header: '动作来源', width: 'w-36' },
  { key: 'sourceDocumentId', header: '业务对象', accessor: (r) => r.sourceDocumentId ?? '无' },
  { key: 'deviceCode', header: '设备/终端', width: 'w-32', accessor: (r) => r.deviceCode ?? '无' },
  { key: 'result', header: '状态', width: 'w-28' },
  { key: 'scannedAtUtc', header: '时间', accessor: (r) => formatDateTime(r.scannedAtUtc) },
]

watch(
  () => route.query,
  (query) => {
    const sourceWorkflow = firstQuery(query.sourceWorkflow)
    const sourceDocumentId = firstQuery(query.sourceDocumentId)
    const scannedValue = firstQuery(query.scannedValue)
    const deviceCode = firstQuery(query.deviceCode)
    if (sourceWorkflow) {
      filters.sourceWorkflow = sourceWorkflow
      workflowFilter.value = sourceWorkflow
    }
    if (sourceDocumentId) filters.sourceDocumentId = sourceDocumentId
    if (scannedValue) filters.scannedValue = scannedValue
    if (deviceCode) filters.deviceCode = deviceCode
  },
  { immediate: true },
)

watch(workflowFilter, (value) => {
  filters.sourceWorkflow = value === 'all' ? undefined : value
})

const errorMessage = computed(() => formatError(scansError.value))
const canSubmit = computed(() =>
  form.deviceCode.trim().length > 0
  && form.scannedValue.trim().length > 0
  && form.sourceWorkflow.trim().length > 0
  && form.sourceDocumentId.trim().length > 0
  && form.result.trim().length > 0
  && (form.result === 'accepted' || form.rejectionReason.trim().length > 0),
)

function openRecordDialog() {
  Object.assign(form, {
    deviceCode: filters.deviceCode ?? '',
    scannedValue: filters.scannedValue ?? '',
    sourceWorkflow: filters.sourceWorkflow ?? '',
    sourceDocumentId: filters.sourceDocumentId ?? '',
    result: 'accepted',
    rejectionReason: '',
  })
  showErrors.value = false
  open.value = true
}

async function submitScan() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const sourceDocumentId = form.sourceDocumentId.trim()
  try {
    await recordScan({
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      deviceCode: form.deviceCode.trim(),
      scannedValue: form.scannedValue.trim(),
      sourceWorkflow: form.sourceWorkflow.trim(),
      sourceDocumentId,
      result: form.result.trim(),
      rejectionReason: form.result === 'accepted' ? undefined : form.rejectionReason.trim(),
      idempotencyKey: `scan-${sourceDocumentId}-${Date.now()}`,
    })
    notifySuccess('扫码审计已记录。')
    open.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

function workflowLabel(value?: string | null) {
  return barcodeScanWorkflowLabel(value)
}

function resultLabel(value?: string | null) {
  if (!value) return '未知'
  return RESULT_OPTIONS.find((option) => option.value === value)?.label ?? value
}

function resultTone(value?: string | null) {
  const normalized = value?.toLowerCase()
  if (normalized === 'accepted' || normalized === 'success' || normalized === 'succeeded') return 'success'
  if (normalized === 'rejected') return 'warning'
  if (normalized === 'failed' || normalized === 'error') return 'danger'
  return undefined
}

function rejectionMessage(value?: string | null) {
  const normalized = value?.trim().toLowerCase()
  if (!normalized) return ''
  if (normalized.includes('unsupported')) return '该扫码场景暂未接入自动业务动作，请转人工处理或从对应业务页面重试。'
  if (normalized.includes('parse') || normalized.includes('invalid')) return '条码解析失败，请核对标签内容、规则和扫描方式。'
  if (normalized.includes('duplicate')) return '该条码动作已被记录，请检查是否重复扫码。'
  return value ?? ''
}

function parsedBarcodeSummary(value?: string | null) {
  const raw = value?.trim()
  if (!raw) return '无扫码内容'
  const parts: string[] = []
  const gtin = /\(01\)([^()]+)/.exec(raw)?.[1]
  const lot = /\(10\)([^()]+)/.exec(raw)?.[1]
  const serial = /\(21\)([^()]+)/.exec(raw)?.[1]
  const expiry = /\(17\)([^()]+)/.exec(raw)?.[1]
  const sscc = /\(00\)([^()]+)/.exec(raw)?.[1]
  if (gtin) parts.push(`GTIN ${gtin}`)
  if (lot) parts.push(`批次 ${lot}`)
  if (serial) parts.push(`序列号 ${serial}`)
  if (expiry) parts.push(`有效期 ${expiry}`)
  if (sscc) parts.push(`SSCC ${sscc}`)
  return parts.length ? parts.join(' / ') : '无法从原文识别标准字段'
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
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
    <PageHeader title="扫码记录" :breadcrumbs="[{ label: '条码标签' }]" :count="`${scansTotal} 条记录`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="scansPending" @click="refreshScans">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="open">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openRecordDialog">
              <PlusIcon aria-hidden="true" />
              补录扫码审计
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>补录扫码审计</DialogProTitle>
              <DialogProDescription>记录 PC 端可见的扫码审计事实；PDA 扫码界面和离线能力不在本页实现。</DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-4" @submit.prevent="submitScan">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写终端、扫码原文、动作来源、业务对象；失败或拒绝时必须填写原因。
              </p>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !form.deviceCode.trim()">
                  <FieldProLabel for="barcode-scan-device">设备/终端 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-scan-device" v-model="form.deviceCode" autocomplete="off" />
                </FieldPro>
                <FieldPro>
                  <FieldProLabel>扫码结果 <span class="text-destructive">*</span></FieldProLabel>
                  <SelectPro v-model="form.result" aria-label="扫码结果">
                    <SelectProTrigger aria-label="扫码结果"><SelectProValue /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="option in RESULT_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                </FieldPro>
                <FieldPro class="sm:col-span-2" :data-invalid="showErrors && !form.scannedValue.trim()">
                  <FieldProLabel for="barcode-scan-value">扫码原文 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-scan-value" v-model="form.scannedValue" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.sourceWorkflow.trim()">
                  <FieldProLabel for="barcode-scan-workflow">动作来源 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-scan-workflow" v-model="form.sourceWorkflow" autocomplete="off" />
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !form.sourceDocumentId.trim()">
                  <FieldProLabel for="barcode-scan-source-id">业务对象 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="barcode-scan-source-id" v-model="form.sourceDocumentId" autocomplete="off" />
                </FieldPro>
                <FieldPro class="sm:col-span-2" :data-invalid="showErrors && form.result !== 'accepted' && !form.rejectionReason.trim()">
                  <FieldProLabel for="barcode-scan-reason">失败/拒绝原因</FieldProLabel>
                  <InputPro id="barcode-scan-reason" v-model="form.rejectionReason" autocomplete="off" />
                </FieldPro>
              </FieldProGroup>
              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="open = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="recordScanPending">
                  <Spinner v-if="recordScanPending" aria-hidden="true" />
                  记录审计
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="filters.scannedValue" search-placeholder="按扫码原文筛选">
      <template #filters>
        <SelectPro v-model="workflowFilter">
          <SelectProTrigger class="h-9 w-36" aria-label="动作来源"><SelectProValue placeholder="全部来源" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部来源</SelectProItem>
            <SelectProItem v-for="option in WORKFLOW_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <InputPro v-model="filters.sourceDocumentId" class="h-9 w-40" placeholder="业务对象" aria-label="业务对象" />
        <InputPro v-model="filters.deviceCode" class="h-9 w-32" placeholder="终端" aria-label="终端" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="scansTotal"
      @update:page="page = $event"
      @update:page-size="(value) => (pageSize = String(value))"
      :columns="columns"
      :rows="scans"
      row-key="scanRecordId"
      :loading="scansPending"
      empty-message="暂无扫码记录。从 MES、WMS、库存或质量上下文进入时会自动带入业务对象筛选。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-parsed="{ row }">
        <span class="text-sm">{{ parsedBarcodeSummary(row.scannedValue) }}</span>
      </template>
      <template #cell-sourceWorkflow="{ row }">
        {{ workflowLabel(row.sourceWorkflow) }}
      </template>
      <template #cell-result="{ row }">
        <div class="grid gap-1">
          <StatusBadgePro :value="row.result" :label="resultLabel(row.result)" :tone="resultTone(row.result)" />
          <span v-if="rejectionMessage(row.rejectionReason)" class="text-xs text-muted-foreground">
            {{ rejectionMessage(row.rejectionReason) }}
          </span>
        </div>
      </template>
    </DataTablePro>
  </BusinessLayout>
</template>
