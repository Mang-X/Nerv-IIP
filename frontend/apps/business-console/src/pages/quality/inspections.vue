<script setup lang="ts">
import type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleInspectionCharacteristicResult,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import type { ComboboxSuggestion, NvDataTableColumn, SearchSelectOption } from '@nerv-iip/ui'
import { qualitySourceTypeLabel } from '@nerv-iip/business-core'
import {
  useQualityInspectionPlanCharacteristics,
  useQualityInspectionPlans,
} from '@/composables/useBusinessQuality'
import { useQualityInspectionTaskActions } from '@/composables/useQualityInspectionTasks'
import {
  useQualityInspectionPlanCatalog,
  useQualityReasonCatalog,
  useQualitySkuCatalog,
  useQualityUomCatalog,
} from '@/composables/useQualityPickerCatalog'
import { hasBusinessContext } from '@/composables/businessContextBinding'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
import { useSkuNames } from '@/composables/useSkuNames'
import { usePagedList } from '@/composables/usePagedList'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { recoverLifecycleAction } from '@/composables/lifecycleAction'
import InspectionRecordDetailSheet from '@/components/quality/InspectionRecordDetailSheet.vue'
import {
  NvButton,
  NvCombobox,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvEntityPicker,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { ClipboardCheckIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '检验任务与记录',
    requiredPermissions: ['business.quality.inspection-records.read'],
  },
})

const route = useRoute()
const router = useRouter()
const initialInspectionPlanKeyword = firstQuery(route.query.inspectionPlanId)
const {
  createInspectionRecord,
  createInspectionRecordPending,
  filters,
  inspectionPlans,
  inspectionPlansError,
  inspectionPlansPending,
  inspectionPlansTotal,
  refreshInspectionPlans,
} = useQualityInspectionPlans(
  initialInspectionPlanKeyword ? { keyword: initialInspectionPlanKeyword } : {},
)
const taskActions = useQualityInspectionTaskActions(filters)
const { page, pageSize } = usePagedList(filters, {
  resetOn: [() => filters.status, () => filters.keyword],
})

const recordSheetOpen = shallowRef(false)
const recordCreatedFromLocatedPlanId = shallowRef('')
const characteristicsAppliedPlanId = shallowRef('')

const recordForm = reactive({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  inspectionPlanId: '',
  sourceType: 'operation',
  sourceService: 'mes-operation',
  sourceDocumentId: '',
  skuCode: 'SKU-001',
  inspectedQuantity: '1',
  batchNo: '',
  serialNo: '',
  dispositionReason: '',
  dispositionAttachmentFileIds: '',
  resultLines: [emptyLine()],
})

// 上下文穿透：从工单/工序/收货带入来源单据、批次、序列号。
const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
const targetInspectionPlanId = computed(() => firstQuery(route.query.inspectionPlanId))
const targetInspectionPlan = computed(() =>
  targetInspectionPlanId.value
    ? inspectionPlans.value.find(
        (plan) =>
          plan.id === targetInspectionPlanId.value || plan.code === targetInspectionPlanId.value,
      )
    : undefined,
)
const targetInspectionPlanMissing = computed(
  () =>
    !!targetInspectionPlanId.value && !inspectionPlansPending.value && !targetInspectionPlan.value,
)
// 特性清单取数的方案：路由定位的方案优先；手动/方案行流程里，表单方案号能对上
// 已加载方案列表（id 或 code）时也取其清单——不对上就不发请求，避免逐键敲号打请求。
const characteristicsPlanId = computed(() => {
  if (targetInspectionPlanId.value) return targetInspectionPlanId.value
  const manual = recordForm.inspectionPlanId.trim()
  if (!manual) return ''
  const matched = inspectionPlans.value.find((plan) => plan.id === manual || plan.code === manual)
  return matched?.id ?? ''
})
const {
  planCharacteristics,
  planCharacteristicsError,
  planCharacteristicsPending,
  refreshPlanCharacteristics,
} = useQualityInspectionPlanCharacteristics(() => ({
  organizationId: filters.organizationId,
  environmentId: filters.environmentId,
  inspectionPlanId: characteristicsPlanId.value,
}))
// 特性建议 = 该检验方案的特性清单（label 显中文名称、hint 显编码）；仍允许录入计划外特性。
const characteristicSuggestions = computed<ComboboxSuggestion[]>(() =>
  planCharacteristics.value.flatMap((characteristic) => {
    const code = characteristic.characteristicCode?.trim()
    if (!code) return []
    return [{ value: code, label: characteristic.name?.trim() || code, hint: code }]
  }),
)
// 同一份清单给「只能选」的选择器用：它按 value 反查 label，选中后框里显示的是中文特性名，
// 而不是 `dimension` / `damping-force` 这种提交用的英文码值。
const characteristicOptions = computed<SearchSelectOption[]>(() =>
  characteristicSuggestions.value.map((item) => ({
    value: item.value,
    label: item.label ?? item.value,
    hint: item.hint,
  })),
)

