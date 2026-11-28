import { maintenanceWorkOrderNoLabel } from '@/composables/equipment/useEquipmentPickerCatalog'
import type {
  NvDataTableColumn,
  NvMetricFacet,
  NvMetricSegment,
  NvMetricStripCell,
} from '@nerv-iip/ui'
import {
  describeAvailabilityWindowReference,
  describeEquipmentReason,
  equipmentSourceStatus,
  useBusinessEquipmentDevice,
  type EquipmentTone,
} from '@/composables/equipment/useBusinessEquipment'
import {
  describeTelemetryOeeDegradation,
  formatOeeRate,
  useBusinessEquipmentHealth,
  useBusinessTelemetryHistory,
  useBusinessTelemetryOee,
  useBusinessTelemetryRuntimeHours,
  useMaintenancePlanRuntimeRemaining,
} from '@/composables/equipment/useBusinessTelemetry'
import { useBusinessDeviceControlCommands } from '@/composables/equipment/useBusinessDeviceControl'
import { usePagedList } from '@/composables/shared/usePagedList'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import {
  useMaintenanceAvailabilityWindows,
  useMaintenanceInspections,
  useMaintenancePlans,
  useMaintenanceReliability,
  useMaintenanceSpareParts,
  useMaintenanceWorkOrders,
} from '@/composables/maintenance/useBusinessMaintenance'
import { useMasterDataDisplayNames } from '@/composables/master-data/useMasterDataDisplayNames'
import {
  formatIsoInterval,
  labelFor,
  MAINTENANCE_INSPECTION_RESULT_LABELS,
} from '@/data/businessLabels'
import { computed, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { inlineErrorMessage } from '@/utils/notify'
import { readFaceText } from '@/utils/readFace'
import { formatDateTime } from '@/utils/format'

export function useDeviceDetailPage() {
  // 网关维护列表 facade 校验 Take 上限为 200；曾用 250 会被拒（400），使详情页维护上下文
  // （工单/计划/备件/点检）与运行小时保养卡全部拿不到数据。收敛到上限内。
  const MAINTENANCE_DETAIL_TAKE = 200
  const RUNTIME_HOURS_DEFAULT_WINDOW_DAYS = 90

  const route = useRoute()
  const router = useRouter()
  const routeDeviceAssetId = computed(() => {
    const params = route.params as Record<string, string | string[] | undefined>
    const value = params.deviceAssetId
    return Array.isArray(value) ? value[0] : value
  })

  const {
    activeAlarms,
    availabilityWindows,
    device,
    deviceError,
    devicePending,
    filters,
    refreshDevice,
  } = useBusinessEquipmentDevice()
  const {
    filters: historyFilters,
    historyError,
    historyPending,
    visibleHistoryItems,
  } = useBusinessTelemetryHistory()
  const {
    filters: oeeFilters,
    oee,
    oeeError,
    oeePending,
    runtimeAvailabilityError,
  } = useBusinessTelemetryOee()
  const {
    availabilityError: maintenanceAvailabilityError,
    availabilityPending: maintenanceAvailabilityPending,
    availabilityWindows: maintenanceAvailabilityWindows,
    filters: maintenanceAvailabilityFilters,
  } = useMaintenanceAvailabilityWindows()
  const {
    filters: reliabilityFilters,
    reliability,
    reliabilityError,
    reliabilityPending,
  } = useMaintenanceReliability()
  const { workOrders, workOrdersError, workOrdersPending } = useMaintenanceWorkOrders({
    take: MAINTENANCE_DETAIL_TAKE,
  })
  const {
    plans,
    plansError,
    plansPending,
    refreshPlans,
    filters: plansFilters,
  } = useMaintenancePlans({ take: MAINTENANCE_DETAIL_TAKE })
  const { inspections, inspectionsError, inspectionsPending } = useMaintenanceInspections({
    take: MAINTENANCE_DETAIL_TAKE,
  })
  const { spareParts, sparePartsError, sparePartsPending } = useMaintenanceSpareParts({
    take: MAINTENANCE_DETAIL_TAKE,
  })

  const auth = useAuthStore()
  const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
  const canControlDevice = computed(() => permissionCodes.value.includes(P.iiotDeviceControlWrite))
  const canCreateMaintenanceWorkOrder = computed(() =>
    permissionCodes.value.includes(P.maintenanceWorkOrdersManage),
  )
  const controlSheetOpen = ref(false)
  // 采集点位（#3870）：就地维护这台设备的采集点位，读者能看，有点位管理权限才能改。
  const canManageTelemetryPoints = computed(() => permissionCodes.value.includes(P.iiotTagsManage))
  const pointsSheetOpen = ref(false)
  const deviceAssetIdRef = computed(() => filters.deviceAssetId)
  const { health, healthError, healthPending, refreshHealth } =
    useBusinessEquipmentHealth(deviceAssetIdRef)
  const healthErrorMessage = computed(() => inlineErrorMessage(healthError.value))
  const {
    commands: controlCommands,
    commandsError: controlCommandsError,
    commandsPending: controlCommandsPending,
    commandsTotal: controlCommandsTotal,
    historyFilters: controlHistoryFilters,
    refreshCommands: refreshControlCommands,
  } = useBusinessDeviceControlCommands(deviceAssetIdRef)
  const { page: controlPage, pageSize: controlPageSize } = usePagedList(controlHistoryFilters)
  const controlCommandsErrorMessage = computed(() => inlineErrorMessage(controlCommandsError.value))

  type ControlCommandRow = (typeof controlCommands)['value'][number]
  const controlColumns: NvDataTableColumn<ControlCommandRow>[] = [
    {
      key: 'requestedAtUtc',
      header: '时间',
      width: 'w-44',
      accessor: (r) => formatDateTime(r.requestedAtUtc),
    },
    // 操作人是登录账号（系统标识），按员工名录显示姓名；查不到姓名显示「—」，不回吐账号。
    { key: 'requestedBy', header: '操作人', accessor: (r) => resolveUser(r.requestedBy) ?? '—' },
    { key: 'commandType', header: '命令' },
    { key: 'value', header: '值', accessor: (r) => r.value ?? (r.tagKey ? '—' : '参数集') },
    { key: 'approvalStatus', header: '审批状态', width: 'w-28' },
    { key: 'status', header: '回执', width: 'w-24' },
  ]
  function controlCommandRowKey(row: ControlCommandRow) {
    return row.commandId ?? row.operationTaskId ?? row.correlationId ?? ''
  }

  const currentState = computed(() => device.value?.currentState)
  const errorMessage = computed(() => inlineErrorMessage(deviceError.value))
  const telemetryErrorMessage = computed(() =>
    inlineErrorMessage(
      historyError.value ||
        oeeError.value ||
        runtimeAvailabilityError.value ||
        runtimeHoursError.value,
    ),
  )
  const oeeDegradedReasons = computed(() =>
    (oee.value?.degradedReasons ?? []).map(describeTelemetryOeeDegradation),
  )
  const maintenanceErrorMessage = computed(() =>
    inlineErrorMessage(
      maintenanceAvailabilityError.value ||
        reliabilityError.value ||
        workOrdersError.value ||
        plansError.value ||
        inspectionsError.value ||
        sparePartsError.value,
    ),
  )
  const blockCount = computed(
    () => availabilityWindows.value.filter((w) => w.availabilityStatus !== 'available').length,
  )
  const historyCount = computed(() => visibleHistoryItems.value.length)
  const currentDeviceId = computed(() => filters.deviceAssetId.trim())
  const currentDeviceWorkOrderMatches = computed(() =>
    workOrders.value.filter((row) => row.deviceAssetId === currentDeviceId.value),
  )
  const currentDeviceWorkOrders = computed(() => currentDeviceWorkOrderMatches.value.slice(0, 5))
  const currentDevicePlanMatches = computed(() =>
    plans.value.filter((row) => row.deviceAssetId === currentDeviceId.value),
  )
  const currentDevicePlans = computed(() => currentDevicePlanMatches.value.slice(0, 5))
  // 运行小时型保养计划（有 runtimeHourInterval）。每条计划的剩余小时由 runtime-hours facade 按各计划
  // 自己的起算窗口算出（不共用一个窗口），距下次保养取剩余最小（最紧迫）的一条；剩余未知（无运行样本）
  // 的计划排在有剩余之后，避免遮住真正快到期的计划。
  const currentDeviceRuntimePlans = computed(() =>
    currentDevicePlanMatches.value.filter((row) => row.runtimeHourInterval != null),
  )
  const { remainingByPlanId, remainingPending, refreshRemaining } =
    useMaintenancePlanRuntimeRemaining(currentDeviceRuntimePlans)
  // 每个运行小时计划的剩余读取结果（含状态）。距下次保养以「已知(ok)计划的最小剩余」为准，但当有候选
  // 计划读取失败/暂无样本时，其真实剩余未知、可能比已知最小值更紧迫，故卡片明确标注「已知计划最小值」并
  // 提示结果不完整——不把已知最小值冒充成全体最紧迫值。
  const runtimeRemainingEntries = computed(() =>
    currentDeviceRuntimePlans.value.map((plan) => {
      const entry = remainingByPlanId.value[plan.planId ?? '']
      return { plan, status: entry?.status, hours: entry?.status === 'ok' ? entry.hours : null }
    }),
  )
  // 任一候选在读取中（或首轮未算出）时不显示任何具体值。
  const anyRuntimeRemainingLoading = computed(
    () =>
      remainingPending.value ||
      runtimeRemainingEntries.value.some((e) => e.status === 'loading' || e.status === undefined),
  )
  // 已知(ok)候选按剩余升序，第一个即已知计划最小剩余（最紧迫的已知者）。
  const mostUrgentOkRuntimeCandidate = computed(
    () =>
      runtimeRemainingEntries.value
        .filter((e) => e.status === 'ok')
        .slice()
        .sort((a, b) => (a.hours ?? 0) - (b.hours ?? 0))[0],
  )
  // 存在读取失败/暂无样本/阈值缺失的候选：真实剩余未知，可能更紧迫（用于「结果不完整」提示）。
  const runtimeRemainingUnknownCount = computed(
    () =>
      runtimeRemainingEntries.value.filter(
        (e) => e.status === 'error' || e.status === 'no-samples' || e.status === 'invalid',
      ).length,
  )
  const runtimeRemainingHasErrorCandidate = computed(() =>
    runtimeRemainingEntries.value.some((e) => e.status === 'error'),
  )
  const runtimeRemainingHasInvalidCandidate = computed(() =>
    runtimeRemainingEntries.value.some((e) => e.status === 'invalid'),
  )
  // 未知候选的**实际**成因短语：只列出当前确实存在的 status（error→读取失败 / no-samples→暂无样本 /
  // invalid→阈值缺失），不罗列不存在的成因——否则「另 N 个计划读取失败/暂无样本/阈值缺失」会让操作员
  // 误以为可能是遥测失败或无样本，而事实可能只有阈值缺失。
  const runtimeRemainingUnknownReason = computed(() => {
    const present = new Set(runtimeRemainingEntries.value.map((e) => e.status))
    return (
      [
        ['error', '读取失败'],
        ['no-samples', '暂无运行记录'],
        ['invalid', '阈值缺失'],
      ] as const
    )
      .filter(([status]) => present.has(status))
      .map(([, label]) => label)
      .join('/')
  })
  // 代表计划（累计小时窗口锚点、计划编号展示）：优先已知最紧迫者，否则取第一个候选。
  const currentDeviceRuntimePlan = computed(
    () => mostUrgentOkRuntimeCandidate.value?.plan ?? currentDeviceRuntimePlans.value[0],
  )
  const runtimeUntilNextCardValue = computed(() => {
    if (anyRuntimeRemainingLoading.value) return '读取中…'
    const mostUrgent = mostUrgentOkRuntimeCandidate.value
    if (mostUrgent) return formatHours(mostUrgent.hours)
    // 没有任何已知(ok)计划：按具体未知成因区分,与计划列表口径一致——不把「阈值缺失」误报成「无样本」。
    if (runtimeRemainingHasErrorCandidate.value) return '读取失败'
    if (runtimeRemainingHasInvalidCandidate.value) return '阈值缺失'
    return '暂无记录'
  })
  // 主卡描述随口径同步：有已知值但也有未知候选时，主 label 本身就说明是「已知计划最少还需」，
  // 不把已知最小值当成全体最紧迫的确定断言。
  const runtimeUntilNextCardDescription = computed(() =>
    mostUrgentOkRuntimeCandidate.value && runtimeRemainingUnknownCount.value > 0
      ? '已知计划最少还需'
      : '距下次保养还需',
  )
  // 卡片提示：只有在展示某个已知(ok)计划的值时才引用具体计划编号+阈值；无 ok 的失败/无样本/阈值缺失
  // 态用不带计划编号的聚合提示，避免把成因错误归到首个候选计划。
  const runtimeUntilNextCardHint = computed(() => {
    if (currentDeviceRuntimePlans.value.length === 0) return ''
    if (anyRuntimeRemainingLoading.value) return '正在读取各运行小时计划剩余小时'
    const mostUrgent = mostUrgentOkRuntimeCandidate.value
    if (mostUrgent) {
      const code = mostUrgent.plan.planCode ?? '—'
      if (runtimeRemainingUnknownCount.value > 0) {
        return `已知计划中的最小值（另 ${runtimeRemainingUnknownCount.value} 个计划${runtimeRemainingUnknownReason.value}，可能更紧迫）· 计划 ${code}`
      }
      return `运行小时型计划 ${code} · 阈值 ${mostUrgent.plan.nextDueRuntimeHours ?? '—'} 小时`
    }
    if (runtimeRemainingHasErrorCandidate.value) return '运行小时读取失败，请稍后重试'
    if (runtimeRemainingHasInvalidCandidate.value) return '保养计划的运行小时阈值未维护完整'
    return '自保养计划起算日以来没有运行记录'
  })
  // 「累计运行小时」是信息卡：窗口锚定运行小时计划起算日（无则近 N 天），展示窗口内累计运行事实。
  const nowIso = ref(new Date().toISOString())
  const runtimeHoursWindowEnd = nowIso
  const runtimeHoursWindowStart = computed(() => {
    const startsOn = currentDeviceRuntimePlan.value?.startsOn
    if (startsOn) return `${startsOn}T00:00:00.000Z`
    const start = new Date(nowIso.value)
    start.setDate(start.getDate() - RUNTIME_HOURS_DEFAULT_WINDOW_DAYS)
    return start.toISOString()
  })
  const {
    totalRuntimeHours,
    hasRuntimeSamples: hasRuntimeHoursSamples,
    runtimeHoursError,
    runtimeHoursPending,
    refreshRuntimeHours,
  } = useBusinessTelemetryRuntimeHours(
    deviceAssetIdRef,
    runtimeHoursWindowStart,
    runtimeHoursWindowEnd,
  )
  // 统一刷新：先把窗口截止时间推进到当前，再刷新设备、计划、累计运行小时与逐计划剩余小时——否则长驻
  // 页面点「刷新」时运行小时/剩余两卡仍停留在进入页面时的时间窗与旧数据。
  function refreshAll() {
    nowIso.value = new Date().toISOString()
    void refreshDevice()
    void refreshPlans()
    void refreshRuntimeHours()
    void refreshRemaining()
    void refreshHealth()
  }
  // 累计运行小时卡值先判 pending/error，再判是否有真实样本——无样本诚实显「无样本」，不把「没有事实」
  // 表达成确定的 0.0 小时（facade 对无样本设备返回 totalRuntimeHours=0/hasRuntimeSamples=false）。
  const cumulativeRuntimeCardValue = computed(() => {
    if (runtimeHoursPending.value) return '读取中…'
    if (runtimeHoursError.value) return '读取失败'
    if (!hasRuntimeHoursSamples.value) return '暂无记录'
    return formatHours(totalRuntimeHours.value)
  })
  const runtimeHoursCardHint = computed(() => {
    if (runtimeHoursPending.value) return '正在读取运行小时'
    if (runtimeHoursError.value) return '运行小时读取失败，请稍后重试'
    if (!hasRuntimeHoursSamples.value)
      return currentDeviceRuntimePlan.value?.startsOn
        ? '自保养计划起算日以来没有设备运行记录'
        : `近 ${RUNTIME_HOURS_DEFAULT_WINDOW_DAYS} 天没有设备运行记录`
    return currentDeviceRuntimePlan.value
      ? '自运行小时型计划起算日累计'
      : `近 ${RUNTIME_HOURS_DEFAULT_WINDOW_DAYS} 天累计`
  })
  const currentDeviceSpareParts = computed(() =>
    spareParts.value.filter((row) => row.deviceAssetId === currentDeviceId.value).slice(0, 5),
  )
  const currentDeviceWorkOrderIds = computed(
    () =>
      new Set(currentDeviceWorkOrderMatches.value.map((row) => row.workOrderId).filter(Boolean)),
  )
  const currentDevicePlanIds = computed(
    () => new Set(currentDevicePlanMatches.value.map((row) => row.planId).filter(Boolean)),
  )
  const currentDeviceInspections = computed(() =>
    inspections.value
      .filter(
        (row) =>
          (row.workOrderId ? currentDeviceWorkOrderIds.value.has(row.workOrderId) : false) ||
          (row.planId ? currentDevicePlanIds.value.has(row.planId) : false),
      )
      .slice(0, 5),
  )
  // --- 指标卡投影 -------------------------------------------------------------
  // 无数据时不喊「无数据」大字：值位收敛成一个安静的破折号，缺口原因交给脚注。
  function rateValue(rate?: number | null) {
    return rate === null || rate === undefined ? '—' : formatOeeRate(rate)
  }
  function rateProgress(rate?: number | null) {
    return rate === null || rate === undefined ? 0 : Math.max(0, Math.min(100, rate * 100))
  }
  /** 查询范围的说法：「最近 8 小时」「近 30 天」，与页面实际取数范围一致。 */
  function recentRangeLabel(startUtc: string, endUtc: string) {
    const hours = Math.round((Date.parse(endUtc) - Date.parse(startUtc)) / 3_600_000)
    return hours < 48 ? `最近 ${hours} 小时` : `近 ${Math.round(hours / 24)} 天`
  }
  const oeeRangeLabel = computed(() =>
    recentRangeLabel(oeeFilters.windowStartUtc, oeeFilters.windowEndUtc),
  )
  const reliabilityRangeLabel = computed(() =>
    recentRangeLabel(reliabilityFilters.windowStartUtc, reliabilityFilters.windowEndUtc),
  )
  function rateFoot(rate?: number | null, explanation?: string) {
    return rate === null || rate === undefined
      ? `${oeeRangeLabel.value}暂无可计算数据`
      : explanation
  }
  const oeeFacets = computed<NvMetricFacet[]>(() => [
    { key: 'availability', label: '可用率', value: rateValue(oee.value?.availabilityRate) },
    { key: 'performance', label: '性能率', value: rateValue(oee.value?.performanceRate) },
    { key: 'quality', label: '质量率', value: rateValue(oee.value?.qualityRate) },
  ])
  // 距下次保养的达成度 = 已累计 ÷（已累计 + 还需）；任一侧未知就不画进度，只留读数。
  const runtimeUntilNextProgress = computed(() => {
    const remaining = mostUrgentOkRuntimeCandidate.value?.hours
    const done = totalRuntimeHours.value
    if (remaining === undefined || remaining === null) return undefined
    if (!hasRuntimeHoursSamples.value || done === undefined || done === null) return undefined
    const span = done + remaining
    return span > 0 ? Math.max(0, Math.min(100, (done / span) * 100)) : undefined
  })
  const reliabilityCells = computed<NvMetricStripCell[]>(() => [
    {
      key: 'mtbf',
      label: 'MTBF',
      // MTBF = 运行时长 ÷ 故障次数：没有运行记录算不出；有运行记录但零故障时如实说「无故障」。
      value: !reliability.value?.mtbfRuntimeHasSamples
        ? '暂无记录'
        : reliability.value.mtbfHours == null
          ? '无故障'
          : metricLabel(reliability.value.mtbfHours, ' 小时'),
      meta: !reliability.value?.mtbfRuntimeHasSamples
        ? `${reliabilityRangeLabel.value}没有运行记录`
        : reliability.value.mtbfHours == null
          ? `${reliabilityRangeLabel.value}没有故障记录`
          : '运行时长 ÷ 故障次数',
    },
    {
      key: 'mttr',
      label: 'MTTR',
      value: metricLabel(reliability.value?.mttrMinutes, ' 分钟'),
      meta: '已完成维修的平均耗时',
    },
  ])
  // 故障与完成维修是同一批维护事件的两个互斥去向，相加等于窗口内维护事件总数。
  const maintenanceEventSegments = computed<NvMetricSegment[]>(() => [
    {
      key: 'failure',
      label: '维护故障',
      value: reliability.value?.failureCount ?? 0,
      tone: 'danger',
    },
    {
      key: 'repair',
      label: '完成维修',
      value: reliability.value?.repairCount ?? 0,
      tone: 'success',
    },
  ])
  const maintenanceEventTotal = computed(
    () => (reliability.value?.failureCount ?? 0) + (reliability.value?.repairCount ?? 0),
  )

  const telemetryPending = computed(() => historyPending.value || oeePending.value)
  const maintenancePending = computed(
    () =>
      maintenanceAvailabilityPending.value ||
      reliabilityPending.value ||
      workOrdersPending.value ||
      plansPending.value ||
      inspectionsPending.value ||
      sparePartsPending.value,
  )

  watch(
    routeDeviceAssetId,
    (deviceAssetId) => {
      if (!deviceAssetId || deviceAssetId === filters.deviceAssetId) return
      filters.deviceAssetId = deviceAssetId
      historyFilters.deviceAssetId = deviceAssetId
      oeeFilters.deviceAssetId = deviceAssetId
      maintenanceAvailabilityFilters.deviceAssetIds = deviceAssetId
      reliabilityFilters.deviceAssetId = deviceAssetId
      // Scope the plans query to this device so the runtime cards are not starved by a global,
      // paginated plan list (a device's plan may otherwise fall outside the first page).
      plansFilters.deviceAssetId = deviceAssetId
      void refreshDevice()
    },
    { immediate: true },
  )

  // 设备 / 维保读面只回编号（DEV-CNC-01 / WC-…），名称在主数据里，按编号 join 出中文名。
  const { resolveDevice, resolveDeviceCode, resolveWorkCenter, resolveUom, resolveUser } =
    useMasterDataDisplayNames({
      devices: true,
      workCenters: true,
      uoms: true,
      users: true,
    })
  // 本页这台设备。路由里的设备引用可能是编码，也可能是设备公开 ID（从维保可用窗口进来时），
  // 名称和编码都从主数据按引用解析，不依赖设备有没有运行数据。
  const deviceName = computed(() => resolveDevice(filters.deviceAssetId))
  const deviceCode = computed(
    () => resolveDeviceCode(filters.deviceAssetId) ?? readFaceText(filters.deviceAssetId, ''),
  )
  const deviceTitle = computed(() => {
    if (deviceName.value && deviceCode.value && deviceName.value !== deviceCode.value) {
      return `${deviceName.value}（${deviceCode.value}）`
    }
    return deviceName.value || deviceCode.value
  })
  // 采集接入状态与可用性窗口同一套后端判据（没有状态快照也没有任何样本才算尚未接入）。
  const sourceStatus = computed(() =>
    equipmentSourceStatus(currentState.value?.isSourceFresh, availabilityWindows.value),
  )
  const notConnected = computed(
    () => Boolean(device.value) && sourceStatus.value === 'notConnected',
  )
  const stateText = computed(() =>
    notConnected.value ? '尚未接入采集' : statusLabel(currentState.value?.currentState),
  )
  const SOURCE_TEXT = { fresh: '采集正常', stale: '采集过期', notConnected: '尚未接入' } as const
  // 未接入是还没配置，不是异常：中性色；采集中断才用警示色。
  const SOURCE_TONE = { fresh: 'success', stale: 'warning', notConnected: 'neutral' } as const
  const sourceText = computed(() => SOURCE_TEXT[sourceStatus.value])
  const sourceTone = computed(() => SOURCE_TONE[sourceStatus.value])
  /** 设备展示串：名称优先；名录失败时只保留可读业务编码，不回吐技术标识。 */
  function deviceLabel(reference?: string | null) {
    if (!reference) return '—'
    return resolveDevice(reference) ?? readFaceText(reference)
  }
  function workCenterLabel(code?: string | null, fallback = '未绑定') {
    if (!code) return fallback
    return resolveWorkCenter(code) ?? readFaceText(code, fallback)
  }

  type Window = (typeof availabilityWindows)['value'][number]
  const columns: NvDataTableColumn<Window>[] = [
    { key: 'availabilityStatus', header: '状态', width: 'w-24' },
    {
      key: 'reason',
      header: '原因',
      accessor: (r) => describeEquipmentReason(r.reasonCode ?? '').label,
    },
    { key: 'workCenterId', header: '工作中心', accessor: (r) => workCenterLabel(r.workCenterId) },
    { key: 'startUtc', header: '开始', width: 'w-44' },
    { key: 'endUtc', header: '结束', width: 'w-44' },
    {
      key: 'sourceReferenceId',
      header: '关联业务',
      accessor: (r) => describeAvailabilityWindowReference(r, deviceCode.value),
    },
    {
      key: 'substituteDeviceAssetIds',
      header: '替代设备',
      accessor: (r) =>
        r.substituteDeviceAssetIds?.length
          ? r.substituteDeviceAssetIds.map((id) => deviceLabel(id)).join('、')
          : '无',
    },
  ]

  function badgeVariant(tone: EquipmentTone) {
    if (tone === 'success') return 'success'
    if (tone === 'danger') return 'danger'
    return 'neutral'
  }
  function statusLabel(status?: string | null) {
    const labels: Record<string, string> = {
      down: '停机',
      faulted: '故障',
      idle: '空闲',
      offline: '离线',
      ready: '就绪',
      running: '运行中',
      stopped: '停止',
      maintenance: '维护中',
      warmup: '预热中',
    }
    // 词表漏了就说「未知状态」，绝不把后端英文码回吐到界面上。
    return status ? (labels[status.toLowerCase()] ?? '未知状态') : '未知'
  }
  function severityLabel(value?: string | null) {
    const labels: Record<string, string> = {
      blocked: '阻塞',
      critical: '严重',
      info: '信息',
      warning: '预警',
      major: '重要',
      minor: '次要',
    }
    // 词表漏了就说「未知级别」，绝不把后端英文码回吐到界面上。
    return value ? (labels[value.toLowerCase()] ?? '未知级别') : '未知'
  }
  function severityVariant(value?: string | null) {
    const severity = value?.toLowerCase()
    if (severity === 'critical' || severity === 'blocked') return 'danger'
    if (severity === 'warning') return 'warning'
    return 'neutral'
  }
  function metricLabel(value?: number | null, suffix = '') {
    if (value === null || value === undefined) return '暂无记录'
    return `${Number(value).toFixed(1)}${suffix}`
  }
  function formatHours(value?: number | null) {
    if (value === null || value === undefined) return '暂无记录'
    return `${Number(value).toFixed(1)} 小时`
  }

  function inspectionResultLabel(value?: string | null) {
    return value ? labelFor(MAINTENANCE_INSPECTION_RESULT_LABELS, value, '未知结果') : '未记录'
  }
  function maintenanceStatusLabel(value?: string | null) {
    const labels: Record<string, string> = {
      open: '待处理',
      opened: '待处理',
      scheduled: '已排程',
      inprogress: '处理中',
      'in-progress': '处理中',
      completed: '已完成',
      closed: '已关闭',
      cancelled: '已取消',
    }
    // 词表漏了就说「未知状态」，绝不把后端英文码回吐到界面上。
    return value ? (labels[value.toLowerCase()] ?? '未知状态') : '未知'
  }
  /** 维修工单以正式单号标识（#3852），附开单时间。 */
  function workOrderLabel(row: { workOrderNo?: string | null; openedAtUtc?: string | null }) {
    const no = maintenanceWorkOrderNoLabel(row.workOrderNo)
    return row.openedAtUtc ? `${no} · ${formatDateTime(row.openedAtUtc)}` : no
  }
  const alarmCodeById = computed(
    () =>
      new Map(
        activeAlarms.value
          .filter((alarm) => alarm.alarmEventId && alarm.alarmCode)
          .map((alarm) => [alarm.alarmEventId!, alarm.alarmCode!]),
      ),
  )
  function alarmLabel(alarmId?: string | null) {
    return (alarmId && alarmCodeById.value.get(alarmId)) || '—'
  }
  function sparePartWorkOrderLabel(row: {
    workOrderId?: string | null
    workOrderNo?: string | null
  }) {
    if (!row.workOrderId) return '未关联'
    return maintenanceWorkOrderNoLabel(row.workOrderNo)
  }
  /** 保养/点检周期：先查常用说法，其余按 ISO 8601 周期翻译（P7D → 每 7 天）。 */
  function intervalLabel(value?: string | null) {
    const labels: Record<string, string> = {
      P7D: '每周',
      P14D: '每两周',
      P30D: '每月',
      P90D: '每季度',
      P180D: '每半年',
      P365D: '每年',
    }
    if (!value) return '未设置'
    return labels[value.toUpperCase()] ?? formatIsoInterval(value)
  }
  function quantityLabel(row: { quantity?: number | null; uomCode?: string | null }) {
    if (row.quantity === null || row.quantity === undefined) return '未记录'
    // 单位按主数据名称显示（如「件」），名录里没有时才显示单位编码本身（租户维护的业务编码）。
    return `${row.quantity} ${resolveUom(row.uomCode) ?? row.uomCode ?? ''}`.trim()
  }
  function recordDowntime() {
    void router.push({ path: '/mes/downtime', query: { deviceAssetId: filters.deviceAssetId } })
  }

  return {
    MAINTENANCE_DETAIL_TAKE,
    activeAlarms,
    alarmLabel,
    availabilityWindows,
    badgeVariant,
    blockCount,
    canControlDevice,
    canCreateMaintenanceWorkOrder,
    canManageTelemetryPoints,
    columns,
    controlColumns,
    controlCommandRowKey,
    controlCommands,
    controlCommandsErrorMessage,
    refreshControlCommands,
    controlCommandsPending,
    controlCommandsTotal,
    controlPage,
    controlPageSize,
    controlSheetOpen,
    cumulativeRuntimeCardValue,
    currentDeviceInspections,
    currentDevicePlans,
    currentDeviceRuntimePlan,
    currentDeviceSpareParts,
    currentDeviceWorkOrders,
    currentState,
    deviceCode,
    deviceName,
    devicePending,
    deviceTitle,
    errorMessage,
    filters,
    health,
    healthErrorMessage,
    healthPending,
    historyCount,
    historyFilters,
    inspectionResultLabel,
    intervalLabel,
    maintenanceAvailabilityWindows,
    maintenanceErrorMessage,
    maintenanceEventSegments,
    maintenanceEventTotal,
    maintenancePending,
    maintenanceStatusLabel,
    mostUrgentOkRuntimeCandidate,
    oee,
    oeeDegradedReasons,
    oeeFacets,
    oeeFilters,
    pointsSheetOpen,
    quantityLabel,
    rateFoot,
    rateProgress,
    rateValue,
    recordDowntime,
    refreshAll,
    reliabilityCells,
    reliabilityRangeLabel,
    resolveUom,
    runtimeHoursCardHint,
    runtimeUntilNextCardDescription,
    runtimeUntilNextCardHint,
    runtimeUntilNextCardValue,
    runtimeUntilNextProgress,
    severityLabel,
    severityVariant,
    sourceText,
    sourceTone,
    sparePartWorkOrderLabel,
    stateText,
    telemetryErrorMessage,
    telemetryPending,
    workOrderLabel,
  }
}
