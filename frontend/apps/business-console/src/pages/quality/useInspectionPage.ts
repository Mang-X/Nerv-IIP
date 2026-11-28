import {
  emptyLine,
  formatSpecification,
  isVariableLine,
  toMeasuredNumber,
  toCharacteristicResults,
  optionalText,
  splitCsv,
  toOptionalNumber,
  type NumericFieldValue,
  hasRequiredDefectContext,
  isNonEmpty,
} from './inspectionResultLines'
import type {
  BusinessConsoleCreateInspectionRecordRequest,
  BusinessConsoleQualityItem,
} from '@nerv-iip/api-client'
import { listBusinessConsoleQualityInspectionTasks } from '@nerv-iip/api-client'
import type { ComboboxSuggestion, NvDataTableColumn, SearchSelectOption } from '@nerv-iip/ui'
import {
  INSPECTION_PLAN_CATEGORIES,
  inspectionPlanCategoryLabel,
  qualitySourceTypeLabel,
} from '@nerv-iip/business-core'
import {
  useQualityFirstArticlePlanActions,
  useQualityInspectionPlanCharacteristics,
  useQualityInspectionPlans,
} from '@/composables/quality/useBusinessQuality'
import { useQualityOperationContext } from '@/composables/quality/useQualityOperationContext'
import { useQualityInspectionTaskActions } from '@/composables/quality/useQualityInspectionTasks'
import {
  useQualityInspectionPlanCatalog,
  useQualityReasonCatalog,
  useQualitySkuCatalog,
  useQualityUomCatalog,
} from '@/composables/quality/useQualityPickerCatalog'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import { useMasterDataDisplayNames } from '@/composables/master-data/useMasterDataDisplayNames'
import { useSkuNames } from '@/composables/master-data/useSkuNames'
import { usePagedList } from '@/composables/shared/usePagedList'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
  notifyWarning,
} from '@/utils/notify'
import type { SourceDocumentKind } from '@/composables/catalog/useSourceDocumentCatalog'
import { readFaceText } from '@/utils/readFace'
import { recoverLifecycleAction } from '@/composables/shared/lifecycleAction'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'