// 来源检验记录定位：hold 时间线「来源检验记录」互链带 ?inspectionRecordId= 进来，打开只读记录详情。
// 详情查询/错误副作用/重试封装在 InspectionRecordDetailSheet，路由页只负责按 query 编排开合（Vue best-practices §2）。
const recordDetailId = computed(() => firstQuery(route.query.inspectionRecordId))
const recordDetailOpen = shallowRef(false)
watch(
  recordDetailId,
  (id) => {
    recordDetailOpen.value = !!id
  },
  { immediate: true },
)
const scanAuditRoute = computed(() => ({
  path: '/barcode/scans',
  query: {
    sourceWorkflow: 'quality.inspection',
    sourceDocumentId: recordForm.sourceDocumentId || targetInspectionPlanId.value || undefined,
    scannedValue: recordForm.serialNo || recordForm.batchNo || undefined,
  },
}))
const shouldCreateRecordFromLocatedPlan = computed(
  () => firstQuery(route.query.action).toLowerCase() === 'create',
)
watch(
  () => route.query,
  (query) => {
    const source =
      firstQuery(query.sourceDocumentNo) ||
      firstQuery(query.sourceDocumentId) ||
      firstQuery(query.workOrderId) ||
      firstQuery(query.operationTaskId)
    const skuCode = firstQuery(query.skuCode)
    const quantity = firstQuery(query.quantity)
    const batch = firstQuery(query.batchNo) || firstQuery(query.materialLotId)
    const serial = firstQuery(query.serialNo)
    if (source) recordForm.sourceDocumentId = source
    if (skuCode) recordForm.skuCode = skuCode
    if (quantity) recordForm.inspectedQuantity = quantity
    if (batch) recordForm.batchNo = batch
    if (serial) recordForm.serialNo = serial
    // 来源类型/来源服务：优先用 query 显式值；否则按入口推断——
    // 物料批且非工序入口视为收货/WMS，避免从收货进入仍归到 MES 工序来源。
    const sourceType = firstQuery(query.sourceType)
    const sourceService = firstQuery(query.sourceService)
    const receivingEntry = !!firstQuery(query.materialLotId) && !firstQuery(query.operationTaskId)
    if (sourceType) recordForm.sourceType = sourceType
    else if (receivingEntry) recordForm.sourceType = 'receiving'
    if (sourceService) recordForm.sourceService = sourceService
    else if (receivingEntry) recordForm.sourceService = 'wms'
    if (source) recordSheetOpen.value = true
  },
  { immediate: true },
)
watch(
  targetInspectionPlanId,
  (id) => {
    if (id) {
      filters.status = undefined
      filters.keyword = id
    } else {
      filters.keyword = undefined
    }
    recordCreatedFromLocatedPlanId.value = ''
  },
  { immediate: true },
)
watch(
  [targetInspectionPlan, shouldCreateRecordFromLocatedPlan],
  ([plan, shouldCreate]) => {
    if (
      !plan ||
      !shouldCreate ||
      recordCreatedFromLocatedPlanId.value === targetInspectionPlanId.value
    )
      return
    recordCreatedFromLocatedPlanId.value = targetInspectionPlanId.value
    useInspectionPlan(plan)
  },
  { immediate: true },
)
watch(
  [planCharacteristics, targetInspectionPlanId, shouldCreateRecordFromLocatedPlan],
  ([characteristics, inspectionPlanId, shouldCreate]) => {
    if (
      !inspectionPlanId ||
      !shouldCreate ||
      characteristics.length === 0 ||
      characteristicsAppliedPlanId.value === inspectionPlanId ||
      !hasPristineResultLines()
    )
      return
    characteristicsAppliedPlanId.value = inspectionPlanId
    recordForm.resultLines = characteristics.map((characteristic) => ({
      ...emptyLine(),
      characteristicCode: characteristic.characteristicCode ?? '',
      characteristicName: characteristic.name ?? '',
      characteristicType: characteristic.characteristicType ?? '',
      unitCode: characteristic.unitCode ?? '',
      specification: formatSpecification(characteristic),
    }))
  },
  { immediate: true },
)

