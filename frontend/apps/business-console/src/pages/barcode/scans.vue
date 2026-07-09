<script setup lang="ts">
import type { BusinessConsoleBarcodeScanRecordItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBarcodeScans } from '@/composables/useBusinessBarcode'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { BARCODE_SCAN_WORKFLOW_OPTIONS, barcodeScanWorkflowLabel } from './workflow-options'

definePage({
  meta: {
    requiresAuth: true,
    title: '扫码记录',
    requiredPermissions: ['business.barcodes.templates.manage'],
  },
})

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
  resetOn: [
    () => filters.deviceCode,
    () => filters.scannedValue,
    () => filters.sourceWorkflow,
    () => filters.sourceDocumentId,
  ],
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

const columns: NvDataTableColumn<BusinessConsoleBarcodeScanRecordItem>[] = [
  {
    key: 'scannedValue',
    header: '扫码原文',
    cellClass: 'font-mono text-xs',
    accessor: (r) => r.scannedValue ?? '无',
  },
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
const canSubmit = computed(
  () =>
    form.deviceCode.trim().length > 0 &&
    form.scannedValue.trim().length > 0 &&
    form.sourceWorkflow.trim().length > 0 &&
    form.sourceDocumentId.trim().length > 0 &&
    form.result.trim().length > 0 &&
    (form.result === 'accepted' || form.rejectionReason.trim().length > 0),
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
  } catch (error) {
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
  if (normalized === 'accepted' || normalized === 'success' || normalized === 'succeeded')
    return 'success'
  if (normalized === 'rejected') return 'warning'
  if (normalized === 'failed' || normalized === 'error') return 'danger'
  return undefined
}

function rejectionMessage(value?: string | null) {
  const normalized = value?.trim().toLowerCase()
  if (!normalized) return ''
  if (normalized.includes('unsupported'))
    return '该扫码场景暂未接入自动业务动作，请转人工处理或从对应业务页面重试。'
  if (normalized.includes('parse') || normalized.includes('invalid'))
    return '条码解析失败，请核对标签内容、规则和扫描方式。'
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
    <NvPageHeader
      title="扫码记录"
      :breadcrumbs="[{ label: '条码标签' }]"
      :count="`${scansTotal} 条记录`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="scansPending"
          @click="refreshScans"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="open">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openRecordDialog">
              <PlusIcon aria-hidden="true" />
              补录扫码审计
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>补录扫码审计</NvDialogTitle>
              <NvDialogDescription
                >记录 PC 端可见的扫码审计事实；PDA
                扫码界面和离线能力不在本页实现。</NvDialogDescription
              >
            </NvDialogHeader>
            <form class="grid gap-4" @submit.prevent="submitScan">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写终端、扫码原文、动作来源、业务对象；失败或拒绝时必须填写原因。
              </p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField :data-invalid="showErrors && !form.deviceCode.trim()">
                  <NvFieldLabel for="barcode-scan-device"
                    >设备/终端 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="barcode-scan-device" v-model="form.deviceCode" autocomplete="off" />
                </NvField>
                <NvField>
                  <NvFieldLabel>扫码结果 <span class="text-destructive">*</span></NvFieldLabel>
                  <NvSelect v-model="form.result" aria-label="扫码结果">
                    <NvSelectTrigger aria-label="扫码结果"><NvSelectValue /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in RESULT_OPTIONS"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField
                  class="sm:col-span-2"
                  :data-invalid="showErrors && !form.scannedValue.trim()"
                >
                  <NvFieldLabel for="barcode-scan-value"
                    >扫码原文 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="barcode-scan-value" v-model="form.scannedValue" autocomplete="off" />
                </NvField>
                <NvField :data-invalid="showErrors && !form.sourceWorkflow.trim()">
                  <NvFieldLabel for="barcode-scan-workflow"
                    >动作来源 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-scan-workflow"
                    v-model="form.sourceWorkflow"
                    autocomplete="off"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !form.sourceDocumentId.trim()">
                  <NvFieldLabel for="barcode-scan-source-id"
                    >业务对象 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-scan-source-id"
                    v-model="form.sourceDocumentId"
                    autocomplete="off"
                  />
                </NvField>
                <NvField
                  class="sm:col-span-2"
                  :data-invalid="
                    showErrors && form.result !== 'accepted' && !form.rejectionReason.trim()
                  "
                >
                  <NvFieldLabel for="barcode-scan-reason">失败/拒绝原因</NvFieldLabel>
                  <NvInput
                    id="barcode-scan-reason"
                    v-model="form.rejectionReason"
                    autocomplete="off"
                  />
                </NvField>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
                <NvButton type="submit" :disabled="recordScanPending">
                  <Spinner v-if="recordScanPending" aria-hidden="true" />
                  记录审计
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="filters.scannedValue" search-placeholder="按扫码原文筛选">
      <template #filters>
        <NvSelect v-model="workflowFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="动作来源"
            ><NvSelectValue placeholder="全部来源"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部来源</NvSelectItem>
            <NvSelectItem
              v-for="option in WORKFLOW_OPTIONS"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvInput
          v-model="filters.sourceDocumentId"
          class="h-9 w-40"
          placeholder="业务对象"
          aria-label="业务对象"
        />
        <NvInput
          v-model="filters.deviceCode"
          class="h-9 w-32"
          placeholder="终端"
          aria-label="终端"
        />
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
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
          <NvStatusBadge
            :value="row.result"
            :label="resultLabel(row.result)"
            :tone="resultTone(row.result)"
          />
          <span v-if="rejectionMessage(row.rejectionReason)" class="text-xs text-muted-foreground">
            {{ rejectionMessage(row.rejectionReason) }}
          </span>
        </div>
      </template>
    </NvDataTable>
  </BusinessLayout>
</template>