export function useInspectionPage(onDirectRecordCreated: () => void) {
  const route = useRoute()
  const router = useRouter()
  const auth = useAuthStore()
  const canManageInspectionPlans = computed(() =>
    (auth.principal?.permissionCodes ?? []).includes('business.quality.inspection-plans.manage'),
  )
  const activeView = shallowRef(
    firstQuery(route.query.view) === 'first-article-records' ? 'first-article-records' : 'plans',
  )
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
  const pageHeaderCount = computed(() =>
    activeView.value === 'plans' ? `${inspectionPlansTotal.value} 个检验方案` : undefined,
  )
  const taskActions = useQualityInspectionTaskActions(filters)
  const inspectionPlanCategoryFilter = computed({
    get: () => filters.category ?? 'all',
    set: (value: string) => {
      filters.category = value === 'all' ? undefined : value
    },
  })
  const { page, pageSize } = usePagedList(filters, {
    resetOn: [() => filters.category, () => filters.status, () => filters.keyword],
  })

  const recordSheetOpen = shallowRef(false)
  const firstArticlePlanSheetOpen = shallowRef(false)
  const recordCreatedFromLocatedPlanId = shallowRef('')
  const characteristicsAppliedPlanId = shallowRef('')

  // 业务范围（组织/环境）不进 recordForm：它由 businessContext store 异步落到 filters 上，
  // 而 `<script setup>` 里取一次 `filters.organizationId` 只是**建表时的字符串快照**——
  // BusinessLayout 要等 principal 到位才 patchContext，页面 setup 早于它执行，快照因此永远是空串。
  // 旧写法把这对空串塞进提交前置条件，弹框里又没有任何组织/环境输入位，于是「提交检验记录」
  // 恒为禁用且页面不报红（#1396 / 走查 #79）。范围一律现取 filters，与列表读面同一份真相。
  const recordForm = reactive({
    inspectionPlanId: '',
    sourceType: 'operation',
    sourceService: 'mes-operation',
    sourceDocumentId: '',
    skuCode: '',
    inspectedQuantity: '1' as NumericFieldValue,
    batchNo: '',
    serialNo: '',
    dispositionReason: '',
    dispositionAttachmentFileIds: '',
    resultLines: [emptyLine()],
  })

  // 来源单据按来源类型从单据目录里选，取值与下游比对的值一致：工序检验、终检的结论按工单 ID 回写
  // 制造执行的质量保留，维修检验挂维修工单 ID。收货检验按采购收货单号核对、客户退货没有可搜列表，
  // 这两类自由输入。
  const SOURCE_DOCUMENT_KINDS: Readonly<Record<string, SourceDocumentKind>> = {
    operation: 'mes-work-order',
    final: 'mes-work-order',
    maintenance: 'maintenance-work-order',
  }

  // 换了来源类型，已选的来源单据就不属于这一类了。
  function changeSourceType(value: unknown) {
    recordForm.sourceType = typeof value === 'string' ? value : ''
    recordForm.sourceService =
      (
        {
          operation: 'mes-operation',
          receiving: 'wms',
          final: 'mes',
          maintenance: 'maintenance',
          'customer-return': 'customer-return',
        } as Record<string, string>
      )[recordForm.sourceType] ?? ''
    recordForm.sourceDocumentId = ''
  }

  // 上下文穿透：从工单/工序/收货带入来源单据、批次、序列号。
  const contextWorkOrderId = computed(() => firstQuery(route.query.workOrderId))
  const contextOperationTaskId = computed(() => firstQuery(route.query.operationTaskId))
  const isOperationInspectionFlow = computed(
    () => !!contextOperationTaskId.value && !firstQuery(route.query.inspectionTaskId),
  )
  const operationContext = useQualityOperationContext(filters, () =>
    isOperationInspectionFlow.value ? contextWorkOrderId.value : '',
  )
  const contextOperation = computed(() =>
    operationContext.detail.value?.workOrderId === contextWorkOrderId.value
      ? operationContext.detail.value.operationTasks?.find(
          (task) => task.operationTaskId === contextOperationTaskId.value,
        )
      : undefined,
  )
  const operationContextReady = computed(
    () =>
      !!contextOperation.value &&
      !!operationContext.detail.value?.skuId?.trim() &&
      !operationContext.pending.value &&
      !operationContext.errorMessage.value,
  )
  // 只接续成功提交时已公开核实的工单/工序；不把检验记录当不良领域关联。
  const submittedOperationSource = shallowRef<{
    workOrderId: string
    operationTaskId: string
  } | null>(null)
  const canContinueDefect = computed(() =>
    [
      'business.mes.quality.read',
      'business.mes.quality.write',
      'business.mes.operations.read',
    ].every((permission) => (auth.principal?.permissionCodes ?? []).includes(permission)),
  )
  const defectContinuationRoute = computed(() =>
    submittedOperationSource.value && canContinueDefect.value
      ? { path: '/mes/quality', query: submittedOperationSource.value }
      : undefined,
  )
  const sourceWorkOrderRoute = computed(() => ({
    path: `/mes/work-orders/${encodeURIComponent(contextWorkOrderId.value)}`,
    query: {
      operationTaskId: operationContextReady.value ? contextOperationTaskId.value : undefined,
    },
  }))
  const operationSourceIdentity = computed(() =>
    [
      auth.principal?.principalId,
      filters.organizationId,
      filters.environmentId,
      contextWorkOrderId.value,
      contextOperationTaskId.value,
    ].join(':'),
  )
  watch(operationSourceIdentity, () => {
    submittedOperationSource.value = null
  })
  const operationContextMessage = computed(() => {
    if (!isOperationInspectionFlow.value || operationContextReady.value) return ''
    if (operationContext.errorMessage.value) return operationContext.errorMessage.value
    if (operationContext.pending.value || !hasBusinessContext(filters))
      return '正在加载工序检验对象…'
    return '未取得该工序的产品上下文，请重试检验对象。'
  })
  watch(
    [
      isOperationInspectionFlow,
      operationContextReady,
      () => operationContext.detail.value?.skuId,
      contextWorkOrderId,
      contextOperationTaskId,
    ],
    ([operationFlow, ready, skuId, workOrderId]) => {
      if (!operationFlow) return
      recordForm.skuCode = ready ? skuId!.trim() : ''
      recordForm.sourceDocumentId = workOrderId
      recordForm.sourceType = 'operation'
      recordForm.sourceService = 'mes-operation'
    },
    { immediate: true, flush: 'sync' },
  )
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
      !!targetInspectionPlanId.value &&
      !inspectionPlansPending.value &&
      !targetInspectionPlan.value,
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
      if (skuCode && !isOperationInspectionFlow.value) recordForm.skuCode = skuCode
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
  const { activateFirstArticlePlan, activateFirstArticlePlanPending } =
    useQualityFirstArticlePlanActions(filters)
  // 换检验方案等于换了检验对象：方案自带的物料要跟着走，已填的特性行不再属于新方案，重置回一行空行。
  const recordPlanModel = computed({
    get: () => recordForm.inspectionPlanId,
    set: (value: string) => {
      if (value === recordForm.inspectionPlanId) return
      recordForm.inspectionPlanId = value
      characteristicsAppliedPlanId.value = ''
      recordForm.resultLines = [emptyLine()]
      const plan = planCatalog.inspectionPlans.value.find((item) => item.id === value)
      if (plan?.skuCode && !isOperationInspectionFlow.value) recordForm.skuCode = plan.skuCode
    },
  })

  const listErrorMessage = computed(() => inlineErrorMessage(inspectionPlansError.value))
  const inspectedQuantity = computed(() => toOptionalNumber(recordForm.inspectedQuantity))
  const isInspectionTaskFlow = computed(() => !!firstQuery(route.query.inspectionTaskId))
  const inspectionTaskSubmissionAllowed = shallowRef(false)
  let inspectionTaskGateEpoch = 0
  watch(
    [
      () => firstQuery(route.query.inspectionTaskId),
      () => filters.organizationId,
      () => filters.environmentId,
    ],
    async ([inspectionTaskId, organizationId, environmentId]) => {
      const epoch = ++inspectionTaskGateEpoch
      if (!inspectionTaskId) {
        inspectionTaskSubmissionAllowed.value = true
        return
      }
      inspectionTaskSubmissionAllowed.value = false
      if (!organizationId.trim() || !environmentId.trim()) return
      try {
        const { data } = await listBusinessConsoleQualityInspectionTasks({
          query: {
            organizationId,
            environmentId,
            inspectionTaskId,
            skip: 0,
            take: 2,
          },
          throwOnError: true,
        })
        if (epoch !== inspectionTaskGateEpoch) return
        const exactTasks = (data?.success ? (data.data?.items ?? []) : []).filter(
          (task) => task.inspectionTaskId === inspectionTaskId,
        )
        inspectionTaskSubmissionAllowed.value =
          exactTasks.length === 1 &&
          Boolean(exactTasks[0]?.allowedActions?.includes('submit-inspection'))
      } catch {
        if (epoch === inspectionTaskGateEpoch) inspectionTaskSubmissionAllowed.value = false
      }
    },
    { immediate: true },
  )
  // 从待检任务行进入时来源字段全部由任务带出——只读呈现，不给用户改的错觉。
  const recordContextItems = computed(() => [
    // 方案标识是 GUID，直接甩给用户等于没说（#1418）。目录里已备好 id → 人读方案号的映射，
    // 走它回显；映射尚未加载或方案已归档时才退回标识本身。
    {
      label: '检验方案',
      value:
        planCatalog.inspectionPlanCodeById.value.get(recordForm.inspectionPlanId) ??
        recordForm.inspectionPlanId,
    },
    { label: '来源类型', value: sourceTypeLabel(recordForm.sourceType) },
    { label: '来源单据', value: recordForm.sourceDocumentId },
    { label: '物料', value: recordForm.skuCode },
    ...(isOperationInspectionFlow.value
      ? [
          {
            label: '来源工序',
            value:
              contextOperation.value?.operationCode ||
              (contextOperation.value ? `工序 ${contextOperation.value.operationSequence}` : ''),
          },
        ]
      : []),
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
  // 提交前置条件收口成一份**会上屏的**清单：按钮为什么灰，页面就得逐条说出来。
  // 旧写法是一串布尔与，任一项不满足按钮即灰、界面却不说缺什么——检验员只能干瞪眼（走查 #79）。
  // 规则：`submitBlockers` 为空 ⟺ 可提交，两者由同一份数据推出，不可能再出现「无红字却提交不了」。
  const submitBlockers = computed<string[]>(() => {
    const blockers: string[] = []
    if (!hasBusinessContext(filters)) {
      blockers.push('尚未确定当前组织。')
    }
    if (isInspectionTaskFlow.value && !inspectionTaskSubmissionAllowed.value) {
      blockers.push(
        '该待检任务当前不允许提交检验（可能已被他人接手或已闭合），请返回待检工作台重新选取任务。',
      )
    }
    if (isOperationInspectionFlow.value && !operationContextReady.value) {
      blockers.push(operationContextMessage.value)
    }
    if (!isNonEmpty(recordForm.sourceType)) blockers.push('请选择来源类型。')
    if (!isNonEmpty(recordForm.sourceService)) blockers.push('请选择来源服务。')
    if (!isNonEmpty(recordForm.sourceDocumentId)) blockers.push('请填写来源单据号。')
    if (!isNonEmpty(recordForm.skuCode)) blockers.push('请选择被检物料 SKU。')
    if (inspectedQuantity.value === undefined || inspectedQuantity.value <= 0) {
      blockers.push('检验数量必须是大于 0 的数值。')
    }
    if (isInspectionTaskFlow.value && planCharacteristicsPending.value) {
      blockers.push('检验特性与规格仍在加载，请稍候。')
    }
    if (isInspectionTaskFlow.value && planCharacteristicsError.value) {
      blockers.push('检验特性与规格加载失败，请点上方「重试」重新加载后再提交。')
    }
    // 逐行点名：只说「有行没填完」等于没说，检验员得知道是第几行、缺哪一项。
    recordForm.resultLines.forEach((line, index) => {
      const name = line.characteristicName.trim() || line.characteristicCode.trim()
      const label = name ? `第 ${index + 1} 行「${name}」` : `第 ${index + 1} 行`
      if (!isNonEmpty(line.characteristicCode)) {
        blockers.push(`${label}：请先选择检验特性。`)
        return
      }
      if (isVariableLine(line) && toMeasuredNumber(line.measuredValue) === undefined) {
        blockers.push(`${label}：请填写数值测量值。`)
      }
      if (!isVariableLine(line) && !isNonEmpty(line.observedValue)) {
        blockers.push(`${label}：请填写实测值。`)
      }
      if (line.result !== 'passed' && !isNonEmpty(line.defectReason)) {
        blockers.push(`${label}：结果不是「合格」时必须选择缺陷原因。`)
      }
      if (
        line.result === 'conditional-release' &&
        (toOptionalNumber(line.defectQuantity) ?? 0) <= 0
      ) {
        blockers.push(`${label}：让步放行必须填写大于 0 的缺陷数量。`)
      }
    })
    if (requiresDispositionReason.value && !isNonEmpty(recordForm.dispositionReason)) {
      blockers.push('存在不合格 / 让步放行的特性，必须选择处置原因。')
    }
    return blockers
  })
  const canCreateRecord = computed(() => submitBlockers.value.length === 0)

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
  function useInspectionPlan(plan: BusinessConsoleQualityItem) {
    recordForm.inspectionPlanId = plan.id ?? ''
    if (plan.skuCode && !isOperationInspectionFlow.value && !firstQuery(route.query.skuCode))
      recordForm.skuCode = plan.skuCode
    if (plan.category?.trim().toLowerCase() === 'first-article') {
      recordForm.sourceType = 'first-article'
      recordForm.sourceService = 'mes-operation'
    }
    recordSheetOpen.value = true
  }
  async function activateDraftFirstArticlePlan(plan: BusinessConsoleQualityItem) {
    const inspectionPlanId = plan.id?.trim()
    if (!inspectionPlanId) return
    try {
      await activateFirstArticlePlan(inspectionPlanId)
      notifySuccess(`首件检验方案 ${plan.code ?? ''} 已启用。`)
      await refreshInspectionPlans()
    } catch (error) {
      notifyOperationFailure('首件方案启用失败', error, '首件检验方案启用失败，请稍后重试。')
    }
  }
  async function openFirstArticleRecord(inspectionRecordId: string) {
    activeView.value = 'first-article-records'
    recordDetailOpen.value = true
    await router.push({
      path: '/quality/inspections',
      query: { ...route.query, view: 'first-article-records', inspectionRecordId },
    })
  }
  function isDraftFirstArticlePlan(plan: BusinessConsoleQualityItem) {
    return (
      plan.category?.trim().toLowerCase() === 'first-article' &&
      plan.status?.trim().toLowerCase() === 'draft'
    )
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
    // 换特性必须换单位，包括「换成无量纲特性」：旧写法只在新特性有单位时才覆盖，
    // 于是从「阻尼力（N）」切到「渗漏检查（无单位）」会把 N 留在行上一起提交。
    line.unitCode = characteristic.unitCode ?? ''
    line.specification = formatSpecification(characteristic)
  }

  // 检验特性的单位不是「随便从计量单位表里挑一个」，而是**该特性自己的量纲**：
  // 检验方案给每条特性写死了 unitCode（阻尼力 N、行程 mm），计数型特性则根本无量纲（null）。
  // 后端也是这么判的——单位与方案不一致且无换算关系时直接拒收
  // （InspectionRecord.cs「unit ... does not match plan unit」）。
  // 旧写法给每一行都套全局计量单位目录，而目录里只有 g/kg/l/min/pcs：
  // 规格文案写着「1080–1320 N」，候选里却没有 N，检验员只能选个错的（走查 #80）。
  // 现在：计划内特性 → 单位由特性派生、只读呈现；计划外特性 → 才回落到计量单位目录自由选。
  function planCharacteristicOf(code: string) {
    const normalized = code.trim().toLowerCase()
    if (!normalized) return undefined
    return planCharacteristics.value.find(
      (item) => (item.characteristicCode ?? '').trim().toLowerCase() === normalized,
    )
  }
  type UnitPolicy =
    | { mode: 'derived'; unitCode: string }
    | { mode: 'dimensionless' }
    | { mode: 'free' }
  function unitPolicyFor(line: { characteristicCode: string }): UnitPolicy {
    const characteristic = planCharacteristicOf(line.characteristicCode)
    if (!characteristic) return { mode: 'free' }
    const unitCode = (characteristic.unitCode ?? '').trim()
    return unitCode ? { mode: 'derived', unitCode } : { mode: 'dimensionless' }
  }
  /** 派生单位在计量单位目录里有中文名就显「中文名（码）」，查不到就只显码——不编造名字。 */
  function unitDisplay(unitCode: string) {
    const known = uomCatalog.uomOptions.value.find((option) => option.value === unitCode)
    return known?.label && known.label !== unitCode ? `${known.label}（${unitCode}）` : unitCode
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

  function inspectionSubmissionSubject() {
    const sourceDocumentNo = readFaceText(firstQuery(route.query.sourceDocumentNo), '')
    return sourceDocumentNo && sourceDocumentNo === recordForm.sourceDocumentId.trim()
      ? `来源单据 ${sourceDocumentNo} 的检验记录`
      : '检验记录'
  }

  async function submitInspectionRecord() {
    if (!hasBusinessContext(filters)) {
      notifyError('业务范围尚未就绪，请稍后重试。')
      return
    }
    if (!canCreateRecord.value) return
    const inspectionTaskId = firstQuery(route.query.inspectionTaskId)
    if (inspectionTaskId) {
      let inspectionRecordId: string | null | undefined
      try {
        const response = await taskActions.startInspection(inspectionTaskId, {
          resultLines: toCharacteristicResults(validResultLines.value),
          dispositionReason: optionalText(recordForm.dispositionReason),
          dispositionAttachmentFileIds: splitCsv(recordForm.dispositionAttachmentFileIds),
        })
        inspectionRecordId = response.data?.inspectionRecordId
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
      notifySuccess(`${inspectionSubmissionSubject()}已提交，待检任务已闭合。`)
      await router.push({
        path: '/quality/inspection-tasks',
        ...(inspectionRecordId ? { query: { inspectionRecordId } } : {}),
      })
      return
    }
    const body: BusinessConsoleCreateInspectionRecordRequest = {
      organizationId: filters.organizationId.trim(),
      environmentId: filters.environmentId.trim(),
      inspectionPlanId: optionalText(recordForm.inspectionPlanId),
      sourceType: recordForm.sourceType.trim(),
      sourceService: recordForm.sourceService.trim(),
      sourceDocumentId: recordForm.sourceDocumentId.trim(),
      skuCode: recordForm.skuCode.trim(),
      inspectedQuantity: inspectedQuantity.value,
      batchNo: optionalText(recordForm.batchNo),
      serialNo: optionalText(recordForm.serialNo),
      resultLines: toCharacteristicResults(validResultLines.value),
      dispositionReason: optionalText(recordForm.dispositionReason),
      dispositionAttachmentFileIds: splitCsv(recordForm.dispositionAttachmentFileIds),
    }
    const sourceIdentity = operationSourceIdentity.value
    const operationSource =
      isOperationInspectionFlow.value &&
      operationContextReady.value &&
      body.sourceType === 'operation' &&
      body.sourceService === 'mes-operation' &&
      body.sourceDocumentId === contextWorkOrderId.value
        ? { workOrderId: contextWorkOrderId.value, operationTaskId: contextOperationTaskId.value }
        : null
    submittedOperationSource.value = null
    let createdRecordId: string | undefined
    try {
      const response = await createInspectionRecord(body)
      createdRecordId = response?.data?.inspectionRecordId
    } catch (error) {
      notifyOperationFailure('检验记录提交失败', error, '检验记录提交失败，请稍后重试。')
      return
    }
    recordSheetOpen.value = false
    if (sourceIdentity === operationSourceIdentity.value)
      submittedOperationSource.value = operationSource
    notifySuccess(`${inspectionSubmissionSubject()}已提交。`)
    if (createdRecordId) {
      onDirectRecordCreated()
      if (!operationSource) await openFirstArticleRecord(createdRecordId)
    }
    if (body.resultLines?.some((line) => line.result === 'failed')) {
      notifyWarning('若检验结果判定为不合格且尚未关联报告，请在检验记录详情开立 NCR。')
    }
  }

  const INSPECTION_PLAN_CATEGORY_OPTIONS = INSPECTION_PLAN_CATEGORIES.map((value) => ({
    value,
    label: inspectionPlanCategoryLabel(value),
  }))
  // 来源类型是英文码，只读带出区要显示中文（映射来自 business-core qualityLabels，PC/PDA 同源）。
  function sourceTypeLabel(value?: string | null) {
    return qualitySourceTypeLabel(value)
  }
  /** 「中文名（编码）」；名录查不到就只显编码，不编造名字。 */
  function namedCode(code: string | null | undefined, name: string | undefined) {
    if (!isPresent(code)) return undefined
    return name ? `${name}（${code}）` : code
  }
  function qualityItemSummary(item: BusinessConsoleQualityItem) {
    const values = [
      inspectionPlanCategoryLabel(item.category),
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
  function isPresent(value: string | undefined | null): value is string {
    return typeof value === 'string' && value.trim().length > 0
  }

  return {
    INSPECTION_PLAN_CATEGORY_OPTIONS,
    SOURCE_DOCUMENT_KINDS,
    activateDraftFirstArticlePlan,
    activateFirstArticlePlanPending,
    activeView,
    addCharacteristicRow,
    canCreateRecord,
    canManageInspectionPlans,
    changeSourceType,
    characteristicOptions,
    characteristicSuggestions,
    characteristicsPlanId,
    columns,
    contextWorkOrderId,
    createInspectionRecordPending,
    defectContinuationRoute,
    filters,
    firstArticlePlanSheetOpen,
    inspectionPlanCategoryFilter,
    inspectionPlans,
    inspectionPlansPending,
    inspectionPlansTotal,
    isDraftFirstArticlePlan,
    isInspectionTaskFlow,
    isOperationInspectionFlow,
    isVariableLine,
    listErrorMessage,
    onCharacteristicCodeChange,
    openFirstArticleRecord,
    operationContext,
    operationContextMessage,
    page,
    pageHeaderCount,
    pageSize,
    planCatalog,
    planCharacteristicsError,
    planCharacteristicsPending,
    reasonCatalog,
    recordContextItems,
    recordDetailId,
    recordDetailOpen,
    recordForm,
    recordPlanModel,
    recordSheetOpen,
    refreshInspectionPlans,
    refreshPlanCharacteristics,
    removeCharacteristicRow,
    requiresDispositionReason,
    scanAuditRoute,
    skuCatalog,
    sourceWorkOrderRoute,
    submitBlockers,
    submitInspectionRecord,
    targetInspectionPlanId,
    targetInspectionPlanMissing,
    unitDisplay,
    unitPolicyFor,
    uomCatalog,
    useInspectionPlan,
  }
}
