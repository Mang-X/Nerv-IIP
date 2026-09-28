<script setup lang="ts">
import type {
  BusinessConsoleBarcodePrintBatchItem,
  BusinessConsoleBarcodePrintItemDetail,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import SourceDocumentPicker from '@/components/business/SourceDocumentPicker.vue'
import { parseVariableRows, rowDisplayLabel } from '@/components/barcode/labelTemplateVariables'
import {
  useBarcodePrintBatches,
  useBarcodeRules,
  useBarcodeTemplates,
} from '@/composables/useBusinessBarcode'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
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
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvFieldLegend,
  NvFieldSet,
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
import { EyeIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute } from 'vue-router'
import {
  barcodeSourceDocumentKind,
  barcodeSourceDocumentRoute,
  isBarcodeScanWorkflow,
} from './workflow-options'

definePage({
  meta: {
    requiresAuth: true,
    title: '打印批次',
    requiredPermissions: ['business.barcodes.templates.manage'],
  },
})

const SOURCE_OPTIONS = [
  { value: 'production.report', label: '生产报工' },
  { value: 'purchase-receipt', label: '采购收货' },
  { value: 'wms.receiving', label: '仓储收货' },
  { value: 'inventory.receipt', label: '库存入库' },
  { value: 'inventory.issue', label: '库存出库' },
  { value: 'inventory.count', label: '库存盘点' },
  { value: 'quality.inspection', label: '质量检验' },
  { value: 'work-order', label: '生产工单' },
]

// 与 BarcodeLabel 打印批次聚合（LabelPrintBatch）的状态码一一对应；tone 只借用状态徽标的色调键。
const STATUS_OPTIONS = [
  { value: 'pending', label: '待处理', tone: 'pending' },
  { value: 'reserved', label: '已预留', tone: 'pending' },
  { value: 'ready-to-print', label: '待打印', tone: 'queued' },
  { value: 'sent-to-printer', label: '已发送打印机', tone: 'dispatched' },
  { value: 'delivery-unknown', label: '送达待核实', tone: 'held' },
  { value: 'printed', label: '已打印', tone: 'completed' },
  { value: 'failed', label: '打印失败', tone: 'failed' },
]

// 这两个数据项由表单其它字段或条码规则决定，不让用户在取值区重复填写。
const SOURCE_DOCUMENT_VARIABLE = 'sourceDocumentId'
const LOT_NO_VARIABLE = 'lotNo'

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
  barcodeRuleId: '',
  sourceDocumentType: '',
  sourceDocumentId: '',
  requestedQuantity: '1',
})
// 按模板数据项逐项填写的标签取值，键是数据项（变量）名，只在组装请求时出现。
const labelValues = shallowRef<Record<string, string>>({})

// 标签模板绑定的是模板主键（GUID），没人能手输——一律从模板目录里选。选择器只展示模板名与编码，
// 主键不上屏（选择器上关掉编码位，否则它会拿主键当编码显示）。
// 打印只接受启用的模板和规则（服务端按启用状态查找），停用的不进候选。
const { templates, templatesPending } = useBarcodeTemplates({ status: 'active', take: 200 })
const templateOptions = computed(() =>
  templates.value
    .filter((template) => !!template.templateId && template.status === 'active')
    .map((template) => ({
      value: template.templateId as string,
      label: template.templateName || template.templateCode || '未命名模板',
      hint: template.templateCode ?? undefined,
    })),
)
const selectedTemplate = computed(() =>
  templates.value.find((template) => template.templateId === form.labelTemplateId),
)
// 选中模板带出的数据项；来源单号由「业务对象编号」自动带入，不单独填写。
const templateVariables = computed(() =>
  parseVariableRows(selectedTemplate.value?.variableSchemaJson),
)
const valueVariables = computed(() =>
  templateVariables.value.filter((variable) => variable.name !== SOURCE_DOCUMENT_VARIABLE),
)

