import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleMpsBucketItem,
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricSegment, StatusTone } from '@nerv-iip/ui'
import { useBusinessSkus, useBusinessMasterDataResources } from '@/composables/master-data'
import {
  SUGGESTION_REJECT_REASON_MAX_LENGTH,
  useBusinessPlanning,
} from '@/composables/planning/useBusinessPlanning'
import { toBaseUomBySku } from '@/composables/master-data/skuBaseUom'
import { isMesWorkOrderDownstream } from '@/composables/planning/fulfillment'
import { useMasterDataDisplayNames } from '@/composables/master-data/useMasterDataDisplayNames'
import { useOrderUrgencies } from '@/composables/urgency/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/urgency/useUrgencyDisplayMode'
import { coveredDemandSkuCodes } from '@/components/planning/planningAggregation'
import { useCanScheduleSingleOrder } from '@/composables/scheduling/useSingleOrderScheduling'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { computed, shallowRef, watch, watchEffect } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useCanReadMaterialDeliveries } from '@/composables/planning/useBusinessPlanningMaterialDeliveries'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import {
  runHorizonLabel,
  planningStatus,
  isMpsReleased,
  isAcceptableSuggestion,
  isOpen,
  downstreamLabel,
  formatDate,
  inputSourcesLabel,
  parseVersionReference,
} from './planningFormatters'
import { usePlanningDemandFilters } from './usePlanningDemandFilters'
import { usePlanningMrpRunDialog } from './usePlanningMrpRunDialog'
import { usePlanningSuggestionLocation } from './usePlanningSuggestionLocation'

