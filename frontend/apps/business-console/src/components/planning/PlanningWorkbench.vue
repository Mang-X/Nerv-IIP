<script setup lang="ts">
import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleMpsBucketItem,
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import {
  useBusinessSkus,
  useBusinessMasterDataResources,
} from '@/composables/useBusinessMasterData'
import { useBusinessPlanning } from '@/composables/useBusinessPlanning'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDatePicker,
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
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
} from '@nerv-iip/ui'
import {
  CheckIcon,
  CornerDownRightIcon,
  ExternalLinkIcon,
  NetworkIcon,
  PlayIcon,
  PlusIcon,
  RefreshCwIcon,
} from '@lucide/vue'
import { computed, shallowRef } from 'vue'
import { useRouter } from 'vue-router'

const {
  acceptSuggestion,
  acceptSuggestionError,
  acceptSuggestionPending,
  createMpsBucket,
  createMpsBucketError,
  createMpsBucketPending,
  createDemandError,
  createDemandPending,
  createOrUpdateDemand,
  demandForm,
  demands,
  demandsError,
  demandsPending,
  mrpRuns,
  mrpRunsError,
  mrpRunsPending,
  mpsBuckets,
  mpsBucketsError,
  mpsBucketsPending,
  mpsForm,
  releaseMpsBucket,
  releaseMpsBucketError,
  releaseMpsBucketPending,
  reviewMpsBucket,
  reviewMpsBucketError,
  reviewMpsBucketPending,
  pegging,
  peggingPending,
  refreshPlanning,
  runMrp,
  runMrpError,
  runMrpPending,
  runRequest,
  runSelection,
  suggestionFilters,
  suggestionTypeFilter,
  suggestions,
  suggestionsError,
  suggestionsPending,
} = useBusinessPlanning()
const router = useRouter()

// 主数据：SKU / 工厂 / 计量单位（Select 显名称、绑定编码，码→名解析复用）。
const { skus } = useBusinessSkus()
const { resources: sites } = useBusinessMasterDataResources('site')
const { resources: units } = useBusinessMasterDataResources('unit-of-measure')

const skuNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const sku of skus.value) {
    if (sku.code) map.set(sku.code, sku.displayName ?? sku.code)
  }
  return map
})
const siteNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const site of sites.value) {
    if (site.code) map.set(site.code, site.displayName ?? site.code)
  }
  return map
})
function skuLabel(code?: string | null) {
  if (!code) return '—'
  return skuNameByCode.value.get(code) ?? code
}
function siteLabel(code?: string | null) {
  if (!code) return '—'
  return siteNameByCode.value.get(code) ?? code
}

const skuOptions = computed(() =>
  skus.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
const siteOptions = computed(() =>
  sites.value
    .filter((s) => s.code)
    .map((s) => ({ value: s.code as string, label: `${s.displayName ?? s.code} · ${s.code}` })),
)
const uomOptions = computed(() =>
  units.value
    .filter((u) => u.code)
    .map((u) => ({ value: u.code as string, label: `${u.displayName ?? u.code} · ${u.code}` })),
)

const canSubmitDemand = computed(
  () =>
    !!demandForm.skuCode?.trim() &&
    !!demandForm.siteCode?.trim() &&
    !!demandForm.uomCode?.trim() &&
    (demandForm.quantity ?? 0) > 0,
)
const canSubmitMps = computed(
  () =>
    !!mpsForm.skuCode?.trim() &&
    !!mpsForm.siteCode?.trim() &&
    !!mpsForm.uomCode?.trim() &&
    !!mpsForm.bucketDate &&
    (mpsForm.quantity ?? 0) > 0,
)

const errorMessage = computed(
  () =>
    [
      demandsError,
      mpsBucketsError,
      mrpRunsError,
      suggestionsError,
      createDemandError,
      createMpsBucketError,
      reviewMpsBucketError,
      releaseMpsBucketError,
      runMrpError,
      acceptSuggestionError,
    ]
      .map((ref) => formatError(ref.value))
      .find(Boolean) ?? '',
)
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}

const demandOpen = shallowRef(false)
const mpsOpen = shallowRef(false)
const mrpOpen = shallowRef(false)
const acceptingSuggestionId = shallowRef<string | null>(null)

const demandTypeOptions = [
  { label: '预测', value: 'forecast' },
  { label: '安全库存', value: 'safety-stock' },
]
const suggestionStatusOptions = [
  { label: '待评审', value: 'open' },
  { label: '已接受', value: 'accepted' },
]

// 建议分型（呼应两组：生产→MES / 采购→ERP）。
const productionSuggestions = computed(() =>
  suggestions.value.filter((i) => i.suggestionType === 'planned-work-order'),
)
const purchaseSuggestions = computed(() =>
  suggestions.value.filter((i) => i.suggestionType === 'planned-purchase'),
)
const openSuggestionCount = computed(() => suggestions.value.filter((i) => isOpen(i.status)).length)
const releasedMpsCount = computed(
  () => mpsBuckets.value.filter((i) => isMpsReleased(i.status)).length,
)

// KPI #3：换掉无意义的跨 SKU/单位求和，改 3 张语义卡。
// 卡1：待评审建议条数（生产 N / 采购 N 拆分作脚注）。
const reviewKpiHint = computed(
  () =>
    `生产 ${productionSuggestions.value.filter((i) => isOpen(i.status)).length} · 采购 ${purchaseSuggestions.value.filter((i) => isOpen(i.status)).length}`,
)

// 卡2：需求 SKU 数 / 已被建议覆盖的 SKU 数（去重）。
const demandSkuCodes = computed(
  () => new Set(demands.value.map((d) => d.skuCode).filter((c): c is string => !!c)),
)
const coveredSkuCodes = computed(() => {
  const covered = new Set<string>()
  const demandSet = demandSkuCodes.value
  for (const s of suggestions.value) {
    if (s.skuCode && demandSet.has(s.skuCode)) covered.add(s.skuCode)
  }
  return covered
})
const demandSkuKpi = computed(() => `${coveredSkuCodes.value.size} / ${demandSkuCodes.value.size}`)