// 条码规则：只列启用、且允许当前业务对象类型的规则。
const { rules, rulesPending } = useBarcodeRules({ status: 'active', take: 200 })
const BARCODE_TYPE_LABELS: Record<string, string> = {
  code128: 'Code 128',
  'gs1-128': 'GS1-128',
  datamatrix: 'Data Matrix',
  qr: 'QR Code',
}
const ruleOptions = computed(() =>
  rules.value
    .filter(
      (rule) =>
        !!rule.barcodeRuleId &&
        rule.status === 'active' &&
        !!form.sourceDocumentType &&
        (rule.allowedSourceDocumentTypes ?? []).includes(form.sourceDocumentType),
    )
    .map((rule) => ({
      value: rule.barcodeRuleId as string,
      label: rule.ruleCode || '未命名规则',
      hint: rule.barcodeType
        ? (BARCODE_TYPE_LABELS[rule.barcodeType] ?? rule.barcodeType)
        : undefined,
    })),
)
const selectedRule = computed(() =>
  rules.value.find((rule) => rule.barcodeRuleId === form.barcodeRuleId),
)
// GS1 条码的批次号取自标签取值里的「批次号」（lotNo），所以模板必须带这一数据项且必填。
const ruleNeedsLotNo = computed(() =>
  (selectedRule.value?.barcodeType ?? '').toLowerCase().startsWith('gs1-'),
)
const templateHasLotNo = computed(() =>
  templateVariables.value.some((variable) => variable.name === LOT_NO_VARIABLE),
)