export function usePlanningWorkbenchController() {
  const route = useRoute()
  const sourceOrder = computed(() =>
    typeof route.query.demandSource === 'string' ? route.query.demandSource.trim() : '',
  )
  const downstreamPage = shallowRef<readonly BusinessConsolePlanningSuggestionItem[]>([])
  const {
    acceptSuggestion,
    activeMrpRun,
    acceptSuggestionPending,
    createMpsBucket,
    createMpsBucketPending,
    createDemandPending,
    createOrUpdateDemand,
    cancelDemand,
    cancelDemandPending,
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
    updateMpsBucket,
    updateMpsBucketPending,
    releaseMpsBucket,
    releaseMpsBucketPending,
    reviewMpsBucket,
    reviewMpsBucketPending,
    pegging,
    peggingPending,
    refreshPlanning,
    rejectSuggestion,
    rejectSuggestionPending,
    runMrp,
    runMrpPending,
    runRequest,
    runSelection,
    suggestionFilters,
    suggestionTypeFilter,
    suggestions,
    suggestionsError,
    suggestionsPending,
    downstreamStatuses,
  } = useBusinessPlanning(
    () => sourceOrder.value,
    () => downstreamPage.value,
  )
  const router = useRouter()
  const auth = useAuthStore()
  const hasPermission = (permission: string) =>
    auth.principal?.permissionCodes?.includes(permission) === true
  const canReadDemands = computed(() => hasPermission(P.planningDemandsRead))
  const canReadMps = computed(() => hasPermission(P.planningMpsRead))
  const canReadMrp = computed(() => hasPermission(P.planningMrpRead))
  const canReadDeliveries = useCanReadMaterialDeliveries()
  const defaultTab = computed(() => {
    if (canReadDemands.value) return 'demands'
    if (canReadMps.value) return 'mps'
    if (canReadMrp.value) return hasPermission(P.planningSuggestionsManage) ? 'suggestions' : 'runs'
    return ''
  })
  const hasScope = computed(() => hasBusinessContext(runRequest))
  const demandsReady = computed(
    () => hasScope.value && canReadDemands.value && !demandsPending.value && !demandsError.value,
  )
  const mpsReady = computed(
    () => hasScope.value && canReadMps.value && !mpsBucketsPending.value && !mpsBucketsError.value,
  )
  const runsReady = computed(
    () => hasScope.value && canReadMrp.value && !mrpRunsPending.value && !mrpRunsError.value,
  )
  const suggestionsReady = computed(
    () => runsReady.value && !suggestionsPending.value && !suggestionsError.value,
  )
  const canReadWorkOrders = computed(() => hasPermission(P.mesWorkOrdersRead))
  const orderUrgencies = useOrderUrgencies(
    computed(() => demands.value.map((demand) => demand.sourceReference)),
  )
  const displayMode = shallowRef<UrgencyDisplayMode>(DEFAULT_URGENCY_DISPLAY_MODE)
  // 排序独立于显示模式：需求默认按统一紧急度排序。
  const orderedDemands = computed(() =>
    orderRowsByUrgency(
      demands.value,
      (demand) => demand.sourceReference,
      orderUrgencies.byReference.value,
    ),
  )
  function refreshUrgency() {
    void orderUrgencies.refresh()
    refreshPlanning()
  }

  const {
    demandKeyword,
    demandTypeFilter,
    demandTypeFilterOptions,
    filteredDemands,
    demandFilterActive,
    demandTypeResetNotice,
  } = usePlanningDemandFilters(demands, orderedDemands, sourceOrder)

  // 主数据：SKU / 工厂 / 计量单位（Select 显名称、绑定编码，码→名解析复用）。
  const canReadResources = () =>
    auth.principal?.permissionCodes?.includes(P.masterDataResourcesRead) === true
  const { skus } = useBusinessSkus(
    () => auth.principal?.permissionCodes?.includes(P.masterDataProductsRead) === true,
  )
  const baseUomBySku = toBaseUomBySku(skus)
  const { resources: sites } = useBusinessMasterDataResources('site', { enabled: canReadResources })
  const { resources: units } = useBusinessMasterDataResources('unit-of-measure', {
    enabled: canReadResources,
  })
  // MPS 的评审人 / 发布人记的是登录账号 ID，按员工名录换成姓名；名录里没有（如系统管理员）就显示「—」。
  const { resolveUser } = useMasterDataDisplayNames({ users: true, enabled: canReadResources })
  function actorName(userId: string) {
    return resolveUser(userId) ?? '—'
  }

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

  // 页内这条只留给「读数据取不到」这一类区域状态。
  // 曾踩坑（MAN-700 / #1289）：写操作的 error ref 也被并进来，于是 RunMrp 一个 500 就把
  // `Internal Server Error` 常驻在页面上，且弹框不会关——操作结果必须走 toast，见
  // `frontend/DESIGN/patterns/feedback-and-notifications.md`。
  const errorMessage = computed(
    () =>
      [demandsError, mpsBucketsError, mrpRunsError, suggestionsError]
        .map((ref) => inlineErrorMessage(ref.value))
        .find(Boolean) ?? '',
  )
  // 时段视图只关心三份读数据的加载/失败态（不掺入各写操作的错误）。
  const planningDataPending = computed(
    () => demandsPending.value || mpsBucketsPending.value || suggestionsPending.value,
  )
  const planningDataError = computed(
    () =>
      [demandsError, mpsBucketsError, suggestionsError]
        .map((ref) => inlineErrorMessage(ref.value))
        .find(Boolean) ?? '',
  )

  const demandOpen = shallowRef(false)
  const activeTab = shallowRef('demands')
  const focusedSuggestionId = shallowRef('')
  const demandCancelTarget = shallowRef<BusinessConsoleDemandSourceItem | null>(null)
  const mpsOpen = shallowRef(false)
  const mpsEditingId = shallowRef<string | null>(null)
  const mrpOpen = shallowRef(false)
  const acceptingSuggestionId = shallowRef<string | null>(null)
  const cancelTarget = shallowRef<BusinessConsolePlanningSuggestionItem | null>(null)

  // 拒绝建议：弹框要求填写原因（必填、≤128 字），确认后走网关两跳。
  const rejectTarget = shallowRef<BusinessConsolePlanningSuggestionItem | null>(null)
  const rejectReason = shallowRef('')
  const canSubmitReject = computed(() => {
    const reason = rejectReason.value.trim()
    return reason.length > 0 && reason.length <= SUGGESTION_REJECT_REASON_MAX_LENGTH
  })
  function openRejectDialog(row: BusinessConsolePlanningSuggestionItem) {
    rejectTarget.value = row
    rejectReason.value = ''
  }
  async function submitRejectSuggestion() {
    const target = rejectTarget.value
    if (!target?.suggestionId || !canSubmitReject.value) return
    try {
      await rejectSuggestion({ suggestionId: target.suggestionId, reason: rejectReason.value })
      notifySuccess('计划建议已拒绝。')
      rejectTarget.value = null
    } catch (error) {
      // 诚实失败：透传服务端 message（composable 已把软失败转成异常）。
      notifyOperationFailure('拒绝计划建议失败', error, '计划建议拒绝失败，请稍后重试。')
    }
  }

  const demandTypeOptions = [
    { label: '预测', value: 'forecast' },
    { label: '安全库存', value: 'safety-stock' },
  ]
  const suggestionStatusOptions = [
    { label: '全部状态', value: 'all' },
    { label: '待评审', value: 'open' },
    { label: '已接受', value: 'accepted' },
    { label: '已拒绝', value: 'rejected' },
    { label: '已被替代', value: 'superseded' },
  ]

  // 建议分型（呼应两组：生产→MES / 采购→ERP）。
  const productionSuggestions = computed(() =>
    scopedSuggestions.value.filter((i) => i.suggestionType === 'planned-work-order'),
  )
  const purchaseSuggestions = computed(() =>
    scopedSuggestions.value.filter((i) => i.suggestionType === 'planned-purchase'),
  )
  const openSuggestionCount = computed(
    () => scopedSuggestions.value.filter((i) => isOpen(i.status)).length,
  )
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
  // 需求池保留窗外行，但当前展示的已完成 MRP 没有处理这些行。
  function isOutsideCompletedHorizon(demand: BusinessConsoleDemandSourceItem): boolean {
    const horizonEnd = timePhasedRun.value?.horizonEnd
    return !!horizonEnd && !!demand.dueDate && demand.dueDate > horizonEnd
  }
  const demandSkuCodes = computed(
    () =>
      new Set(
        demands.value
          .filter((demand) => !isOutsideCompletedHorizon(demand))
          .map((demand) => demand.skuCode)
          .filter((code): code is string => !!code),
      ),
  )
  // 覆盖口径集中在 planningAggregation.countsTowardCoverage：供给型 + 状态仍算数（拒绝的不算）
  // + 锁定 timePhasedRun 那一次运行。KPI、需求池「覆盖」列共用建议判据，
  // 不会出现「刚拒绝的建议还占着覆盖」或「KPI 跨运行、图只算一次」这种自相矛盾。
  const coveredSkuCodes = computed(() =>
    coveredDemandSkuCodes(suggestions.value, coverageRunId.value, demandSkuCodes.value),
  )
  /**
   * 需求覆盖率＝已生成建议的物料 ÷ 需求物料（去重），目标 100%。
   * 需求池为空时分母为 0，覆盖率无从谈起——返回 null 走无样本态，
   * 不能把"没有需求"画成 100% 满绿（那等于宣称计划已全覆盖）。
   */
  const demandCoverageRate = computed(() => {
    const total = demandSkuCodes.value.size
    if (total === 0) return null
    return Math.round((coveredSkuCodes.value.size / total) * 100)
  })
  // 已发布 + 未发布 = 全部主计划行，是真正的构成关系。
  const mpsSegments = computed<NvMetricSegment[]>(() => [
    { key: 'released', label: '已发布', value: releasedMpsCount.value, tone: 'success' },
    {
      key: 'draft',
      label: '待评审发布',
      value: mpsBuckets.value.length - releasedMpsCount.value,
      tone: 'warning',
    },
  ])

  // 运行列表由服务端按创建时间降序返回，不能用计划范围判断新旧。
  const runsChronological = computed(() => [...mrpRuns.value].reverse())
  const latestRun = computed(() => mrpRuns.value[0] ?? null)
  const latestCompletedRun = computed(
    () => mrpRuns.value.find((run) => run.status?.toLowerCase() === 'completed') ?? null,
  )
  const staleDemandChangeCount = computed(() => latestCompletedRun.value?.demandChangeCount ?? 0)
  const {
    suggestionRunChoice,
    scopedSuggestions,
    deniedSource,
    returnLabel,
    sourceUnresolved,
    focusSuggestion,
    navigateToSuggestion,
    focusPegging,
  } = usePlanningSuggestionLocation(
    {
      mrpRuns,
      suggestions,
      suggestionFilters,
      suggestionTypeFilter,
      mrpRunsError,
      suggestionsError,
      demandsError,
      runRequest,
      runSelection,
    },
    {
      route,
      router,
      activeTab,
      focusedSuggestionId,
      defaultTab,
      sourceOrder,
      canReadMrp,
      canReadMps,
      canReadDemands,
      canReadDeliveries,
      latestCompletedRun,
    },
  )

  async function confirmCancelDemand() {
    const target = demandCancelTarget.value
    if (!target?.demandSourceId) return
    try {
      await cancelDemand(target.demandSourceId)
      notifySuccess('需求已作废，追溯记录仍保留。')
      demandCancelTarget.value = null
    } catch (error) {
      notifyOperationFailure('作废需求失败', error, '作废需求失败，请稍后重试。')
    }
  }
  function runChoiceLabel(run: BusinessConsoleMrpRunItem): string {
    return `第 ${mrpRuns.value.length - mrpRuns.value.indexOf(run)} 次 · ${runHorizonLabel(run)}`
  }
  function successorRunLabel(runId: string): string {
    const run = mrpRuns.value.find((item) => item.runId === runId)
    return run ? runChoiceLabel(run) : '后续 MRP 运行'
  }
  // 历次 MRP 产出的建议条数——真实时序，作为最近一次运行卡的迷你趋势。
  const mrpSuggestionSeries = computed(() =>
    runsChronological.value.map((run) => run.suggestionCount ?? 0),
  )
  const mrpRunLabels = computed(() =>
    runsChronological.value.map((run) => formatDate(run.horizonStart)),
  )
  const latestRunKpiValue = computed(() =>
    latestRun.value ? planningStatus(latestRun.value.status).label : '未运行',
  )
  // 卡片两条脚注分工：左＝这次运行覆盖的计划范围，右＝它读了哪些输入。
  // 没有运行过时只留左边一句「尚未运行 MRP」，右边留空——两边渲染同一句是重复噪音。
  const latestRunKpiHint = computed(() =>
    latestRun.value ? `输入 ${inputSourcesLabel(latestRun.value.inputSources)}` : '',
  )
  const selectedRun = computed(
    () => mrpRuns.value.find((r) => r.runId === runSelection.runId) ?? null,
  )
  // 时段视图与覆盖统计只看已完成运行：选中已完成批次时使用它，否则使用最近完成批次。
  // 后端 RunMrp 不关闭历史 Open 建议，跨运行求和会重复计数，必须锁单次运行。
  const timePhasedRun = computed(() =>
    selectedRun.value?.status?.toLowerCase() === 'completed'
      ? selectedRun.value
      : latestCompletedRun.value,
  )
  // 覆盖统计锁定的运行 = 时段视图那一次；两图与 KPI 共用，口径不再分叉。
  const coverageRunId = computed(() => timePhasedRun.value?.runId ?? '')

  // 需求覆盖状态：本次运行里出现未被拒绝的供给建议 → 已生成建议；否则未覆盖。
  function demandCoverage(skuCode?: string | null): { label: string; tone: StatusTone } {
    if (skuCode && coveredSkuCodes.value.has(skuCode))
      return { label: '已生成建议', tone: 'success' }
    return { label: '未覆盖', tone: 'neutral' }
  }
  function demandCoverageForRow(row: BusinessConsoleDemandSourceItem): {
    label: string
    tone: StatusTone
  } {
    if (isOutsideCompletedHorizon(row)) return { label: '窗外', tone: 'warning' }
    return demandCoverage(row.skuCode)
  }

  // 计划建议分型筛选后的可见行。
  const visibleSuggestions = computed(() => {
    const t = suggestionTypeFilter.type
    // 'all' 哨兵 = 不过滤（reka 的 SelectItem 不允许空串 value，故用 'all' 代替原空串）。
    const rows =
      !t || t === 'all'
        ? scopedSuggestions.value
        : scopedSuggestions.value.filter((s) => s.suggestionType === t)
    // 表格默认分页；将定位行放到首屏，不改变其它建议的相对次序。
    const focused = rows.find((row) => row.suggestionId === focusedSuggestionId.value)
    return focused ? [focused, ...rows.filter((row) => row !== focused)] : rows
  })
  const suggestionPage = shallowRef(1)
  const suggestionPageSize = shallowRef(10)
  watch([visibleSuggestions, suggestionPageSize], () => {
    suggestionPage.value = 1
  })
  const suggestionPageRows = computed(() => {
    const start = (suggestionPage.value - 1) * suggestionPageSize.value
    return visibleSuggestions.value.slice(start, start + suggestionPageSize.value)
  })
  watchEffect(() => {
    downstreamPage.value =
      activeTab.value === 'suggestions' && !sourceUnresolved.value ? suggestionPageRows.value : []
  })
  const suggestionTypeFilterOptions = [
    { label: '全部类型', value: 'all' },
    { label: '生产建议 (→MES)', value: 'planned-work-order' },
    { label: '采购建议 (→ERP)', value: 'planned-purchase' },
    { label: '提前调整', value: 'reschedule-in' },
    { label: '延期调整', value: 'reschedule-out' },
    { label: '释放日已过', value: 'release-date-past' },
    { label: '负可用', value: 'negative-availability' },
    { label: '超期在途', value: 'overdue-receipt' },
    { label: '取消计划收货', value: 'cancel' },
    { label: '销售超出主计划', value: 'mps-sales-excess' },
  ]

  const demandColumns: NvDataTableColumn<BusinessConsoleDemandSourceItem>[] = [
    { key: 'sourceReference', header: '来源', width: 'w-48', cellClass: 'font-medium' },
    { key: 'demandType', header: '类型', width: 'w-24' },
    { key: 'sourceStatus', header: '状态', width: 'w-24' },
    { key: 'skuCode', header: 'SKU', width: 'w-44' },
    { key: 'siteCode', header: '工厂', width: 'w-36' },
    { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
    { key: 'dueDate', header: '需求日', width: 'w-32' },
    { key: 'urgency', header: '紧迫度', width: 'w-28' },
    { key: 'coverage', header: '覆盖', width: 'w-28' },
    { key: 'actions', header: '', align: 'end', width: 'w-20' },
  ]
  const mpsColumns: NvDataTableColumn<BusinessConsoleMpsBucketItem>[] = [
    { key: 'bucketDate', header: '计划周期', cellClass: 'font-medium', width: 'w-32' },
    { key: 'skuCode', header: 'SKU', width: 'w-64' },
    { key: 'siteCode', header: '工厂', width: 'w-40' },
    { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
    { key: 'status', header: '状态', width: 'w-28' },
    { key: 'reviewRelease', header: '评审/发布', width: 'w-40' },
    { key: 'actions', header: '', align: 'end', width: 'w-44' },
  ]
  const runColumns: NvDataTableColumn<BusinessConsoleMrpRunItem>[] = [
    // runId 是 GUID，不显裸 GUID；以「计划范围」(horizon) 作人读锚点，追溯按钮内部用 runId。
    { key: 'horizon', header: '计划范围', width: 'w-52', cellClass: 'font-medium' },
    { key: 'status', header: '状态', width: 'w-24' },
    { key: 'inputSources', header: '输入来源', width: 'w-52' },
    { key: 'inputCoverage', header: '输入覆盖', width: 'w-40' },
    { key: 'demandCount', header: '覆盖需求', align: 'end', width: 'w-24' },
    { key: 'inputDegradationSources', header: '输入状态', width: 'w-36' },
    { key: 'suggestionCount', header: '建议', align: 'end', width: 'w-20' },
    { key: 'suggestionsPerDemand', header: '每条需求的建议数', align: 'end', width: 'w-48' },
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
    { key: 'suggestion', header: '建议', width: 'w-24' },
  ]
  const suggestionColumns: NvDataTableColumn<BusinessConsolePlanningSuggestionItem>[] = [
    // suggestionId 是 GUID 且无人读号；不显裸 GUID，行由「类型 + SKU + 数量 + 原因」自识别。
    { key: 'suggestionType', header: '类型', width: 'w-28', cellClass: 'font-medium' },
    { key: 'skuCode', header: 'SKU', width: 'w-32' },
    { key: 'quantity', header: '数量', align: 'end', width: 'w-28' },
    { key: 'requiredDate', header: '需求日', width: 'w-28' },
    { key: 'reasonCode', header: '来源与原因', width: 'w-44' },
    { key: 'downstream', header: '承接单据', width: 'w-40' },
    { key: 'status', header: '状态', width: 'w-24' },
    {
      key: 'actions',
      header: '操作',
      align: 'end',
      width: 'w-44',
      headerClass: 'sticky right-0 z-20 border-l bg-background',
      cellClass: 'sticky right-0 z-10 border-l bg-card',
    },
  ]

  // 五个写操作此前都没有 try/catch：一次 500 就是「未处理拒绝 + 弹框永远关不掉」（MAN-700 / #1289）。
  // 现在一律 toast 分层透传，失败时弹框保持打开可改可重试，成功才关。
  async function submitDemand() {
    try {
      await createOrUpdateDemand()
      demandOpen.value = false
    } catch (error) {
      notifyOperationFailure('保存需求失败', error, '保存需求失败，请稍后重试。')
    }
  }
  async function submitMpsBucket() {
    try {
      if (mpsEditingId.value) await updateMpsBucket(mpsEditingId.value)
      else await createMpsBucket()
      mpsOpen.value = false
      mpsEditingId.value = null
    } catch (error) {
      notifyOperationFailure('保存主计划行失败', error, '保存主计划行失败，请稍后重试。')
    }
  }
  function editMpsBucket(row: BusinessConsoleMpsBucketItem) {
    if (!row.mpsId) return
    mpsForm.skuCode = row.skuCode ?? ''
    mpsForm.uomCode = row.uomCode ?? ''
    mpsForm.siteCode = row.siteCode ?? ''
    mpsForm.bucketDate = row.bucketDate ?? ''
    mpsForm.quantity = row.quantity ?? 0
    mpsEditingId.value = row.mpsId
    mpsOpen.value = true
  }
  function newMpsBucket() {
    mpsEditingId.value = null
    mpsForm.skuCode = ''
    mpsForm.uomCode = ''
    mpsForm.siteCode = ''
    mpsForm.bucketDate = new Date().toISOString().slice(0, 10)
    mpsForm.quantity = 0
  }
  const { submitMrpRun, mrpRunInProgress } = usePlanningMrpRunDialog(
    { runMrp, activeMrpRun },
    mrpOpen,
  )

  async function reviewMps(row: BusinessConsoleMpsBucketItem) {
    if (!row.mpsId) return
    try {
      await reviewMpsBucket(row.mpsId)
      notifySuccess('主计划行已完成评审。')
    } catch (error) {
      notifyOperationFailure('评审主计划行失败', error, '评审主计划行失败，请稍后重试。')
    }
  }
  async function releaseMps(row: BusinessConsoleMpsBucketItem) {
    if (!row.mpsId) return
    try {
      await releaseMpsBucket(row.mpsId)
      notifySuccess('主计划行已发布，可作为 MRP 输入。')
    } catch (error) {
      notifyOperationFailure('发布主计划行失败', error, '发布主计划行失败，请稍后重试。')
    }
  }
  async function acceptPlanningSuggestion(row: BusinessConsolePlanningSuggestionItem) {
    if (!row.suggestionId || !row.suggestionType) return
    if (!isAcceptableSuggestion(row.suggestionType)) return
    if (acceptingSuggestionId.value) return
    if (row.suggestionType === 'cancel' && cancelTarget.value?.suggestionId !== row.suggestionId) {
      cancelTarget.value = row
      return
    }
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
      cancelTarget.value = null
    } catch (error) {
      notifyOperationFailure(
        '接受计划建议失败',
        error,
        '计划建议接受失败，请检查生产版本、供应商、库存或权限状态。',
      )
    } finally {
      acceptingSuggestionId.value = null
    }
  }
  // —— 计划建议行的「对该单排产」（MAN-694 / #1262）——
  // 排程的最小单位是 MES 工单。生产建议只有**被接受、承接成 MES 工单之后**才有可排的单，
  // 承接单据 id 就是工单 id（同一个值也被 downstreamRoute 拿去下钻 /mes/work-orders/{id}）。
  const canScheduleSingleOrder = useCanScheduleSingleOrder()
  const scheduleTarget = shallowRef<BusinessConsolePlanningSuggestionItem | null>(null)

  function suggestionWorkOrderId(row: BusinessConsolePlanningSuggestionItem) {
    // 码值口径两侧不一：后端种子写 `business-mes`，这里原来严格比 `BusinessMes`，
    // 于是演示数据上「对该单排产」一个按钮都不渲染（第五轮走查实测）。归一化后再比。
    return isMesWorkOrderDownstream(row.downstreamService, row.downstreamDocumentType)
      ? (row.downstreamDocumentId ?? '')
      : ''
  }
  function suggestionScheduleHint(row: BusinessConsolePlanningSuggestionItem) {
    // 排产的最小单位是工单：没承接成工单就没有可排的单，说清楚而不是给个点不动的按钮。
    return row.suggestionType === 'planned-work-order' ? '未承接工单，暂不能排产' : ''
  }
  function downstreamRoute(
    service: string | null | undefined,
    type: string | null | undefined,
    documentId: string,
  ) {
    // 同一个口径问题：严格比字面量时，种子的 `business-mes` 会被兜底路由到 /erp 关键词搜索，
    // 看着有链接、点进去却不是工单详情——比按钮不渲染更难发现。
    if (isMesWorkOrderDownstream(service, type)) {
      return { path: `/mes/work-orders/${encodeURIComponent(documentId)}` }
    }

    return {
      path: '/erp',
      query: { keyword: documentId },
    }
  }
  function formatMpsQuantity(value?: number | null, uom?: string | null) {
    const name = units.value.find((unit) => unit.code === uom)?.displayName?.trim() || '—'
    return `${value ?? 0} ${name}`
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

  return {
    acceptPlanningSuggestion,
    acceptSuggestionPending,
    acceptingSuggestionId,
    activeMrpRun,
    activeTab,
    actorName,
    canReadDeliveries,
    canReadDemands,
    canReadMps,
    canReadMrp,
    canReadWorkOrders,
    canScheduleSingleOrder,
    canSubmitDemand,
    canSubmitMps,
    canSubmitReject,
    cancelDemandPending,
    cancelTarget,
    confirmCancelDemand,
    coverageRunId,
    coveredSkuCodes,
    createDemandPending,
    createMpsBucketPending,
    defaultTab,
    demandCancelTarget,
    demandColumns,
    demandCoverageForRow,
    demandCoverageRate,
    demandFilterActive,
    demandForm,
    demandKeyword,
    demandOpen,
    demandSkuCodes,
    demandTypeFilter,
    demandTypeFilterOptions,
    demandTypeOptions,
    demandTypeResetNotice,
    demands,
    demandsError,
    demandsPending,
    demandsReady,
    deniedSource,
    displayMode,
    downstreamRoute,
    downstreamStatuses,
    editMpsBucket,
    errorMessage,
    filteredDemands,
    focusPegging,
    focusSuggestion,
    focusedSuggestionId,
    formatMpsQuantity,
    hasPermission,
    hasScope,
    latestCompletedRun,
    latestRun,
    latestRunKpiHint,
    latestRunKpiValue,
    mpsBuckets,
    mpsBucketsError,
    mpsBucketsPending,
    mpsColumns,
    mpsEditingId,
    mpsForm,
    baseUomBySku,
    mpsOpen,
    mpsReady,
    mpsSegments,
    mrpOpen,
    mrpRunInProgress,
    mrpRunLabels,
    mrpRuns,
    mrpRunsError,
    mrpRunsPending,
    mrpSuggestionSeries,
    navigateToSuggestion,
    newMpsBucket,
    openBomContext,
    openPeggingSource,
    openRejectDialog,
    openSalesOrderDemand,
    openSuggestionCount,
    orderUrgencies,
    pegging,
    peggingColumns,
    peggingPending,
    planningDataError,
    planningDataPending,
    refreshPlanning,
    refreshUrgency,
    rejectReason,
    rejectSuggestionPending,
    rejectTarget,
    releaseMps,
    releaseMpsBucketPending,
    returnLabel,
    reviewKpiHint,
    reviewMps,
    reviewMpsBucketPending,
    router,
    runChoiceLabel,
    runColumns,
    runMrpPending,
    runRequest,
    runSelection,
    runsReady,
    scheduleTarget,
    scopedSuggestions,
    selectedRun,
    siteLabel,
    siteOptions,
    skuLabel,
    sourceOrder,
    sourceUnresolved,
    staleDemandChangeCount,
    submitDemand,
    submitMpsBucket,
    submitMrpRun,
    submitRejectSuggestion,
    successorRunLabel,
    suggestionColumns,
    suggestionFilters,
    suggestionPage,
    suggestionPageRows,
    suggestionPageSize,
    suggestionRunChoice,
    suggestionScheduleHint,
    suggestionStatusOptions,
    suggestionTypeFilter,
    suggestionTypeFilterOptions,
    suggestionWorkOrderId,
    suggestions,
    suggestionsError,
    suggestionsPending,
    suggestionsReady,
    timePhasedRun,
    uomOptions,
    updateMpsBucketPending,
    visibleSuggestions,
  }
}