// 创建检验记录的码值一律只选不填：方案、物料、单位取主数据目录，缺陷/处置原因取原因码目录。
const planCatalog = useQualityInspectionPlanCatalog()
const skuCatalog = useQualitySkuCatalog()
const uomCatalog = useQualityUomCatalog()
const reasonCatalog = useQualityReasonCatalog()
// 换检验方案等于换了检验对象：方案自带的物料要跟着走，已填的特性行不再属于新方案，重置回一行空行。
const recordPlanModel = computed({
  get: () => recordForm.inspectionPlanId,
  set: (value: string) => {
    if (value === recordForm.inspectionPlanId) return
    recordForm.inspectionPlanId = value
    characteristicsAppliedPlanId.value = ''
    recordForm.resultLines = [emptyLine()]
    const plan = planCatalog.inspectionPlans.value.find((item) => item.id === value)
    if (plan?.skuCode) recordForm.skuCode = plan.skuCode
  },
})

const listErrorMessage = computed(() => formatError(inspectionPlansError.value))
const inspectedQuantity = computed(() => toOptionalNumber(recordForm.inspectedQuantity))
const isInspectionTaskFlow = computed(() => !!firstQuery(route.query.inspectionTaskId))
// 从待检任务行进入时来源字段全部由任务带出——只读呈现，不给用户改的错觉。
const recordContextItems = computed(() => [
  { label: '检验方案', value: recordForm.inspectionPlanId },
  { label: '来源类型', value: sourceTypeLabel(recordForm.sourceType) },
  { label: '来源单据', value: recordForm.sourceDocumentId },
  { label: '物料', value: recordForm.skuCode },
  { label: '检验数量', value: recordForm.inspectedQuantity },
  { label: '批次', value: recordForm.batchNo },
  { label: '序列号', value: recordForm.serialNo },
])
const requiresDispositionReason = computed(() =>
  recordForm.resultLines.some(
    (line) => line.result === 'failed' || line.result === 'conditional-release',
  ),
)
const validResultLines = computed(() =>
  recordForm.resultLines.filter(
    (line) =>
      isNonEmpty(line.characteristicCode) &&
      // 计量型特性有效性看数值测量值（后端契约必填），计数型仍看实测值文本（#1326）。
      (isVariableLine(line)
        ? toMeasuredNumber(line.measuredValue) !== undefined
        : isNonEmpty(line.observedValue)) &&
      isNonEmpty(line.result) &&
      hasRequiredDefectContext(line),
  ),
)
const canCreateRecord = computed(
  () =>
    hasBusinessContext(filters) &&
    (isInspectionTaskFlow.value ||
      (isNonEmpty(recordForm.organizationId) && isNonEmpty(recordForm.environmentId))) &&
    isNonEmpty(recordForm.sourceType) &&
    isNonEmpty(recordForm.sourceService) &&
    isNonEmpty(recordForm.sourceDocumentId) &&
    isNonEmpty(recordForm.skuCode) &&
    inspectedQuantity.value !== undefined &&
    inspectedQuantity.value > 0 &&
    (!requiresDispositionReason.value || isNonEmpty(recordForm.dispositionReason)) &&
    (!isInspectionTaskFlow.value ||
      (!planCharacteristicsPending.value && !planCharacteristicsError.value)) &&
    validResultLines.value.length > 0,
)

// 检验方案读面只回编码（SKU-… / WC-… / DEV-…），中文名在主数据里，按编码 join 出来。
const { resolveSkuName } = useSkuNames()
const { resolveDevice, resolveWorkCenter } = useMasterDataDisplayNames({
  devices: true,
  workCenters: true,
})