const batchColumns: NvDataTableColumn<BusinessConsoleBarcodePrintBatchItem>[] = [
  // 列宽显式给定、长文本列允许换行：单号与条码值较长，自动列宽会让相邻两列文字叠在一起。
  {
    key: 'sourceDocumentId',
    header: '来源单据',
    width: 'w-44',
    cellClass: 'font-medium whitespace-normal break-all',
    accessor: (r) => r.sourceDocumentId ?? '未关联单据',
  },
  { key: 'source', header: '业务来源', width: 'w-20', cellClass: 'whitespace-normal' },
  {
    key: 'requestedQuantity',
    header: '数量',
    align: 'end',
    width: 'w-16',
    accessor: (r) => formatQuantity(r.requestedQuantity),
  },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'createdAtUtc',
    header: '创建时间',
    width: 'w-32',
    cellClass: 'whitespace-normal',
    accessor: (r) => formatDateTime(r.createdAtUtc),
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

const itemColumns: NvDataTableColumn<BusinessConsoleBarcodePrintItemDetail>[] = [
  {
    key: 'sequenceNo',
    header: '序号',
    width: 'w-14',
    accessor: (r) => String(r.sequenceNo ?? '无'),
  },
  {
    key: 'labelValue',
    header: '标签内容',
    cellClass: 'font-mono text-xs whitespace-normal break-all',
    accessor: (r) => r.labelValue ?? '无',
  },
  {
    key: 'fileId',
    header: '标签文件',
    width: 'w-20',
    accessor: (r) => (r.fileId ? '已生成' : '未生成'),
  },
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

const listErrorMessage = computed(() => inlineErrorMessage(printBatchesError.value))
const detailErrorMessage = computed(() => inlineErrorMessage(printBatchDetailError.value))
const selectedItems = computed(() => printBatchDetail.value?.items ?? [])
function isValueRequired(name: string, required: boolean) {
  return required || (ruleNeedsLotNo.value && name === LOT_NO_VARIABLE)
}
function valueError(variable: { name: string; required: boolean; maxLength: string }) {
  const value = (labelValues.value[variable.name] ?? '').trim()
  if (!value && isValueRequired(variable.name, variable.required)) return '请填写'
  const maxLength = Number(variable.maxLength)
  if (Number.isInteger(maxLength) && maxLength > 0 && value.length > maxLength) {
    return `不超过 ${maxLength} 个字`
  }
  return ''
}
const lotNoMissingFromTemplate = computed(
  () => ruleNeedsLotNo.value && !!selectedTemplate.value && !templateHasLotNo.value,
)
const labelValuesValid = computed(
  () => !lotNoMissingFromTemplate.value && valueVariables.value.every((v) => !valueError(v)),
)
const canCreate = computed(
  () =>
    form.labelTemplateId.trim().length > 0 &&
    form.barcodeRuleId.trim().length > 0 &&
    form.sourceDocumentType.trim().length > 0 &&
    form.sourceDocumentId.trim().length > 0 &&
    Number(form.requestedQuantity) > 0 &&
    labelValuesValid.value,
)

// 换模板：取值按新模板的数据项重来。
watch(
  () => form.labelTemplateId,
  () => {
    labelValues.value = {}
  },
)
// 规则候选随业务对象类型变化：已选规则不再适用就清掉；只有一条可用时直接选上。
watch(
  ruleOptions,
  (options) => {
    if (form.barcodeRuleId && !options.some((option) => option.value === form.barcodeRuleId)) {
      form.barcodeRuleId = ''
    }
    if (!form.barcodeRuleId && options.length === 1) form.barcodeRuleId = options[0]!.value
  },
  { immediate: true },
)

function setLabelValue(name: string, value: unknown) {
  labelValues.value = { ...labelValues.value, [name]: String(value ?? '') }
}

// 只送模板声明过的数据项：服务端拒收未声明的键，空的选填项不送。
function buildLabelValuesJson(sourceDocumentId: string) {
  const values: Record<string, string> = {}
  for (const variable of templateVariables.value) {
    const value =
      variable.name === SOURCE_DOCUMENT_VARIABLE
        ? sourceDocumentId
        : (labelValues.value[variable.name] ?? '').trim()
    if (value) values[variable.name] = value
  }
  return JSON.stringify(values)
}

function openCreate() {
  const sourceDocumentId = filters.sourceDocumentId ?? 'manual'
  Object.assign(form, {
    labelTemplateId: '',
    barcodeRuleId: '',
    sourceDocumentType: filters.sourceDocumentType ?? '',
    sourceDocumentId: sourceDocumentId === 'manual' ? '' : sourceDocumentId,
    requestedQuantity: '1',
  })
  labelValues.value = {}
  createIdempotencyKey.value = newPrintBatchIdempotencyKey(sourceDocumentId)
  showErrors.value = false
  open.value = true
}

// 换了类型，已选的单据就不属于这一类了。
function changeSourceDocumentType(value: unknown) {
  form.sourceDocumentType = typeof value === 'string' ? value : ''
  form.sourceDocumentId = ''
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
      barcodeRuleId: form.barcodeRuleId.trim(),
      labelTemplateId: form.labelTemplateId.trim(),
      sourceDocumentType: form.sourceDocumentType.trim(),
      sourceDocumentId,
      labelValuesJson: buildLabelValuesJson(sourceDocumentId),
      requestedQuantity: Number(form.requestedQuantity),
      idempotencyKey: createIdempotencyKey.value || newPrintBatchIdempotencyKey(sourceDocumentId),
    })
    const nextId = response?.data?.printBatchId
    if (nextId) filters.selectedPrintBatchId = nextId
    notifySuccess('打印批次已提交。')
    open.value = false
  } catch (error) {
    notifyOperationFailure('提交打印批次失败', error, '提交打印批次失败，请稍后重试。')
  }
}

function scanWorkflowForPrintBatch(sourceDocumentType?: string | null) {
  if (sourceDocumentType === 'work-order') return 'production.report'
  return isBarcodeScanWorkflow(sourceDocumentType) ? sourceDocumentType : undefined
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
  return SOURCE_OPTIONS.find((option) => option.value === value)?.label ?? '其他业务对象'
}

function statusLabel(value?: string | null) {
  if (!value) return '未知'
  return STATUS_OPTIONS.find((option) => option.value === value)?.label ?? '其他状态'
}