// 卡3：最近一次 MRP（状态 + 建议数）。运行按计划范围排序取最后一条。
const latestRun = computed(() => {
  const runs = mrpRuns.value
  if (runs.length === 0) return null
  return [...runs].sort((a, b) => (a.horizonStart ?? '').localeCompare(b.horizonStart ?? ''))[
    runs.length - 1
  ]
})
const latestRunKpiValue = computed(() =>
  latestRun.value ? planningStatus(latestRun.value.status).label : '未运行',
)
const latestRunKpiHint = computed(() =>
  latestRun.value ? `输入 ${inputSourcesLabel(latestRun.value.inputSources)}` : '尚未运行 MRP',
)

// MRP 运行覆盖率 = 建议数 / 需求数（除零保护）。
function coverageRate(run: BusinessConsoleMrpRunItem): string {
  const demandCount = run.demandCount ?? 0
  if (demandCount <= 0) return '—'
  const pct = Math.round(((run.suggestionCount ?? 0) / demandCount) * 100)
  return `${pct}%`
}

// 运行的人读锚点：以「计划范围」作追溯标题，避免裸 GUID。
function runHorizonLabel(run?: BusinessConsoleMrpRunItem | null): string {
  if (!run) return '选择一次运行'
  return `计划范围 ${formatDate(run.horizonStart)} ~ ${formatDate(run.horizonEnd)}`
}
const selectedRun = computed(
  () => mrpRuns.value.find((r) => r.runId === runSelection.runId) ?? null,
)

// 需求紧迫度：按 dueDate 距今天数。逾期 danger / 7 天内 warning / 正常 neutral。
function dueUrgency(dueDate?: string | null): { label: string; tone: StatusTone } {
  if (!dueDate) return { label: '未排期', tone: 'neutral' }
  const due = new Date(dueDate.slice(0, 10))
  if (Number.isNaN(due.getTime())) return { label: '未排期', tone: 'neutral' }
  const today = new Date(new Date().toISOString().slice(0, 10))
  const days = Math.round((due.getTime() - today.getTime()) / 86_400_000)
  if (days < 0) return { label: `逾期 ${Math.abs(days)} 天`, tone: 'danger' }
  if (days === 0) return { label: '今日到期', tone: 'warning' }
  if (days <= 7) return { label: `${days} 天内`, tone: 'warning' }
  return { label: `${days} 天后`, tone: 'neutral' }
}

// 需求覆盖状态：建议里出现该 SKU → 已生成建议；否则未覆盖。
function demandCoverage(skuCode?: string | null): { label: string; tone: StatusTone } {
  if (skuCode && coveredSkuCodes.value.has(skuCode)) return { label: '已生成建议', tone: 'success' }
  return { label: '未覆盖', tone: 'neutral' }
}

// pegging 分型（成品净需求 / 组件展开）。
function peggingTypeLabel(value?: string | null): { label: string; tone: StatusTone } {
  const map: Record<string, { label: string; tone: StatusTone }> = {
    'finished-good': { label: '成品净需求', tone: 'info' },
    'finished-good-net-requirement': { label: '成品净需求', tone: 'info' },
    component: { label: '组件展开', tone: 'neutral' },
    'component-net-requirement': { label: '组件展开', tone: 'neutral' },
  }
  return map[value ?? ''] ?? { label: '需求展开', tone: 'neutral' }
}
// 父项=组件 或 无组件 → 视为顶层（成品行，不缩进）；有不同组件 → 缩进。
function isComponentRow(row: BusinessConsoleMrpPeggingItem): boolean {
  return !!row.componentSkuCode && row.componentSkuCode !== row.parentSkuCode
}

// 计划建议分型筛选后的可见行。
const visibleSuggestions = computed(() => {
  const t = suggestionTypeFilter.type
  // 'all' 哨兵 = 不过滤（reka 的 SelectItem 不允许空串 value，故用 'all' 代替原空串）。
  if (!t || t === 'all') return suggestions.value
  return suggestions.value.filter((s) => s.suggestionType === t)
})
const suggestionTypeFilterOptions = [
  { label: '全部类型', value: 'all' },
  { label: '生产建议 (→MES)', value: 'planned-work-order' },
  { label: '采购建议 (→ERP)', value: 'planned-purchase' },
  { label: '提前调整', value: 'reschedule-in' },
  { label: '延期调整', value: 'reschedule-out' },
  { label: '取消计划收货', value: 'cancel' },
]