type PlanRow = BusinessConsoleQualityItem
const columns: NvDataTableColumn<PlanRow>[] = [
  { key: 'code', header: '方案', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'summary', header: '摘要', accessor: (r) => qualityItemSummary(r) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

function emptyLine() {
  return {
    characteristicCode: '',
    result: 'passed',
    observedValue: '',
    // 计量型（variable）特性提交契约要求数值 measuredValue，缺失后端直接拒（#1326）；
    // characteristicType 从方案特性清单带出，驱动「数值输入 vs 文本输入」的切换。
    measuredValue: '',
    characteristicType: '',
    unitCode: '',
    defectReason: '',
    defectQuantity: '',
    characteristicName: '',
    specification: '',
  }
}
function hasPristineResultLines() {
  if (recordForm.resultLines.length !== 1) return false
  const line = recordForm.resultLines[0]
  return (
    !!line &&
    line.result === 'passed' &&
    [
      line.characteristicCode,
      line.observedValue,
      line.measuredValue,
      line.characteristicType,
      line.unitCode,
      line.defectReason,
      line.defectQuantity,
      line.characteristicName,
      line.specification,
    ].every((value) => value === '')
  )
}
function formatSpecification(characteristic: {
  nominalValue?: number | null
  lowerSpecLimit?: number | null
  upperSpecLimit?: number | null
  unitCode?: string | null
}) {
  const unit = characteristic.unitCode ? ` ${characteristic.unitCode}` : ''
  if (characteristic.lowerSpecLimit != null || characteristic.upperSpecLimit != null) {
    return `${characteristic.lowerSpecLimit ?? '—'}–${characteristic.upperSpecLimit ?? '—'}${unit}`
  }
  return characteristic.nominalValue == null ? '' : `目标 ${characteristic.nominalValue}${unit}`
}
function useInspectionPlan(plan: BusinessConsoleQualityItem) {
  recordForm.inspectionPlanId = plan.id ?? ''
  if (plan.skuCode && !firstQuery(route.query.skuCode)) recordForm.skuCode = plan.skuCode
  recordSheetOpen.value = true
}
function addCharacteristicRow() {
  recordForm.resultLines.push(emptyLine())
}
// 选中（或敲全）一个计划特性编码时，带出名称 / 单位 / 规格；计划外编码不动其它字段。
function onCharacteristicCodeChange(line: ReturnType<typeof emptyLine>, value: string) {
  line.characteristicCode = value
  const code = value.trim().toLowerCase()
  if (!code) return
  const characteristic = planCharacteristics.value.find(
    (item) => (item.characteristicCode ?? '').trim().toLowerCase() === code,
  )
  if (!characteristic) {
    // 计划外编码类型未知：清掉类型标记回退文本录入，避免沿用上一个特性的计量模式。
    line.characteristicType = ''
    return
  }
  line.characteristicName = characteristic.name ?? ''
  line.characteristicType = characteristic.characteristicType ?? ''
  if (characteristic.unitCode) line.unitCode = characteristic.unitCode
  line.specification = formatSpecification(characteristic)
}
// 计量型（variable）特性：录数值测量值、提交 measuredValue；计数型（attribute）录文本实测值。
function isVariableLine(line: { characteristicType: string }) {
  return line.characteristicType.trim().toLowerCase() === 'variable'
}
// 测量值必须显式录入：空串不许被 Number('') === 0 吞成合法的 0（区别于 toOptionalNumber）。
function toMeasuredNumber(value: string) {
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}
function removeCharacteristicRow(index: number) {
  if (recordForm.resultLines.length === 1) {
    recordForm.resultLines[0] = emptyLine()
    return
  }
  recordForm.resultLines.splice(index, 1)
}

const INSPECTION_TASK_CONTEXT_QUERY_KEYS = [
  'inspectionTaskId',
  'inspectionPlanId',
  'sourceDocumentId',
  'sourceDocumentNo',
  'workOrderId',
  'operationTaskId',
  'sourceType',
  'sourceService',
  'skuCode',
  'quantity',
  'batchNo',
  'materialLotId',
  'serialNo',
  'action',
] as const

async function resetInspectionTaskContext() {
  recordSheetOpen.value = false
  recordCreatedFromLocatedPlanId.value = ''
  characteristicsAppliedPlanId.value = ''
  filters.keyword = undefined
  Object.assign(recordForm, {
    inspectionPlanId: '',
    sourceType: 'operation',
    sourceService: 'mes-operation',
    sourceDocumentId: '',
    skuCode: '',
    inspectedQuantity: '1',
    batchNo: '',
    serialNo: '',
    dispositionReason: '',
    dispositionAttachmentFileIds: '',
    resultLines: [emptyLine()],
  })

  const query = { ...route.query }
  for (const key of INSPECTION_TASK_CONTEXT_QUERY_KEYS) delete query[key]
  await router.replace({ query })
}

async function submitInspectionRecord() {
  if (!hasBusinessContext(filters)) {
    notifyError('业务范围尚未就绪，请稍后重试。')
    return
  }
  if (!canCreateRecord.value) return
  const inspectionTaskId = firstQuery(route.query.inspectionTaskId)
  if (inspectionTaskId) {
    let response
    try {
      response = await taskActions.startInspection(inspectionTaskId, {
        resultLines: toCharacteristicResults(),
        dispositionReason: optionalText(recordForm.dispositionReason),
        dispositionAttachmentFileIds: splitCsv(recordForm.dispositionAttachmentFileIds),
      })
    } catch (error) {
      if (
        await recoverLifecycleAction(error, {
          reset: resetInspectionTaskContext,
          refresh: taskActions.refreshInspectionTasks,
          notify: (message) => notifyError(message),
        })
      ) {
        return
      }
      notifyOperationFailure('检验记录提交失败', error, '检验记录提交失败，请稍后重试。')
      return
    }
    recordSheetOpen.value = false
    notifySuccess(
      `检验记录 ${response?.data?.inspectionRecordId ?? inspectionTaskId} 已提交，待检任务已闭合。`,
    )
    await router.push({
      path: '/quality/inspection-tasks',
    })
    return
  }
  const body: BusinessConsoleCreateInspectionRecordRequest = {
    organizationId: recordForm.organizationId.trim(),
    environmentId: recordForm.environmentId.trim(),
    inspectionPlanId: optionalText(recordForm.inspectionPlanId),
    sourceType: recordForm.sourceType.trim(),
    sourceService: recordForm.sourceService.trim(),
    sourceDocumentId: recordForm.sourceDocumentId.trim(),
    skuCode: recordForm.skuCode.trim(),
    inspectedQuantity: inspectedQuantity.value,
    batchNo: optionalText(recordForm.batchNo),
    serialNo: optionalText(recordForm.serialNo),
    resultLines: toCharacteristicResults(),
    dispositionReason: optionalText(recordForm.dispositionReason),
    dispositionAttachmentFileIds: splitCsv(recordForm.dispositionAttachmentFileIds),
  }
  let response
  try {
    response = await createInspectionRecord(body)
  } catch (error) {
    notifyOperationFailure('检验记录提交失败', error, '检验记录提交失败，请稍后重试。')
    return
  }
  recordSheetOpen.value = false
  notifySuccess(`检验记录 ${response?.data?.inspectionRecordId ?? body.sourceDocumentId} 已提交。`)
}

function toCharacteristicResults(): BusinessConsoleInspectionCharacteristicResult[] {
  return validResultLines.value.map((line) => {
    // 计量型特性必须带数值 measuredValue（后端按方案规格判定），observedValue 同步为其字符串形式；
    // 计数型保持 observedValue 文本、不带 measuredValue（#1326）。
    const measuredValue = isVariableLine(line) ? toMeasuredNumber(line.measuredValue) : undefined
    return {
      characteristicCode: line.characteristicCode.trim(),
      result: line.result.trim(),
      observedValue:
        measuredValue !== undefined ? String(measuredValue) : line.observedValue.trim(),
      measuredValue,
      unitCode: optionalText(line.unitCode),
      defectReason: optionalText(line.defectReason),
      defectQuantity: toOptionalNumber(line.defectQuantity),
    }
  })
}
function optionalText(value: string) {
  const trimmed = value.trim()
  return trimmed ? trimmed : undefined
}
function splitCsv(value: string) {
  const values = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean)
  return values.length ? values : undefined
}
function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}
function hasRequiredDefectContext(line: {
  result: string
  defectReason: string
  defectQuantity: string
}) {
  if (line.result === 'passed') return true
  if (!isNonEmpty(line.defectReason)) return false
  return line.result !== 'conditional-release' || (toOptionalNumber(line.defectQuantity) ?? 0) > 0
}
// 检验类别是英文码（receiving/operation/final…），摘要列直接拼会漏出 operation 这类工程语言。
const CATEGORY_LABELS: Record<string, string> = {
  receiving: '来料检',
  operation: '工序检',
  'in-process': '工序检',
  final: '终检',
  outgoing: '出货检',
  rework: '返工检',
}
// 来源类型是英文码，只读带出区要显示中文（映射来自 business-core qualityLabels，PC/PDA 同源）。
function sourceTypeLabel(value?: string | null) {
  return qualitySourceTypeLabel(value)
}
function categoryLabel(value?: string | null) {
  const code = (value ?? '').trim()
  if (!code) return ''
  return CATEGORY_LABELS[code.toLowerCase()] ?? code
}
/** 「中文名（编码）」；名录查不到就只显编码，不编造名字。 */
function namedCode(code: string | null | undefined, name: string | undefined) {
  if (!isPresent(code)) return undefined
  return name ? `${name}（${code}）` : code
}
function qualityItemSummary(item: BusinessConsoleQualityItem) {
  const values = [
    categoryLabel(item.category),
    namedCode(item.skuCode, resolveSkuName(item.skuCode)),
    item.partnerId,
    namedCode(item.workCenterId, resolveWorkCenter(item.workCenterId)),
    namedCode(item.deviceAssetId, resolveDevice(item.deviceAssetId)),
    item.documentType,
  ].filter(isPresent)
  return values.length ? values.join(' / ') : '无'
}
function firstQuery(value: unknown) {
  if (Array.isArray(value)) return typeof value[0] === 'string' ? value[0] : ''
  return typeof value === 'string' ? value : ''
}
function formatError(error: unknown) {
  return inlineErrorMessage(error)
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function isPresent(value: string | undefined | null): value is string {
  return typeof value === 'string' && value.trim().length > 0
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="检验任务与记录"
      :breadcrumbs="[{ label: '质量' }]"
      :count="`${inspectionPlansTotal} 个检验方案`"
    >
      <template #actions>
        <NvButton v-if="contextWorkOrderId" size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="`/mes/work-orders/${encodeURIComponent(contextWorkOrderId)}`"
            >返回工单 {{ contextWorkOrderId }}</RouterLink
          >
        </NvButton>
        <NvButton size="sm" type="button" variant="outline" as-child>
          <RouterLink :to="scanAuditRoute">扫码记录</RouterLink>
        </NvButton>
        <NvButton size="sm" type="button" @click="recordSheetOpen = true">
          <ClipboardCheckIcon aria-hidden="true" />
          创建检验记录
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="inspectionPlansPending"
          @click="refreshInspectionPlans"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.status"
          class="h-9 w-32"
          placeholder="状态（可选）"
          aria-label="检验状态"
        />
      </template>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>
    <p v-else-if="targetInspectionPlanMissing" class="text-sm text-warning" role="status">
      未找到检验方案 {{ targetInspectionPlanId }}。请确认该方案是否已归档或无权访问。
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="inspectionPlansTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="inspectionPlans"
      :row-key="(r) => r.id ?? r.code ?? '无'"
      :loading="inspectionPlansPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前筛选下没有检验方案。检验记录应从工单、收货或检验任务进入；也可用右上角创建检验记录临时补录。"
    >
      <template #cell-code="{ row }">
        <span class="font-medium">{{ row.code ?? '无' }}</span>
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`检验方案操作 ${row.code ?? ''}`">
          <NvDropdownMenuItem @click="useInspectionPlan(row)">
            <ClipboardCheckIcon aria-hidden="true" />
            创建检验记录
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="recordSheetOpen">
      <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-3xl">
        <NvDialogHeader>
          <NvDialogTitle>创建检验记录</NvDialogTitle>
          <!-- 检验对象由下方只读区或来源字段呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            检验对象：来源单据 {{ recordForm.sourceDocumentId || '未指定' }}，物料
            {{ recordForm.skuCode }}。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid content-start gap-4" @submit.prevent="submitInspectionRecord">
          <!-- 从待检任务行进入：来源单据 / 物料 / 数量 / 批次 全部由任务带出，只读呈现。 -->
          <CarriedContextSummary
            v-if="isInspectionTaskFlow"
            label="检验对象"
            :items="recordContextItems"
          />

          <NvFieldGroup v-else class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="record-plan">检验方案</NvFieldLabel>
              <NvEntityPicker
                id="record-plan"
                v-model="recordPlanModel"
                :options="planCatalog.inspectionPlanOptions.value"
                title="选择检验方案"
                placeholder="选择检验方案"
                source-text="数据来自质量检验方案"
                empty-text="当前范围内没有检验方案"
                :loading="planCatalog.inspectionPlansPending.value"
                clearable
                aria-label="检验方案"
              />
            </NvField>
            <NvField>
              <NvFieldLabel>来源类型</NvFieldLabel>
              <NvSelect v-model="recordForm.sourceType">
                <NvSelectTrigger aria-label="来源类型"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="operation">工序</NvSelectItem>
                  <NvSelectItem value="receiving">收货</NvSelectItem>
                  <NvSelectItem value="final">终检</NvSelectItem>
                  <NvSelectItem value="maintenance">维修</NvSelectItem>
                  <NvSelectItem value="customer-return">客户退货</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel>来源服务</NvFieldLabel>
              <NvSelect v-model="recordForm.sourceService">
                <NvSelectTrigger aria-label="来源服务"><NvSelectValue /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="mes-operation">MES 工序</NvSelectItem>
                  <NvSelectItem value="inventory">库存</NvSelectItem>
                  <NvSelectItem value="wms">WMS</NvSelectItem>
                  <NvSelectItem value="mes">MES</NvSelectItem>
                  <NvSelectItem value="erp">ERP</NvSelectItem>
                  <NvSelectItem value="maintenance">维修</NvSelectItem>
                  <NvSelectItem value="purchase-receipt">采购收货</NvSelectItem>
                  <NvSelectItem value="customer-return">客户退货</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="record-source-document">来源单据</NvFieldLabel>
              <NvInput id="record-source-document" v-model="recordForm.sourceDocumentId" required />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-sku">SKU</NvFieldLabel>
              <NvEntityPicker
                id="record-sku"
                v-model="recordForm.skuCode"
                :options="skuCatalog.skuOptions.value"
                title="选择 SKU"
                placeholder="选择 SKU"
                source-text="数据来自基础数据物料主数据"
                :loading="skuCatalog.skusPending.value"
                aria-label="SKU"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-quantity">检验数量</NvFieldLabel>
              <NvInput
                id="record-quantity"
                v-model="recordForm.inspectedQuantity"
                inputmode="decimal"
                min="0.000001"
                required
                step="any"
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-batch">批次</NvFieldLabel>
              <NvInput id="record-batch" v-model="recordForm.batchNo" />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-serial">序列号</NvFieldLabel>
              <NvInput id="record-serial" v-model="recordForm.serialNo" />
            </NvField>
          </NvFieldGroup>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <h3 class="text-sm font-semibold text-foreground">检验特性</h3>
              <NvButton size="sm" variant="outline" type="button" @click="addCharacteristicRow">
                <PlusIcon aria-hidden="true" />
                添加行
              </NvButton>
            </div>
            <p
              v-if="planCharacteristicsError"
              class="flex items-center gap-2 text-sm text-destructive"
              role="alert"
            >
              检验特性与规格加载失败，请重试后再检验。
              <NvButton
                size="sm"
                variant="outline"
                type="button"
                @click="refreshPlanCharacteristics"
              >
                重试
              </NvButton>
            </p>
            <p
              v-else-if="planCharacteristicsPending"
              class="text-sm text-muted-foreground"
              role="status"
            >
              正在加载检验特性与规格…
            </p>
            <p
              v-else-if="characteristicsPlanId && characteristicSuggestions.length"
              class="text-sm text-muted-foreground"
            >
              特性编码建议来自该检验方案的特性清单，也可直接录入计划外特性。
            </p>
            <p v-else-if="characteristicsPlanId" class="text-sm text-muted-foreground">
              该检验方案暂无特性清单，请直接录入特性编码。
            </p>
            <div class="grid gap-2">
              <div
                v-for="(line, index) in recordForm.resultLines"
                :key="index"
                class="grid gap-2 rounded-lg border p-3 md:grid-cols-[1fr_140px_1fr_110px_auto]"
              >
                <NvField>
                  <NvFieldLabel :for="`characteristic-code-${index}`">检验特性</NvFieldLabel>
                  <!-- 方案有特性清单时用「只能选」的选择器：框里显中文特性名，英文编码只作副信息；
                       清单为空（计划外特性）才退回自由录入的编码输入框。 -->
                  <NvSearchSelect
                    v-if="characteristicOptions.length"
                    :id="`characteristic-code-${index}`"
                    :model-value="line.characteristicCode"
                    :options="characteristicOptions"
                    placeholder="选择检验特性"
                    search-placeholder="搜索特性名称 / 编码…"
                    empty-text="特性清单中无匹配项"
                    :aria-label="`第 ${index + 1} 个检验特性`"
                    @update:model-value="(value) => onCharacteristicCodeChange(line, value)"
                  />
                  <NvCombobox
                    v-else
                    :id="`characteristic-code-${index}`"
                    :model-value="line.characteristicCode"
                    :suggestions="characteristicSuggestions"
                    placeholder="录入特性编码"
                    empty-text="特性清单中无匹配项"
                    @update:model-value="(value) => onCharacteristicCodeChange(line, value)"
                  />
                  <NvFieldDescription v-if="line.characteristicCode">
                    编码：{{ line.characteristicCode }}
                  </NvFieldDescription>
                </NvField>
                <NvField>
                  <NvFieldLabel>结果</NvFieldLabel>
                  <NvSelect v-model="line.result">
                    <NvSelectTrigger :aria-label="`第 ${index + 1} 个特性结果`"
                      ><NvSelectValue
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem value="passed">合格</NvSelectItem>
                      <NvSelectItem value="failed">不合格</NvSelectItem>
                      <NvSelectItem value="conditional-release">让步放行</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <!-- 计量型特性录数值测量值（提交 measuredValue，语义对齐 PDA 数字键盘）；
                     计数型仍录文本实测值（observedValue）。 -->
                <NvField v-if="isVariableLine(line)">
                  <NvFieldLabel :for="`measured-value-${index}`">
                    测量值{{ line.unitCode ? `（${line.unitCode}）` : '' }}
                  </NvFieldLabel>
                  <NvInput
                    :id="`measured-value-${index}`"
                    v-model="line.measuredValue"
                    type="number"
                    inputmode="decimal"
                    step="any"
                    required
                  />
                  <NvFieldDescription v-if="line.specification">
                    规格：{{ line.specification }}
                  </NvFieldDescription>
                </NvField>
                <NvField v-else>
                  <NvFieldLabel :for="`observed-value-${index}`">实测值</NvFieldLabel>
                  <NvInput :id="`observed-value-${index}`" v-model="line.observedValue" required />
                  <NvFieldDescription v-if="line.specification">
                    规格：{{ line.specification }}
                  </NvFieldDescription>
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`unit-code-${index}`">单位</NvFieldLabel>
                  <NvEntityPicker
                    :id="`unit-code-${index}`"
                    v-model="line.unitCode"
                    :options="uomCatalog.uomOptions.value"
                    title="选择计量单位"
                    placeholder="选择单位"
                    source-text="数据来自基础数据计量单位"
                    :loading="uomCatalog.uomsPending.value"
                    clearable
                    :aria-label="`第 ${index + 1} 个特性单位`"
                  />
                </NvField>
                <NvField class="md:col-span-2">
                  <NvFieldLabel :for="`defect-reason-${index}`">缺陷原因</NvFieldLabel>
                  <NvSearchSelect
                    :id="`defect-reason-${index}`"
                    v-model="line.defectReason"
                    :options="reasonCatalog.defectReasonOptions.value"
                    placeholder="选择缺陷原因"
                    empty-text="原因码目录里没有匹配项"
                    :loading="reasonCatalog.reasonsPending.value"
                    :aria-label="`第 ${index + 1} 个特性缺陷原因`"
                  />
                </NvField>
                <NvField>
                  <NvFieldLabel :for="`defect-quantity-${index}`">缺陷数量</NvFieldLabel>
                  <NvInput
                    :id="`defect-quantity-${index}`"
                    v-model="line.defectQuantity"
                    inputmode="decimal"
                    type="number"
                  />
                </NvField>
                <div class="flex items-end justify-end">
                  <NvButton
                    size="icon-sm"
                    variant="ghost"
                    type="button"
                    @click="removeCharacteristicRow(index)"
                  >
                    <Trash2Icon />
                    <span class="sr-only">移除检验特性</span>
                  </NvButton>
                </div>
              </div>
            </div>
          </div>

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="record-disposition">
                处置原因
                <span v-if="requiresDispositionReason" class="text-destructive">*</span>
              </NvFieldLabel>
              <!-- 处置原因是记录详情直接展示的处置结论，所以存人读原因名称、不是原因编码。 -->
              <NvSearchSelect
                id="record-disposition"
                v-model="recordForm.dispositionReason"
                :options="reasonCatalog.dispositionReasonOptions.value"
                placeholder="选择处置原因"
                empty-text="原因码目录里没有匹配项"
                :loading="reasonCatalog.reasonsPending.value"
                aria-label="处置原因"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="record-files">附件</NvFieldLabel>
              <NvInput
                id="record-files"
                v-model="recordForm.dispositionAttachmentFileIds"
                placeholder="多个附件用逗号分隔"
              />
            </NvField>
          </NvFieldGroup>

          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="recordSheetOpen = false"
              >取消</NvButton
            >
            <NvButton type="submit" :disabled="createInspectionRecordPending || !canCreateRecord">
              <Spinner v-if="createInspectionRecordPending" aria-hidden="true" />
              <ClipboardCheckIcon v-else aria-hidden="true" />
              提交检验记录
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>

    <!-- 来源检验记录只读详情：hold 时间线「来源检验记录」互链带 ?inspectionRecordId= 进入即定位到该记录。 -->
    <InspectionRecordDetailSheet
      v-model:open="recordDetailOpen"
      :record-id="recordDetailId"
      :organization-id="filters.organizationId"
      :environment-id="filters.environmentId"
    />
  </BusinessLayout>
</template>