function statusTone(value?: string | null) {
  return STATUS_OPTIONS.find((option) => option.value === value)?.tone ?? 'pending'
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
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="打印批次"
      :breadcrumbs="[{ label: '条码标签' }]"
      :count="`${printBatchesTotal} 个批次`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="printBatchesPending"
          @click="refreshPrintBatches"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="open">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建打印批次
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>新建打印批次</NvDialogTitle>
              <!-- 说明不上界面：仅供读屏播报。 -->
              <NvDialogDescription class="sr-only"
                >按标签模板与业务对象提交打印批次。</NvDialogDescription
              >
            </NvDialogHeader>
            <form class="grid gap-4" @submit.prevent="submitCreate">
              <p v-if="showErrors && !canCreate" class="text-sm text-destructive" role="alert">
                请选择标签模板、业务对象和条码规则，补全标签取值，并确保打印数量大于 0。
              </p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField :data-invalid="showErrors && !form.labelTemplateId.trim()">
                  <NvFieldLabel for="barcode-print-template"
                    >标签模板 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvEntityPicker
                    id="barcode-print-template"
                    v-model="form.labelTemplateId"
                    :options="templateOptions"
                    :show-code="false"
                    title="选择标签模板"
                    placeholder="选择标签模板"
                    empty-text="暂无标签模板，请先在标签模板维护"
                    :loading="templatesPending"
                    aria-label="标签模板"
                    clearable
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !(Number(form.requestedQuantity) > 0)">
                  <NvFieldLabel for="barcode-print-quantity"
                    >打印数量 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput
                    id="barcode-print-quantity"
                    v-model="form.requestedQuantity"
                    type="number"
                    min="1"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !form.sourceDocumentType.trim()">
                  <NvFieldLabel for="barcode-print-source-type"
                    >业务对象类型 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvSelect
                    :model-value="form.sourceDocumentType"
                    @update:model-value="changeSourceDocumentType"
                  >
                    <NvSelectTrigger id="barcode-print-source-type">
                      <NvSelectValue placeholder="选择业务对象类型" />
                    </NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in SOURCE_OPTIONS"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField :data-invalid="showErrors && !form.sourceDocumentId.trim()">
                  <NvFieldLabel for="barcode-print-source-id"
                    >业务对象编号 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <SourceDocumentPicker
                    id="barcode-print-source-id"
                    v-model="form.sourceDocumentId"
                    :kind="barcodeSourceDocumentKind(form.sourceDocumentType)"
                    :invalid="showErrors && !form.sourceDocumentId.trim()"
                  />
                </NvField>
                <NvField :data-invalid="showErrors && !form.barcodeRuleId.trim()">
                  <NvFieldLabel for="barcode-print-rule"
                    >条码规则 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvEntityPicker
                    id="barcode-print-rule"
                    v-model="form.barcodeRuleId"
                    :options="ruleOptions"
                    :show-code="false"
                    title="选择条码规则"
                    :placeholder="form.sourceDocumentType ? '选择条码规则' : '请先选择业务对象类型'"
                    empty-text="暂无适用于该业务对象的启用规则，请先在条码规则维护"
                    :loading="rulesPending"
                    :disabled="!form.sourceDocumentType"
                    aria-label="条码规则"
                    clearable
                  />
                </NvField>
                <NvFieldSet v-if="selectedTemplate" class="gap-3 sm:col-span-2">
                  <NvFieldLegend variant="label">标签取值</NvFieldLegend>
                  <p v-if="lotNoMissingFromTemplate" class="text-sm text-destructive" role="alert">
                    所选条码规则是 GS1
                    条码，需要「批次号」，但这个模板没有「批次号」数据项。请换一个模板，或先在标签模板里加上「批次号」。
                  </p>
                  <p v-else-if="valueVariables.length === 0" class="text-sm text-muted-foreground">
                    这个模板的内容都由系统自动带出，不需要填写。
                  </p>
                  <div v-else class="grid gap-3 sm:grid-cols-2">
                    <NvField
                      v-for="variable in valueVariables"
                      :key="variable.name"
                      :data-invalid="showErrors && !!valueError(variable)"
                    >
                      <NvFieldLabel :for="`barcode-print-value-${variable.name}`"
                        >{{ rowDisplayLabel(variable.name, variable.label) }}
                        <span
                          v-if="isValueRequired(variable.name, variable.required)"
                          class="text-destructive"
                          >*</span
                        ></NvFieldLabel
                      >
                      <NvInput
                        :id="`barcode-print-value-${variable.name}`"
                        :model-value="labelValues[variable.name] ?? ''"
                        autocomplete="off"
                        @update:model-value="(value) => setLabelValue(variable.name, value)"
                      />
                      <NvFieldDescription v-if="showErrors && valueError(variable)">
                        <span class="text-destructive">{{ valueError(variable) }}</span>
                      </NvFieldDescription>
                    </NvField>
                  </div>
                </NvFieldSet>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
                <NvButton type="submit" :disabled="createPrintBatchPending">
                  <Spinner v-if="createPrintBatchPending" aria-hidden="true" />
                  提交打印
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvSelect v-model="sourceFilter">
          <NvSelectTrigger class="h-9 w-36" aria-label="业务对象类型"
            ><NvSelectValue placeholder="全部对象"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部对象</NvSelectItem>
            <NvSelectItem
              v-for="option in SOURCE_OPTIONS"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvInput
          v-model="filters.sourceDocumentId"
          class="h-9 w-40"
          placeholder="业务对象编号"
          aria-label="业务对象编号"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-28" aria-label="批次状态"
            ><NvSelectValue placeholder="全部状态"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部状态</NvSelectItem>
            <NvSelectItem
              v-for="option in STATUS_OPTIONS"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <div class="grid gap-4 xl:grid-cols-[minmax(0,1.2fr)_minmax(24rem,0.8fr)]">
      <NvDataTable
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
        <template #cell-sourceDocumentId="{ row }">
          <RouterLink
            v-if="barcodeSourceDocumentRoute(row.sourceDocumentType, row.sourceDocumentId)"
            class="underline underline-offset-2"
            :to="barcodeSourceDocumentRoute(row.sourceDocumentType, row.sourceDocumentId)!"
          >
            {{ row.sourceDocumentId }}
          </RouterLink>
          <span v-else>{{ row.sourceDocumentId ?? '未关联单据' }}</span>
        </template>
        <template #cell-source="{ row }">
          {{ sourceLabel(row.sourceDocumentType) }}
        </template>
        <template #cell-status="{ row }">
          <NvStatusBadge :value="statusTone(row.status)" :label="statusLabel(row.status)" />
        </template>
        <template #cell-actions="{ row }">
          <NvButton
            size="sm"
            variant="ghost"
            type="button"
            :disabled="!row.printBatchId"
            @click="selectBatch(row)"
          >
            <EyeIcon aria-hidden="true" />
            详情
          </NvButton>
        </template>
      </NvDataTable>

      <section class="grid content-start gap-3 rounded-md border bg-card p-4">
        <div class="flex items-start justify-between gap-3">
          <div>
            <h2 class="text-base font-semibold">批次详情</h2>
            <p class="text-sm text-muted-foreground">
              {{ printBatchDetail?.sourceDocumentId ?? '选择左侧任务查看标签明细' }}
            </p>
          </div>
          <NvButton v-if="printBatchDetail?.sourceDocumentId" size="sm" variant="outline" as-child>
            <RouterLink :to="scanRecordRoute(printBatchDetail)"> 扫码记录 </RouterLink>
          </NvButton>
        </div>
        <p v-if="detailErrorMessage" class="text-sm text-destructive" role="alert">
          {{ detailErrorMessage }}
        </p>
        <div v-if="printBatchDetail" class="grid gap-2 text-sm sm:grid-cols-2">
          <div>
            <span class="text-muted-foreground">业务对象：</span
            >{{ sourceLabel(printBatchDetail.sourceDocumentType) }} ·
            {{ printBatchDetail.sourceDocumentId ?? '无' }}
          </div>
          <div><span class="text-muted-foreground">标签模板：</span>已匹配</div>
          <div>
            <span class="text-muted-foreground">数量：</span
            >{{ formatQuantity(printBatchDetail.requestedQuantity) }}
          </div>
          <div>
            <span class="text-muted-foreground">状态：</span
            >{{ statusLabel(printBatchDetail.status) }}
          </div>
        </div>
        <NvDataTable
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