const demandColumns: NvDataTableColumn<BusinessConsoleDemandSourceItem>[] = [
  { key: 'sourceReference', header: '来源', cellClass: 'font-medium' },
  { key: 'demandType', header: '类型', width: 'w-24' },
  { key: 'sourceStatus', header: '状态', width: 'w-24' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'siteCode', header: '工厂' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
  { key: 'dueDate', header: '需求日', width: 'w-32' },
  { key: 'urgency', header: '紧迫度', width: 'w-28' },
  { key: 'coverage', header: '覆盖', width: 'w-28' },
]
const mpsColumns: NvDataTableColumn<BusinessConsoleMpsBucketItem>[] = [
  { key: 'bucketDate', header: '计划桶', cellClass: 'font-medium', width: 'w-32' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'siteCode', header: '工厂', width: 'w-40' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
  { key: 'status', header: '状态', width: 'w-28' },
  { key: 'reviewRelease', header: '评审/发布', width: 'w-40' },
  { key: 'actions', header: '', align: 'end', width: 'w-44' },
]
const runColumns: NvDataTableColumn<BusinessConsoleMrpRunItem>[] = [
  // runId 是 GUID，不显裸 GUID；以「计划范围」(horizon) 作人读锚点，追溯按钮内部用 runId。
  { key: 'horizon', header: '计划范围', cellClass: 'font-medium' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'inputSources', header: '输入来源', width: 'w-52' },
  { key: 'inputCoverage', header: '输入覆盖', width: 'w-40' },
  { key: 'demandCount', header: '覆盖需求', align: 'end', width: 'w-24' },
  { key: 'inputDegradationSources', header: '输入状态', width: 'w-36' },
  { key: 'suggestionCount', header: '建议', align: 'end', width: 'w-20' },
  { key: 'coverage', header: '覆盖率', align: 'end', width: 'w-24' },
  { key: 'availabilityCount', header: '库存快照', align: 'end', width: 'w-24' },
  { key: 'actions', header: '', align: 'end', width: 'w-24' },
]
const peggingColumns: NvDataTableColumn<BusinessConsoleMrpPeggingItem>[] = [
  { key: 'demandSourceReference', header: '需求来源', cellClass: 'font-medium' },
  { key: 'peggingType', header: '展开类型', width: 'w-28' },
  { key: 'sourceType', header: '来源类型', width: 'w-28' },
  { key: 'sku', header: '物料层级' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-24' },
  { key: 'engineeringRef', header: '工程引用' },
]
const suggestionColumns: NvDataTableColumn<BusinessConsolePlanningSuggestionItem>[] = [
  // suggestionId 是 GUID 且无人读号；不显裸 GUID，行由「类型 + SKU + 数量 + 原因」自识别。
  { key: 'suggestionType', header: '类型', width: 'w-28', cellClass: 'font-medium' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
  { key: 'requiredDate', header: '需求日', width: 'w-28' },
  { key: 'reasonCode', header: '原因' },
  { key: 'downstream', header: '承接单据', width: 'w-40' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '', align: 'end', width: 'w-32' },
]

async function submitDemand() {
  await createOrUpdateDemand()
  demandOpen.value = false
}
async function submitMpsBucket() {
  await createMpsBucket()
  mpsOpen.value = false
}
async function submitMrpRun() {
  await runMrp()
  mrpOpen.value = false
}
async function reviewMps(row: BusinessConsoleMpsBucketItem) {
  if (!row.mpsId) return
  await reviewMpsBucket(row.mpsId)
  notifySuccess('MPS bucket 已完成评审。')
}
async function releaseMps(row: BusinessConsoleMpsBucketItem) {
  if (!row.mpsId) return
  await releaseMpsBucket(row.mpsId)
  notifySuccess('MPS bucket 已发布，可作为 MRP 输入。')
}
async function acceptPlanningSuggestion(row: BusinessConsolePlanningSuggestionItem) {
  if (!row.suggestionId || !row.suggestionType) return
  if (!isAcceptableSuggestion(row.suggestionType)) return
  if (acceptingSuggestionId.value) return
  acceptingSuggestionId.value = row.suggestionId
  try {
    const response = await acceptSuggestion({
      suggestionId: row.suggestionId,
      suggestionType: row.suggestionType,
    })
    const reference = response.data?.downstreamDocumentId
    notifySuccess(
      reference
        ? `计划建议已承接到 ${downstreamLabel(response.data?.downstreamService, response.data?.downstreamDocumentType)}「${reference}」。`
        : '计划建议已接受。',
    )
    if (reference) {
      await router.push(
        downstreamRoute(
          response.data?.downstreamService,
          response.data?.downstreamDocumentType,
          reference,
        ),
      )
    }
  } catch (error) {
    notifyError(error, '计划建议接受失败，请检查生产版本、供应商、库存或权限状态。')
  } finally {
    acceptingSuggestionId.value = null
  }
}

function planningStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'accepted' || s === 'completed')
    return { label: s === 'accepted' ? '已接受' : '已完成', tone: 'success' }
  if (s === 'running' || s === 'inprogress') return { label: '运行中', tone: 'info' }
  if (s === 'failed') return { label: '失败', tone: 'danger' }
  if (s === 'open' || s === 'pending') return { label: '待评审', tone: 'warning' }
  return { label: status || '未知', tone: 'neutral' }
}
function mpsStatus(status?: string | null): { label: string; tone: StatusTone } {
  const s = (status ?? '').toLowerCase()
  if (s === 'released') return { label: '已发布', tone: 'success' }
  if (s === 'reviewed') return { label: '已评审', tone: 'info' }
  if (s === 'draft') return { label: '草稿', tone: 'neutral' }
  return { label: status || '未知', tone: 'neutral' }
}
function isMpsReleased(status?: string | null) {
  return status?.toLowerCase() === 'released'
}
function demandTypeLabel(value?: string | null) {
  return (
    (
      { forecast: '预测', 'safety-stock': '安全库存', 'sales-order': '销售订单' } as Record<
        string,
        string
      >
    )[value ?? ''] ??
    (value || '未指定')
  )
}
function suggestionTypeLabel(value?: string | null) {
  return (
    (
      {
        'planned-purchase': '采购建议',
        'planned-work-order': '生产建议',
        'reschedule-in': '提前调整',
        'reschedule-out': '延期调整',
        cancel: '取消收货',
      } as Record<string, string>
    )[value ?? ''] ??
    (value || '未指定')
  )
}
function suggestionTypeTone(value?: string | null): StatusTone {
  if (value === 'planned-work-order') return 'info'
  if (value === 'planned-purchase') return 'neutral'
  if (value === 'reschedule-in' || value === 'reschedule-out') return 'warning'
  if (value === 'cancel') return 'danger'
  return 'neutral'
}
function isAcceptableSuggestion(value?: string | null) {
  return value === 'planned-work-order' || value === 'planned-purchase'
}
function reasonLabel(value?: string | null) {
  const map: Record<string, string> = {
    inventory_shortage: '库存不足',
    material_shortage: '物料不足',
    demand_pegging: '需求驱动',
    safety_stock: '安全库存',
    'finished-good-net-requirement': '成品净需求',
    'component-net-requirement': '组件净需求',
    'safety-stock-replenishment': '安全库存补充',
  }
  // 未知码一律降级为通用中文，绝不回显原始英文码。
  return map[value ?? ''] ?? '按计划规则形成'
}
function sourceTypeLabel(value?: string | null) {
  const map: Record<string, string> = {
    sales: '销售来源',
    'sales-order': '销售来源',
    forecast: '预测来源',
    safety: '安全库存来源',
    'safety-stock': '安全库存来源',
    mps: 'MPS 来源',
    component: '组件展开',
    demand: '需求来源',
    'scheduled-receipt': '在途来源',
  }
  return map[(value ?? '').toLowerCase()] ?? '来源未分类'
}
function isOpen(status?: string | null) {
  return status?.toLowerCase() === 'open'
}
function downstreamLabel(service?: string | null, type?: string | null) {
  if (service === 'BusinessMes' && type === 'WorkOrder') return 'MES 工单'
  if (service === 'BusinessErp' && type === 'PurchaseRequisition') return 'ERP 采购申请'
  return '下游单据'
}
function downstreamRoute(
  service: string | null | undefined,
  type: string | null | undefined,
  documentId: string,
) {
  if (service === 'BusinessMes' && type === 'WorkOrder') {
    return { path: `/mes/work-orders/${encodeURIComponent(documentId)}` }
  }

  return {
    path: '/erp',
    query: { keyword: documentId },
  }
}
function formatDate(value?: string | null) {
  return value ? value.slice(0, 10) : '-'
}
function formatQuantity(value?: number | null, uom?: string | null) {
  return `${value ?? 0} ${uom ?? ''}`.trim()
}
function formatRatio(value?: number | null) {
  if (value === null || value === undefined) return '—'
  return value === 1 ? '1' : value.toString()
}
function inputDegradationLabel(sources?: readonly string[] | null) {
  return sources && sources.length > 0
    ? sources.map(inputDegradationSourceLabel).join('、')
    : '正常'
}
function inputDegradationSourceLabel(source: string) {
  return (
    (
      {
        'scheduled-receipts': '在途到货',
        'master-data-planning-parameters': '主数据规划参数',
      } as Record<string, string>
    )[source] ?? source
  )
}
function inputSourcesLabel(sources?: readonly string[] | null) {
  return sources && sources.length > 0 ? sources.map(inputSourceLabel).join('、') : '未记录'
}
function inputSourceLabel(source: string) {
  return (
    (
      {
        mps: 'MPS 主计划',
        'demand-source': '需求池',
        'sales-order': '销售需求',
        forecast: '预测',
        'safety-stock': '安全库存',
      } as Record<string, string>
    )[source] ?? source
  )
}
function inputCoverageLabel(run: BusinessConsoleMrpRunItem) {
  const start = run.inputCoverageStart ?? run.horizonStart
  const end = run.inputCoverageEnd ?? run.horizonEnd
  return `${formatDate(start)} ~ ${formatDate(end)}`
}

function parseVersionReference(reference?: string | null) {
  if (!reference) return {}
  const separator = reference.lastIndexOf(':')
  if (separator <= 0 || separator >= reference.length - 1) return {}
  return {
    bomCode: reference.slice(0, separator),
    revision: reference.slice(separator + 1),
  }
}

function bomAnalysisQuery(row: BusinessConsoleMrpPeggingItem) {
  return {
    kind: 'manufacturing',
    view: 'tree',
    skuCode: row.parentSkuCode ?? row.componentSkuCode ?? '',
    componentCode:
      row.componentSkuCode && row.componentSkuCode !== row.parentSkuCode
        ? row.componentSkuCode
        : undefined,
    effectiveDate: selectedRun.value?.horizonStart ?? new Date().toISOString().slice(0, 10),
    lotSize: String(row.quantity ?? 1),
    ...parseVersionReference(row.manufacturingBomReference),
  }
}

function openBomContext(row: BusinessConsoleMrpPeggingItem) {
  void router.push({
    path: '/engineering/bom-analysis',
    query: bomAnalysisQuery(row),
  })
}

function explanationRows(row: BusinessConsolePlanningSuggestionItem) {
  const explanation = row.netRequirementExplanation
  if (!explanation) return []
  const grossDemandLabel = explanation.primarySourceType === 'component' ? '组件毛需求' : '总需求'
  return [
    { label: grossDemandLabel, value: explanation.grossDemandQuantity },
    { label: '现有库存', value: explanation.onHandQuantity },
    { label: '预留', value: explanation.reservedQuantity },
    { label: '可抵扣库存', value: explanation.availableToNetQuantity },
    { label: '在途', value: explanation.scheduledReceiptQuantity },
    { label: '安全库存', value: explanation.safetyStockQuantity },
    { label: '净需求', value: explanation.netRequirementQuantity },
    { label: '建议量', value: explanation.plannedQuantity },
  ]
}

function openPeggingSource(row: BusinessConsoleMrpPeggingItem) {
  const sourceType = (row.sourceType ?? '').toLowerCase()
  if (
    sourceType === 'component' ||
    row.manufacturingBomReference ||
    row.productionVersionReference
  ) {
    openBomContext(row)
    return
  }

  if (sourceType === 'scheduled-receipt') {
    void router.push({ path: '/erp', query: { keyword: row.demandSourceReference } })
    return
  }

  void router.push({
    path: '/planning',
    query: {
      source: row.demandSourceReference,
      runId: runSelection.runId,
    },
  })
}

function openSalesOrderDemand(row: BusinessConsoleDemandSourceItem) {
  void router.push({
    path: '/erp/sales/orders',
    query: { keyword: row.sourceReference },
  })
}
</script>

<template>
  <NvPageHeader title="需求与计划" :breadcrumbs="[{ label: '需求与计划' }]">
    <template #actions>
      <NvButton
        size="sm"
        type="button"
        variant="outline"
        :disabled="demandsPending"
        @click="refreshPlanning"
      >
        <RefreshCwIcon aria-hidden="true" />
        刷新
      </NvButton>

      <NvDialog v-model:open="mrpOpen">
        <NvDialogTrigger as-child>
          <NvButton size="sm" type="button" variant="outline">
            <PlayIcon aria-hidden="true" />
            运行 MRP
          </NvButton>
        </NvDialogTrigger>
        <NvDialogContent>
          <NvDialogHeader>
            <NvDialogTitle>运行 MRP</NvDialogTitle>
            <NvDialogDescription
              >按计划周期汇总已发布
              MPS、销售需求、预测和安全库存，生成生产与采购建议。</NvDialogDescription
            >
          </NvDialogHeader>
          <form class="grid gap-4" @submit.prevent="submitMrpRun">
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField>
                <NvFieldLabel>开始日期</NvFieldLabel>
                <NvDatePicker
                  v-model="runRequest.horizonStart"
                  placeholder="选择开始日期"
                  class="w-full"
                />
              </NvField>
              <NvField>
                <NvFieldLabel>结束日期</NvFieldLabel>
                <NvDatePicker
                  v-model="runRequest.horizonEnd"
                  placeholder="选择结束日期"
                  class="w-full"
                />
              </NvField>
            </NvFieldGroup>
            <NvDialogFooter>
              <NvButton type="button" variant="outline" @click="mrpOpen = false">取消</NvButton>
              <NvButton type="submit" :disabled="runMrpPending">
                <Spinner v-if="runMrpPending" aria-hidden="true" />
                运行
              </NvButton>
            </NvDialogFooter>
          </form>
        </NvDialogContent>
      </NvDialog>

      <NvDialog v-model:open="mpsOpen">
        <NvDialogTrigger as-child>
          <NvButton size="sm" type="button" variant="outline">
            <PlusIcon aria-hidden="true" />
            新建 MPS
          </NvButton>
        </NvDialogTrigger>
        <NvDialogContent class="sm:max-w-2xl">
          <NvDialogHeader>
            <NvDialogTitle>新建 MPS bucket</NvDialogTitle>
            <NvDialogDescription
              >维护主生产计划 bucket，评审并发布后进入 MRP 输入。</NvDialogDescription
            >
          </NvDialogHeader>
          <form class="grid gap-4" @submit.prevent="submitMpsBucket">
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField>
                <NvFieldLabel for="mps-sku">SKU</NvFieldLabel>
                <NvSelect v-model="mpsForm.skuCode">
                  <NvSelectTrigger id="mps-sku"
                    ><NvSelectValue placeholder="选择 SKU"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="mps-site">工厂</NvFieldLabel>
                <NvSelect v-model="mpsForm.siteCode">
                  <NvSelectTrigger id="mps-site"
                    ><NvSelectValue placeholder="选择工厂"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in siteOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="mps-uom">单位</NvFieldLabel>
                <NvSelect v-model="mpsForm.uomCode">
                  <NvSelectTrigger id="mps-uom"
                    ><NvSelectValue placeholder="选择单位"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="mps-qty">计划数量</NvFieldLabel>
                <NvInput
                  id="mps-qty"
                  v-model.number="mpsForm.quantity"
                  min="0.0001"
                  step="0.0001"
                  type="number"
                />
              </NvField>
              <NvField>
                <NvFieldLabel>计划桶日期</NvFieldLabel>
                <NvDatePicker
                  v-model="mpsForm.bucketDate"
                  placeholder="选择计划日期"
                  class="w-full"
                />
              </NvField>
            </NvFieldGroup>
            <NvDialogFooter>
              <NvButton type="button" variant="outline" @click="mpsOpen = false">取消</NvButton>
              <NvButton type="submit" :disabled="createMpsBucketPending || !canSubmitMps">
                <Spinner v-if="createMpsBucketPending" aria-hidden="true" />
                保存 MPS
              </NvButton>
            </NvDialogFooter>
          </form>
        </NvDialogContent>
      </NvDialog>

      <NvDialog v-model:open="demandOpen">
        <NvDialogTrigger as-child>
          <NvButton size="sm" type="button">
            <PlusIcon aria-hidden="true" />
            新建需求
          </NvButton>
        </NvDialogTrigger>
        <NvDialogContent class="sm:max-w-2xl">
          <NvDialogHeader>
            <NvDialogTitle>新建需求</NvDialogTitle>
            <NvDialogDescription
              >录入销售订单、预测或安全库存需求，作为 MRP 与计划建议的来源。</NvDialogDescription
            >
          </NvDialogHeader>
          <form class="grid gap-4" @submit.prevent="submitDemand">
            <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
              <NvField>
                <NvFieldLabel>需求类型</NvFieldLabel>
                <NvSelect v-model="demandForm.demandType">
                  <NvSelectTrigger aria-label="需求类型"><NvSelectValue /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in demandTypeOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="demand-source">来源单号</NvFieldLabel>
                <NvInput
                  id="demand-source"
                  v-model="demandForm.sourceReference"
                  placeholder="SO-2026-001"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="demand-sku">SKU</NvFieldLabel>
                <NvSelect v-model="demandForm.skuCode">
                  <NvSelectTrigger id="demand-sku"
                    ><NvSelectValue placeholder="选择 SKU"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in skuOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="demand-site">工厂</NvFieldLabel>
                <NvSelect v-model="demandForm.siteCode">
                  <NvSelectTrigger id="demand-site"
                    ><NvSelectValue placeholder="选择工厂"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in siteOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="demand-uom">单位</NvFieldLabel>
                <NvSelect v-model="demandForm.uomCode">
                  <NvSelectTrigger id="demand-uom"
                    ><NvSelectValue placeholder="选择单位"
                  /></NvSelectTrigger>
                  <NvSelectContent>
                    <NvSelectItem v-for="o in uomOptions" :key="o.value" :value="o.value">{{
                      o.label
                    }}</NvSelectItem>
                  </NvSelectContent>
                </NvSelect>
              </NvField>
              <NvField>
                <NvFieldLabel for="demand-qty">数量</NvFieldLabel>
                <NvInput
                  id="demand-qty"
                  v-model.number="demandForm.quantity"
                  min="0.0001"
                  step="0.0001"
                  type="number"
                />
              </NvField>
              <NvField>
                <NvFieldLabel>需求日期</NvFieldLabel>
                <NvDatePicker
                  v-model="demandForm.dueDate"
                  placeholder="选择需求日期"
                  class="w-full"
                />
              </NvField>
            </NvFieldGroup>
            <NvDialogFooter>
              <NvButton type="button" variant="outline" @click="demandOpen = false">取消</NvButton>
              <NvButton type="submit" :disabled="createDemandPending || !canSubmitDemand">
                <Spinner v-if="createDemandPending" aria-hidden="true" />
                保存需求
              </NvButton>
            </NvDialogFooter>
          </form>
        </NvDialogContent>
      </NvDialog>
    </template>
  </NvPageHeader>

  <NvSectionCards :columns="4">
    <NvSectionCard
      description="MPS 已发布"
      :value="releasedMpsCount"
      footnote="bucket 可进入 MRP"
      :hint="`总计 ${mpsBuckets.length} 个主计划 bucket`"
    />
    <NvSectionCard
      description="待评审建议"
      :value="openSuggestionCount"
      footnote="条计划建议待接受"
      :hint="reviewKpiHint"
    />
    <NvSectionCard
      description="需求覆盖 (SKU)"
      :value="demandSkuKpi"
      footnote="已生成建议 / 需求 SKU 数"
      hint="按 SKU 去重统计"
    />
    <NvSectionCard
      description="最近一次 MRP"
      :value="latestRunKpiValue"
      :footnote="latestRun ? runHorizonLabel(latestRun) : '—'"
      :hint="latestRunKpiHint"
    />
  </NvSectionCards>

  <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

  <NvTabs default-value="demands">
    <NvTabsList>
      <NvTabsTrigger value="demands">需求池 ({{ demands.length }})</NvTabsTrigger>
      <NvTabsTrigger value="mps">MPS 主计划 ({{ mpsBuckets.length }})</NvTabsTrigger>
      <NvTabsTrigger value="runs">MRP 运行 ({{ mrpRuns.length }})</NvTabsTrigger>
      <NvTabsTrigger value="suggestions">计划建议 ({{ suggestions.length }})</NvTabsTrigger>
    </NvTabsList>

    <NvTabsContent value="demands">
      <NvDataTable
        :columns="demandColumns"
        :rows="demands"
        row-key="demandSourceId"
        :loading="demandsPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前范围没有计划需求。"
      >
        <template #cell-sourceReference="{ row }">
          <NvButton
            v-if="row.demandType === 'sales-order'"
            type="button"
            size="sm"
            variant="ghost"
            :aria-label="`查看销售订单 ${row.sourceReference}`"
            @click="openSalesOrderDemand(row)"
          >
            {{ row.sourceReference }}
            <ExternalLinkIcon aria-hidden="true" />
          </NvButton>
          <span v-else>{{ row.sourceReference }}</span>
        </template>
        <template #cell-demandType="{ row }">{{ demandTypeLabel(row.demandType) }}</template>
        <template #cell-sourceStatus="{ row }">
          <NvStatusBadge
            :label="row.sourceStatus === 'cancelled' ? '已取消' : '有效'"
            :tone="row.sourceStatus === 'cancelled' ? 'neutral' : 'success'"
          />
        </template>
        <template #cell-skuCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ skuLabel(row.skuCode) }}</span>
            <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
          </div>
        </template>
        <template #cell-siteCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ siteLabel(row.siteCode) }}</span>
            <span v-if="row.siteCode" class="text-xs text-muted-foreground">{{
              row.siteCode
            }}</span>
          </div>
        </template>
        <template #cell-quantity="{ row }"
          ><span class="tabular-nums">{{
            formatQuantity(row.quantity, row.uomCode)
          }}</span></template
        >
        <template #cell-dueDate="{ row }">{{ formatDate(row.dueDate) }}</template>
        <template #cell-urgency="{ row }"
          ><NvStatusBadge
            :label="dueUrgency(row.dueDate).label"
            :tone="dueUrgency(row.dueDate).tone"
        /></template>
        <template #cell-coverage="{ row }"
          ><NvStatusBadge
            :label="demandCoverage(row.skuCode).label"
            :tone="demandCoverage(row.skuCode).tone"
        /></template>
      </NvDataTable>
    </NvTabsContent>

    <NvTabsContent value="mps">
      <NvDataTable
        :columns="mpsColumns"
        :rows="mpsBuckets"
        row-key="mpsId"
        :loading="mpsBucketsPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前范围没有 MPS bucket。"
      >
        <template #cell-bucketDate="{ row }">{{ formatDate(row.bucketDate) }}</template>
        <template #cell-skuCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ skuLabel(row.skuCode) }}</span>
            <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
          </div>
        </template>
        <template #cell-siteCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ siteLabel(row.siteCode) }}</span>
            <span v-if="row.siteCode" class="text-xs text-muted-foreground">{{
              row.siteCode
            }}</span>
          </div>
        </template>
        <template #cell-quantity="{ row }"
          ><span class="tabular-nums">{{
            formatQuantity(row.quantity, row.uomCode)
          }}</span></template
        >
        <template #cell-status="{ row }"
          ><NvStatusBadge :label="mpsStatus(row.status).label" :tone="mpsStatus(row.status).tone"
        /></template>
        <template #cell-reviewRelease="{ row }">
          <div class="flex min-w-0 flex-col gap-0.5 text-sm">
            <span>{{ row.reviewedBy ? `评审 ${row.reviewedBy}` : '未评审' }}</span>
            <span class="text-muted-foreground">{{
              row.releasedBy ? `发布 ${row.releasedBy}` : '未发布'
            }}</span>
          </div>
        </template>
        <template #cell-actions="{ row }">
          <div class="flex justify-end gap-2">
            <NvButton
              v-if="row.status?.toLowerCase() === 'draft'"
              size="sm"
              type="button"
              variant="outline"
              :disabled="reviewMpsBucketPending"
              @click="reviewMps(row)"
            >
              <Spinner v-if="reviewMpsBucketPending" aria-hidden="true" />
              <CheckIcon v-else aria-hidden="true" />
              评审
            </NvButton>
            <NvButton
              v-if="row.status?.toLowerCase() === 'reviewed'"
              size="sm"
              type="button"
              variant="outline"
              :disabled="releaseMpsBucketPending"
              @click="releaseMps(row)"
            >
              <Spinner v-if="releaseMpsBucketPending" aria-hidden="true" />
              <CheckIcon v-else aria-hidden="true" />
              发布
            </NvButton>
          </div>
        </template>
      </NvDataTable>
    </NvTabsContent>

    <NvTabsContent value="runs" class="grid gap-4">
      <NvDataTable
        :columns="runColumns"
        :rows="mrpRuns"
        row-key="runId"
        :loading="mrpRunsPending"
        :searchable="false"
        :column-settings="false"
        empty-message="尚未运行 MRP。"
      >
        <template #cell-horizon="{ row }"
          >{{ formatDate(row.horizonStart) }} ~ {{ formatDate(row.horizonEnd) }}</template
        >
        <template #cell-status="{ row }"
          ><NvStatusBadge
            :label="planningStatus(row.status).label"
            :tone="planningStatus(row.status).tone"
        /></template>
        <template #cell-inputSources="{ row }">{{ inputSourcesLabel(row.inputSources) }}</template>
        <template #cell-inputCoverage="{ row }">{{ inputCoverageLabel(row) }}</template>
        <template #cell-demandCount="{ row }"
          ><span class="tabular-nums">{{ row.demandCount ?? 0 }}</span></template
        >
        <template #cell-inputDegradationSources="{ row }">
          <NvStatusBadge
            :label="inputDegradationLabel(row.inputDegradationSources)"
            :tone="row.hasInputDegradation ? 'warning' : 'success'"
          />
        </template>
        <template #cell-suggestionCount="{ row }"
          ><span class="tabular-nums">{{ row.suggestionCount ?? 0 }}</span></template
        >
        <template #cell-coverage="{ row }"
          ><span class="tabular-nums font-medium">{{ coverageRate(row) }}</span></template
        >
        <template #cell-availabilityCount="{ row }"
          ><span class="tabular-nums">{{ row.availabilityCount ?? 0 }}</span></template
        >
        <template #cell-actions="{ row }">
          <NvButton
            size="sm"
            type="button"
            :variant="runSelection.runId === row.runId ? 'secondary' : 'ghost'"
            @click="runSelection.runId = row.runId ?? ''"
          >
            查看追溯
          </NvButton>
        </template>
      </NvDataTable>

      <div class="grid gap-2">
        <div class="flex items-center gap-2">
          <span class="text-sm font-medium text-foreground">需求追溯</span>
          <span class="text-sm text-muted-foreground">{{ runHorizonLabel(selectedRun) }}</span>
          <NvStatusBadge
            v-if="selectedRun"
            :label="planningStatus(selectedRun.status).label"
            :tone="planningStatus(selectedRun.status).tone"
          />
        </div>
        <NvDataTable
          :columns="peggingColumns"
          :rows="pegging"
          :row-key="(r) => `${r.suggestionId}:${r.componentSkuCode}`"
          :loading="peggingPending"
          :searchable="false"
          :column-settings="false"
          empty-message="选择一条 MRP 运行查看需求与物料来源。"
        >
          <template #cell-peggingType="{ row }">
            <NvStatusBadge
              :label="peggingTypeLabel(row.peggingType).label"
              :tone="peggingTypeLabel(row.peggingType).tone"
            />
          </template>
          <template #cell-sourceType="{ row }">
            <NvStatusBadge :label="sourceTypeLabel(row.sourceType)" tone="neutral" />
          </template>
          <template #cell-demandSourceReference="{ row }">
            <div class="flex items-center justify-between gap-2">
              <span class="min-w-0 truncate">{{ row.demandSourceReference }}</span>
              <NvButton size="sm" type="button" variant="ghost" @click="openPeggingSource(row)">
                <ExternalLinkIcon aria-hidden="true" />
              </NvButton>
            </div>
          </template>
          <template #cell-sku="{ row }">
            <div
              :class="
                isComponentRow(row) ? 'flex items-start gap-1.5 pl-5' : 'flex flex-col gap-0.5'
              "
            >
              <CornerDownRightIcon
                v-if="isComponentRow(row)"
                class="mt-0.5 size-3.5 shrink-0 text-muted-foreground"
                aria-hidden="true"
              />
              <div class="flex flex-col gap-0.5">
                <template v-if="isComponentRow(row)">
                  <span>{{ skuLabel(row.componentSkuCode) }}</span>
                  <span class="text-xs text-muted-foreground">
                    {{ row.componentSkuCode }} · 属 {{ skuLabel(row.parentSkuCode) }}
                  </span>
                </template>
                <template v-else>
                  <span class="font-medium">{{ skuLabel(row.parentSkuCode) }}</span>
                  <span v-if="row.parentSkuCode" class="text-xs text-muted-foreground">{{
                    row.parentSkuCode
                  }}</span>
                </template>
              </div>
            </div>
          </template>
          <template #cell-quantity="{ row }"
            ><span class="tabular-nums">{{ row.quantity ?? 0 }}</span></template
          >
          <template #cell-engineeringRef="{ row }">
            <div class="flex items-center justify-between gap-2">
              <div class="flex min-w-0 flex-col gap-0.5">
                <span>{{
                  row.manufacturingBomReference ?? row.productionVersionReference ?? '—'
                }}</span>
                <span v-if="row.routingReference" class="text-xs text-muted-foreground"
                  >工艺 {{ row.routingReference }}</span
                >
              </div>
              <NvButton
                v-if="row.parentSkuCode || row.componentSkuCode"
                size="sm"
                type="button"
                variant="ghost"
                @click="openBomContext(row)"
              >
                <NetworkIcon aria-hidden="true" />
                查看 BOM
              </NvButton>
            </div>
          </template>
        </NvDataTable>
      </div>
    </NvTabsContent>

    <NvTabsContent value="suggestions" class="grid gap-3">
      <div class="flex flex-wrap items-center gap-2">
        <NvSelect v-model="suggestionTypeFilter.type">
          <NvSelectTrigger class="h-9 w-44" aria-label="建议分型"
            ><NvSelectValue placeholder="全部类型"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="o in suggestionTypeFilterOptions"
              :key="o.value"
              :value="o.value"
              >{{ o.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <span class="text-sm text-muted-foreground"> 生产建议 → MES · 采购建议 → ERP </span>
        <div class="ms-auto flex items-center gap-2">
          <NvSelect v-model="suggestionFilters.status">
            <NvSelectTrigger class="h-9 w-32" aria-label="建议状态"
              ><NvSelectValue placeholder="建议状态"
            /></NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem v-for="o in suggestionStatusOptions" :key="o.value" :value="o.value">{{
                o.label
              }}</NvSelectItem>
            </NvSelectContent>
          </NvSelect>
        </div>
      </div>
      <NvDataTable
        :columns="suggestionColumns"
        :rows="visibleSuggestions"
        row-key="suggestionId"
        :loading="suggestionsPending"
        :searchable="false"
        :column-settings="false"
        empty-message="当前范围没有计划建议。"
      >
        <template #cell-suggestionType="{ row }">
          <NvStatusBadge
            :label="suggestionTypeLabel(row.suggestionType)"
            :tone="suggestionTypeTone(row.suggestionType)"
          />
        </template>
        <template #cell-skuCode="{ row }">
          <div class="flex flex-col gap-0.5">
            <span>{{ skuLabel(row.skuCode) }}</span>
            <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
          </div>
        </template>
        <template #cell-quantity="{ row }"
          ><span class="tabular-nums">{{
            formatQuantity(row.quantity, row.uomCode)
          }}</span></template
        >
        <template #cell-requiredDate="{ row }">{{ formatDate(row.requiredDate) }}</template>
        <template #cell-reasonCode="{ row }">
          <div class="grid min-w-80 gap-2">
            <div class="flex flex-wrap items-center gap-2">
              <span>{{ reasonLabel(row.reasonCode) }}</span>
              <NvStatusBadge
                v-if="row.netRequirementExplanation"
                :label="sourceTypeLabel(row.netRequirementExplanation.primarySourceType)"
                tone="info"
              />
            </div>
            <div
              v-if="row.netRequirementExplanation"
              class="grid gap-2 rounded-md border border-border/70 bg-muted/30 p-2 text-xs"
            >
              <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
                <span class="font-medium text-foreground">净需求公式</span>
                <span class="font-mono tabular-nums text-muted-foreground">{{
                  row.netRequirementExplanation.formula
                }}</span>
              </div>
              <div class="grid grid-cols-2 gap-1 sm:grid-cols-4">
                <div
                  v-for="item in explanationRows(row)"
                  :key="item.label"
                  class="min-w-0 rounded border border-border/60 bg-background px-2 py-1"
                >
                  <div class="text-muted-foreground">{{ item.label }}</div>
                  <div class="tabular-nums text-foreground">{{ item.value ?? 0 }}</div>
                </div>
              </div>
              <div class="flex flex-wrap gap-x-4 gap-y-1 text-muted-foreground">
                <span>scrap {{ formatRatio(row.netRequirementExplanation.scrapRate) }}</span>
                <span>yield {{ formatRatio(row.netRequirementExplanation.yieldRate) }}</span>
                <span
                  v-if="
                    row.netRequirementExplanation.primarySourceType === 'component' &&
                    (row.netRequirementExplanation.scrapRate ||
                      row.netRequirementExplanation.yieldRate !== 1)
                  "
                  >scrap/yield 已计入组件毛需求</span
                >
                <span v-if="row.netRequirementExplanation.uomConversions?.length"
                  >单位 {{ row.netRequirementExplanation.uomConversions.join('；') }}</span
                >
                <span v-if="row.netRequirementExplanation.degradationSources?.length"
                  >退化
                  {{
                    inputDegradationLabel(row.netRequirementExplanation.degradationSources)
                  }}</span
                >
              </div>
            </div>
          </div>
        </template>
        <template #cell-downstream="{ row }">
          <NvButton
            v-if="row.downstreamDocumentId"
            size="sm"
            type="button"
            variant="ghost"
            @click="
              router.push(
                downstreamRoute(
                  row.downstreamService,
                  row.downstreamDocumentType,
                  row.downstreamDocumentId,
                ),
              )
            "
          >
            <ExternalLinkIcon aria-hidden="true" />
            {{ row.downstreamDocumentId }}
          </NvButton>
          <span v-else class="text-sm text-muted-foreground">未承接</span>
        </template>
        <template #cell-status="{ row }"
          ><NvStatusBadge
            :label="planningStatus(row.status).label"
            :tone="planningStatus(row.status).tone"
        /></template>
        <template #cell-actions="{ row }">
          <NvButton
            v-if="isOpen(row.status) && isAcceptableSuggestion(row.suggestionType)"
            size="sm"
            type="button"
            variant="outline"
            :disabled="acceptingSuggestionId === row.suggestionId"
            @click="acceptPlanningSuggestion(row)"
          >
            <Spinner v-if="acceptingSuggestionId === row.suggestionId" aria-hidden="true" />
            <CheckIcon v-else aria-hidden="true" />
            接受
          </NvButton>
          <span v-else-if="isOpen(row.status)" class="text-sm text-muted-foreground"
            >异常待处理</span
          >
        </template>
      </NvDataTable>
    </NvTabsContent>
  </NvTabs>
</template>
