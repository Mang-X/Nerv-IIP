import type {
  BusinessConsoleAuthorizedWorkScope,
  BusinessConsoleMaintenanceSparePartInput,
  BusinessConsoleMaintenanceWorkOrderItem,
} from '@nerv-iip/api-client'
import {
  formatWorkScopeKey,
  maintenancePriorityLabel,
  maintenancePriorityLabels,
  statusActionGate,
} from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { recoverLifecycleAction } from '@/composables/shared/lifecycleAction'
import { useMaintenanceWorkOrders } from '@/composables/maintenance/useBusinessMaintenance'
import { useMaintenanceDowntimeReasonDirectory } from '@/composables/maintenance/useMaintenanceDowntimeReasonDirectory'
import { useBusinessWorkers, useBusinessMasterDataResources } from '@/composables/master-data'
import {
  maintenanceWorkOrderNoLabel,
  useDeviceSiteLookup,
  useEquipmentAlarmCatalog,
  useEquipmentSkuCatalog,
  useEquipmentUomCatalog,
} from '@/composables/equipment/useEquipmentPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/master-data/useBusinessPartnerNames'
import { usePagedList } from '@/composables/shared/usePagedList'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { formatDateTime as formatLocalDateTime } from '@/utils/format'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import { storeToRefs } from 'pinia'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

export function useMaintenanceWorkOrdersPage() {
  const {
    workOrders,
    workOrdersError,
    workOrdersPending,
    workOrdersTotal,
    refreshWorkOrders,
    createWorkOrder,
    createWorkOrderPending,
    readExpectedRestoreScopes,
    updateExpectedRestore,
    expectedRestorePending,
    completeWorkOrder,
    completeWorkOrderPending,
    filters,
  } = useMaintenanceWorkOrders()
  const { page, pageSize } = usePagedList(filters)
  const route = useRoute()
  const router = useRouter()

  // 技师目录（人员选择器数据源，读自 /master-data/workers）。
  const { workers, workersPending } = useBusinessWorkers()
  // 设备台账（列表里把编号解析成设备名，读自 master-data device-asset 资源）。
  const { resources: deviceResources } = useBusinessMasterDataResources('device-asset')
  // 完工登记的换件行：物料与单位从主数据选，单位默认跟随物料基本单位。
  const { baseUomBySku } = useEquipmentSkuCatalog()
  const { uomOptions, uomsPending } = useEquipmentUomCatalog()
  function applySpareRowSku(row: { skuCode: string; uomCode: string }) {
    const code = row.skuCode.trim()
    const baseUom = baseUomBySku.value.get(code)
    if (baseUom) {
      row.uomCode = baseUom
      return
    }
    if (!code) return
    // 就地新建的物料要等物料列表刷新回来才查得到基本单位：等到查得到再带出，期间该行换了物料就作废。
    const stop = watch(
      () => baseUomBySku.value.get(code),
      (next) => {
        if (!next) return
        stop()
        if (row.skuCode.trim() === code) row.uomCode = next
      },
    )
  }
  const deviceNameByCode = computed(() => {
    const map = new Map<string, string>()
    for (const r of deviceResources.value) {
      if (r.code) map.set(r.code, r.displayName ?? r.code)
    }
    return map
  })
  // 设备所在工厂：维修工单归属设备所在工厂，备件从该工厂已登记的库位领出（#3902）。
  const { deviceSiteCode, siteLabel } = useDeviceSiteLookup()
  /** 设备展示串：名称优先，台账查不到就只显编号，不编名字。 */
  function deviceLabel(code?: string | null, fallback = '—') {
    if (!code) return fallback
    return deviceNameByCode.value.get(code) ?? code
  }
  // 外部维修供应商只回编码（SUP-…），客户/供应商中文名在主数据业务伙伴里。
  const { resolvePartner } = useBusinessPartnerNames()
  // 当前登录用户（开单人默认当前用户，可改选他人，不自由输入）。
  const auth = useAuthStore()
  const { principal } = storeToRefs(auth)
  const currentUserId = computed(() => principal.value?.principalId ?? '')
  const canManageWorkOrders = computed(() =>
    (principal.value?.permissionCodes ?? []).includes(P.maintenanceWorkOrdersManage),
  )
  const workerOptions = computed(() =>
    workers.value
      .map((w) => ({
        value: w.userId ?? '',
        label: w.displayName ?? w.userId ?? '',
        hint: w.employeeNo ?? undefined,
      }))
      .filter((o) => o.value.length > 0),
  )
  function personLabel(userId: string) {
    return (
      workers.value.find((w) => w.userId === userId)?.displayName ??
      principal.value?.loginName ??
      userId
    )
  }

  const priorityOptions = [
    { label: '高', value: 'high' },
    { label: '中', value: 'medium' },
    { label: '低', value: 'low' },
  ]
  const resultOptions = [
    { label: '已修复', value: 'repaired' },
    { label: '已更换部件', value: 'replaced' },
    { label: '已校准', value: 'calibrated' },
  ]
  // 指派技师 / 实际技师均走 master-data 复用件 WorkerSelect（服务端检索工人目录，绑 userId）。
  // 建单写 assignedTechnicianUserId、完工写 actualTechnicianUserId（#897 已补契约）。
  // 可靠性汇总按技师聚合。

  // 待执行 / 已完成基于本页可见行的语义统计（可行动数，非机械总数）。
  const OPEN_STATUSES = new Set(['open', 'opened', 'scheduled', 'inprogress', 'in-progress'])
  const pendingCount = computed(
    () => workOrders.value.filter((w) => OPEN_STATUSES.has((w.status ?? '').toLowerCase())).length,
  )
  const highPriorityPending = computed(
    () =>
      workOrders.value.filter(
        (w) =>
          OPEN_STATUSES.has((w.status ?? '').toLowerCase()) &&
          (w.priority ?? '').toLowerCase() === 'high',
      ).length,
  )

  const createOpen = shallowRef(false)
  const createForm = reactive({
    deviceAssetId: '',
    priority: 'medium',
    openedByUserId: '',
    sourceAlarmId: '',
    assignedTechnicianUserId: '',
    estimatedLaborMinutes: '',
    assetUnavailableReasonCode: '',
    unavailabilityMode: 'none',
    expectedRestoreAtLocal: '',
  })
  const createError = shallowRef('')
  const { alarmOptions, alarmsPending } = useEquipmentAlarmCatalog(() => createForm.deviceAssetId)
  // 用户换了设备，原先挑的报警就不属于这台设备了。清空写在设备框的 setter 里，
  // 只有用户操作才触发；从报警页带入建单时直接写字段，不会误清带入的报警。
  const createDeviceModel = computed({
    get: () => createForm.deviceAssetId,
    set: (value: string) => {
      if (value === createForm.deviceAssetId) return
      createForm.deviceAssetId = value
      createForm.sourceAlarmId = ''
    },
  })
  const downtimeReasons = useMaintenanceDowntimeReasonDirectory(filters)
  const {
    keyword: reasonKeyword,
    options: downtimeReasonOptions,
    state: directoryState,
    message: directoryMessage,
    total: reasonsTotal,
  } = downtimeReasons
  // 完工登记的停机原因与建单同一份权威目录（#3855）。以前是写死的三个码，目录里根本没有，
  // 服务端按目录硬校验，完工一律失败。单独一个实例：两个抽屉的检索词互不串。
  const completeReasons = useMaintenanceDowntimeReasonDirectory(filters)
  const {
    keyword: completeReasonKeyword,
    options: completeReasonOptions,
    state: completeReasonState,
    message: completeReasonMessage,
    total: completeReasonsTotal,
  } = completeReasons
  watch(
    () => [filters.organizationId, filters.environmentId],
    () => {
      createForm.assetUnavailableReasonCode = ''
      reasonKeyword.value = ''
      completeReasonKeyword.value = ''
    },
    { flush: 'sync' },
  )

  // 停机原因就地新增：目录里没有要用的原因时不离开当前抽屉，建好后自动选中（#3855）。
  const reasonFormSession = shallowRef(0)
  const reasonFormOpen = shallowRef(false)
  let reasonFormTarget: 'create' | 'complete' = 'create'
  function openReasonCreate(target: 'create' | 'complete') {
    reasonFormTarget = target
    reasonFormSession.value += 1
    reasonFormOpen.value = true
  }
  const knownReasonCodes = computed(() => [
    ...new Set([
      ...downtimeReasons.reasons.value.map((row) => row.reasonCode),
      ...completeReasons.reasons.value.map((row) => row.reasonCode),
    ]),
  ])
  function onReasonSaved(reasonCode: string) {
    if (reasonFormTarget === 'create') createForm.assetUnavailableReasonCode = reasonCode
    else completeForm.downtimeReasonCode = reasonCode
  }

  interface SparePartRow {
    id: number
    skuCode: string
    quantity: string
    uomCode: string
    unitCost: string
    /** 领出库位：设备所在工厂下已登记的库位（#3902）。 */
    locationCode: string
  }
  let nextSpareRowId = 1
  function createSpareRow(): SparePartRow {
    // 单位留空：选完物料自动带出它的基本单位；主档没维护就由操作员选，不预填假单位。
    return {
      id: nextSpareRowId++,
      skuCode: '',
      quantity: '1',
      uomCode: '',
      unitCost: '',
      locationCode: '',
    }
  }

  const completeOpen = shallowRef(false)
  const completeTarget = shallowRef<BusinessConsoleMaintenanceWorkOrderItem>()
  const completeForm = reactive({
    result: 'repaired',
    // 停机原因从目录里选，不预填：预填码一旦不在目录里，完工就会被拒。
    downtimeReasonCode: '',
    downtimeMinutes: '30',
    actualLaborMinutes: '',
    externalServiceCostAmount: '',
    costCurrencyCode: 'CNY',
    // 实际执行技师（#897 完工契约 actualTechnicianUserId，userId）。默认取建单指派技师。
    actualTechnicianUserId: '',
  })
  const spareRows = reactive<SparePartRow[]>([createSpareRow()])
  // 备件成本覆盖：空串 = 未覆盖（用自动合计）；非空 = 人工改写值。
  const sparePartCostOverride = shallowRef('')
  const completeError = shallowRef('')

  // 自动合计：Σ(数量 × 单价)，仅计入数值有效的行。
  const autoSparePartCost = computed(() =>
    spareRows.reduce((sum, row) => {
      // number 输入框经 v-model 可能回传 number，String() 归一后再判空/解析。
      const unitRaw = String(row.unitCost ?? '').trim()
      const qty = Number(row.quantity)
      const unit = Number(unitRaw)
      if (!unitRaw || !Number.isFinite(qty) || !Number.isFinite(unit)) return sum
      return sum + qty * unit
    }, 0),
  )
  // 展示/编辑用：未覆盖时回显自动合计，人工输入即视为覆盖。
  const sparePartCostDisplay = computed({
    get: () =>
      sparePartCostOverride.value !== ''
        ? sparePartCostOverride.value
        : autoSparePartCost.value > 0
          ? String(round2(autoSparePartCost.value))
          : '',
    set: (value: string) => {
      sparePartCostOverride.value = value
    },
  })
  const listErrorMessage = computed(() => inlineErrorMessage(workOrdersError.value))
  // 服务端错误走 toast；这里只留点提交后的字段级校验汇总。
  const createErrorMessage = computed(() => createError.value)
  const completeErrorMessage = computed(() => completeError.value)
  const queryPrefilled = shallowRef(false)
  // 从报警行「创建维修工单」带入时：设备与来源报警是既定事实，只读呈现，不再给输入位。
  const createCarried = shallowRef(false)
  const createCarriedItems = computed(() => [
    { label: '设备', value: deviceLabel(createForm.deviceAssetId) },
    { label: '来源报警', value: createForm.sourceAlarmId },
  ])
  const completeCarriedItems = computed(() => {
    const target = completeTarget.value
    if (!target) return []
    // 「—」是列表占位符，不是事实——只读区里直接不渲染该条。
    const omitPlaceholder = (value: string) => (value === '—' ? undefined : value)
    return [
      { label: '工单号', value: workOrderNo(target) },
      { label: '设备', value: deviceLabel(target.deviceAssetId) },
      { label: '优先级', value: omitPlaceholder(priorityLabel(target.priority)) },
      { label: '保修', value: omitPlaceholder(warrantyStatusLabel(target.warrantyStatus)) },
      {
        label: '供应商',
        value: target.supplierPartnerCode
          ? (resolvePartner(target.supplierPartnerCode) ?? target.supplierPartnerCode)
          : undefined,
      },
      { label: '开单时间', value: omitPlaceholder(formatOpenedAt(target.openedAtUtc)) },
      {
        label: '备件领出工厂',
        value: completeSiteCode.value ? siteLabel(completeSiteCode.value) : '设备未登记所属工厂',
      },
    ]
  })
  /** 完工工单的设备所在工厂：备件的领出库位只在这个工厂里选。 */
  const completeSiteCode = computed(() => deviceSiteCode(completeTarget.value?.deviceAssetId))

  type WorkOrderRow = BusinessConsoleMaintenanceWorkOrderItem
  // 没有可用操作的角色不渲染「操作」列，免得只剩表头、整列空白。
  const columns = computed<NvDataTableColumn<WorkOrderRow>[]>(() => [
    {
      key: 'workOrderNo',
      header: '工单号',
      // 正式单号形如 MWO-yyyyMMdd-NNNNNN，固定列宽避免与设备列重叠（#3852）
      width: 'w-48',
      cellClass: 'font-medium whitespace-nowrap',
      accessor: (r) => workOrderNo(r),
    },
    {
      key: 'deviceAssetId',
      header: '设备',
      width: 'w-32',
      cellClass: 'truncate',
      accessor: (r) =>
        r.deviceAssetId && deviceNameByCode.value.has(r.deviceAssetId)
          ? `${deviceLabel(r.deviceAssetId)} ${r.deviceAssetId}`
          : (r.deviceAssetId ?? '—'),
    },
    { key: 'warrantyStatus', header: '保修', width: 'w-20' },
    {
      key: 'warrantyExpiresOn',
      header: '保修到期',
      width: 'w-24',
      accessor: (r) => formatDate(r.warrantyExpiresOn),
    },
    {
      key: 'supplierPartnerCode',
      header: '供应商',
      width: 'w-24',
      accessor: (r) =>
        r.supplierPartnerCode
          ? (resolvePartner(r.supplierPartnerCode) ?? r.supplierPartnerCode)
          : '—',
    },
    { key: 'priority', header: '优先级', width: 'w-16' },
    { key: 'status', header: '状态', width: 'w-20' },
    {
      key: 'inventoryPostingFailureReason',
      header: '备件过账',
      width: 'w-24',
      accessor: (row) =>
        row.inventoryPostingFailureReason ? `过账失败：${row.inventoryPostingFailureReason}` : '—',
    },
    {
      key: 'assignedTechnicianUserId',
      header: '技师',
      width: 'w-20',
      accessor: (r) => technicianLabel(r.assignedTechnicianUserId),
    },
    {
      key: 'expectedRestoreAtUtc',
      header: '预计恢复',
      width: 'w-32',
      accessor: (r) => formatDateTime(r.expectedRestoreAtUtc),
    },
    {
      key: 'openedAtUtc',
      header: '开单时间',
      width: 'w-32',
      accessor: (r) => formatOpenedAt(r.openedAtUtc),
    },
    ...(canManageWorkOrders.value
      ? [
          {
            key: 'actions',
            header: '操作',
            width: 'w-16',
            headerClass: 'sticky end-0 z-20 border-l bg-card',
            cellClass: 'sticky end-0 z-10 border-l bg-card',
            align: 'end' as const,
          },
        ]
      : []),
  ])

  /** 人读工单号：后端按编码规则分配的正式单号（#3852，MWO-yyyyMMdd-NNNNNN），可按单号检索。 */
  function workOrderNo(row: WorkOrderRow) {
    return maintenanceWorkOrderNoLabel(row.workOrderNo)
  }
  // 建单只开放高/中/低三档，但报警自动开单等来源还会带 critical/urgent/normal；
  // 显示统一走 business-core 的跨端生产词表，PC 的空值/未知值继续显示破折号。
  function priorityLabel(value?: string | null) {
    const code = (value ?? '').trim().toLowerCase()
    if (!Object.hasOwn(maintenancePriorityLabels, code)) return '—'
    return maintenancePriorityLabel(code)
  }
  function technicianLabel(userId?: string | null) {
    if (!userId) return '未指派'
    const worker = workers.value.find((w) => w.userId === userId)
    return worker?.displayName ?? userId
  }
  function warrantyStatusLabel(value?: string | null) {
    switch ((value ?? '').toLowerCase()) {
      case 'in-warranty':
        return '在保'
      case 'out-of-warranty':
        return '出保'
      default:
        // 设备没登记保修信息不是一种"状态"，用占位符弱化，别喊「未知」制造疑问。
        return '—'
    }
  }
  function rowKey(row: WorkOrderRow) {
    return row.workOrderId ?? '维护工单'
  }
  function round2(value: number) {
    return Math.round(value * 100) / 100
  }

  function openCreate(prefill: Partial<typeof createForm> = {}) {
    createCarried.value = Boolean(prefill.deviceAssetId || prefill.sourceAlarmId)
    createForm.deviceAssetId = prefill.deviceAssetId ?? ''
    createForm.priority = 'medium'
    createForm.openedByUserId = currentUserId.value
    createForm.sourceAlarmId = prefill.sourceAlarmId ?? ''
    createForm.assignedTechnicianUserId = ''
    createForm.estimatedLaborMinutes = ''
    createForm.assetUnavailableReasonCode = ''
    createForm.unavailabilityMode = 'none'
    createForm.expectedRestoreAtLocal = ''
    reasonKeyword.value = ''
    createError.value = ''
    createOpen.value = true
  }
  async function submitCreate() {
    if (!createForm.deviceAssetId.trim() || !createForm.openedByUserId) {
      createError.value = '请填写设备并选择开单人。'
      return
    }
    const estimatedLaborMinutes = optionalNonNegativeInt(createForm.estimatedLaborMinutes)
    if (estimatedLaborMinutes === false) {
      createError.value = '预估工时需为非负整数。'
      return
    }
    if (
      createForm.unavailabilityMode === 'reason' &&
      (directoryState.value !== 'ok' || !createForm.assetUnavailableReasonCode)
    ) {
      createError.value = directoryMessage.value || '请选择设备占用原因。'
      return
    }
    const body = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      deviceAssetId: createForm.deviceAssetId.trim(),
      priority: createForm.priority,
      openedBy: personLabel(createForm.openedByUserId),
      sourceAlarmId: createForm.sourceAlarmId.trim() || undefined,
      assignedTechnicianUserId: createForm.assignedTechnicianUserId || undefined,
      assetUnavailableReasonCode:
        createForm.unavailabilityMode === 'reason' ? createForm.assetUnavailableReasonCode : null,
      expectedRestoreAtUtc:
        createForm.unavailabilityMode === 'reason' && createForm.expectedRestoreAtLocal
          ? new Date(createForm.expectedRestoreAtLocal).toISOString()
          : null,
      ...(estimatedLaborMinutes !== undefined ? { estimatedLaborMinutes } : {}),
    }
    try {
      await createWorkOrder(body)
      createOpen.value = false
      notifySuccess('维护工单已创建')
    } catch (error) {
      notifyOperationFailure('维护工单创建失败', error, '维护工单创建失败，请稍后重试。')
    }
  }

  const expectedRestoreOpen = shallowRef(false)
  const expectedRestoreTarget = shallowRef<WorkOrderRow>()
  const expectedRestoreAtLocal = shallowRef('')
  const expectedRestoreScopes = shallowRef<BusinessConsoleAuthorizedWorkScope[]>([])
  const expectedRestoreScopeKey = shallowRef('')
  const expectedRestoreScopeOptions = computed(() =>
    expectedRestoreScopes.value.map((scope) => ({
      value: formatWorkScopeKey(scope.kind!, scope.id!),
      label: scope.displayName ?? scope.id!,
    })),
  )
  const expectedRestoreScope = computed(() =>
    expectedRestoreScopes.value.find(
      (scope) => formatWorkScopeKey(scope.kind!, scope.id!) === expectedRestoreScopeKey.value,
    ),
  )
  function localDateTime(value?: string | null) {
    if (!value) return ''
    const date = new Date(value)
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
    return local.toISOString().slice(0, 19)
  }
  function canUpdateExpectedRestore(row: WorkOrderRow) {
    return (
      canManageWorkOrders.value &&
      Boolean(row.workOrderId) &&
      ['open', 'accepted', 'inprogress', 'paused', 'waitingforparts'].includes(
        (row.status ?? '').toLowerCase(),
      ) &&
      (!row.assignedTechnicianUserId || row.assignedTechnicianUserId === currentUserId.value)
    )
  }
  async function openExpectedRestore(row: WorkOrderRow) {
    expectedRestoreTarget.value = row
    expectedRestoreAtLocal.value = localDateTime(row.expectedRestoreAtUtc)
    expectedRestoreScopes.value = []
    expectedRestoreScopeKey.value = ''
    expectedRestoreOpen.value = true
    try {
      const scopes = (await readExpectedRestoreScopes()).filter(
        (scope) => scope.kind === 'organization' || scope.kind === 'self' || scope.kind === 'team',
      )
      expectedRestoreScopes.value = scopes
      const selected =
        scopes.find(
          (scope) => scope.kind === 'organization' && scope.id === filters.organizationId,
        ) ??
        scopes.find((scope) => scope.kind === 'team' && scope.id === row.assignedTeamId) ??
        scopes.find((scope) => scope.kind === 'self' && scope.id === currentUserId.value) ??
        scopes[0]
      if (selected) expectedRestoreScopeKey.value = formatWorkScopeKey(selected.kind!, selected.id!)
      else notifyError('当前没有可用的维修作业范围，请联系管理员。')
    } catch (error) {
      notifyOperationFailure(
        '维修作业范围读取失败',
        error,
        '维修作业范围读取失败，请关闭抽屉后重试。',
      )
    }
  }
  async function submitExpectedRestore() {
    const target = expectedRestoreTarget.value!
    try {
      const detail = await updateExpectedRestore(
        target,
        expectedRestoreAtLocal.value ? new Date(expectedRestoreAtLocal.value).toISOString() : null,
        expectedRestoreScope.value!,
      )
      expectedRestoreTarget.value = detail
      expectedRestoreAtLocal.value = localDateTime(detail.expectedRestoreAtUtc)
      expectedRestoreOpen.value = false
      notifySuccess('预计恢复时间已保存')
    } catch (error) {
      notifyOperationFailure('预计恢复时间保存失败', error, '预计恢复时间保存失败，请刷新后重试。')
    }
  }

  function openComplete(row: WorkOrderRow) {
    completeTarget.value = row
    completeForm.result = 'repaired'
    completeForm.downtimeReasonCode = ''
    completeReasonKeyword.value = ''
    completeForm.downtimeMinutes = '30'
    completeForm.actualLaborMinutes = ''
    completeForm.externalServiceCostAmount = ''
    completeForm.costCurrencyCode = 'CNY'
    // 实际技师默认沿用建单指派技师，完工时可改选真正执行人。
    completeForm.actualTechnicianUserId = row.assignedTechnicianUserId ?? ''
    spareRows.splice(0, spareRows.length, createSpareRow())
    sparePartCostOverride.value = ''
    completeError.value = ''
    completeOpen.value = true
  }
  function canComplete(row: WorkOrderRow) {
    return statusActionGate({
      domain: 'maintenance-work-order',
      action: 'complete',
      facts: { status: row.status },
    }).executable
  }
  function addSpareRow() {
    spareRows.push(createSpareRow())
  }
  function removeSpareRow(rowId: number) {
    if (spareRows.length === 1) {
      Object.assign(spareRows[0], createSpareRow())
      return
    }
    const index = spareRows.findIndex((row) => row.id === rowId)
    if (index >= 0) spareRows.splice(index, 1)
  }
  function spareOutOfBounds(row: SparePartRow) {
    return Boolean(row.skuCode.trim()) && !(Number(row.quantity) > 0)
  }

  async function submitComplete() {
    const target = completeTarget.value
    if (!target?.workOrderId) return
    if (!completeForm.downtimeReasonCode) {
      completeError.value = completeReasonMessage.value || '请选择停机原因。'
      return
    }
    const minutes = Number(completeForm.downtimeMinutes)
    if (!(minutes >= 0)) {
      completeError.value = '停机时长需为非负数。'
      return
    }
    const actualLaborMinutes = optionalNonNegativeInt(completeForm.actualLaborMinutes)
    if (actualLaborMinutes === false) {
      completeError.value = '实际工时需为非负整数。'
      return
    }
    const externalServiceCostAmount = optionalNonNegativeNumber(
      completeForm.externalServiceCostAmount,
    )
    if (externalServiceCostAmount === false) {
      completeError.value = '外委费用需为非负数。'
      return
    }
    // 备件成本人工覆盖须为合法非负数——否则负值会发出负成本、非法值会静默丢字段。
    const overrideCost = optionalNonNegativeNumber(sparePartCostOverride.value)
    if (overrideCost === false) {
      completeError.value = '备件成本汇总需为非负数。'
      return
    }
    const sparePartCostAmount =
      overrideCost !== undefined
        ? overrideCost
        : autoSparePartCost.value > 0
          ? round2(autoSparePartCost.value)
          : undefined
    // 完成需登记至少一条更换备件（领料扣减）；后端以此核销维护成本。
    const filledSpares = spareRows.filter((row) => row.skuCode.trim())
    if (filledSpares.length === 0) {
      completeError.value = '请登记至少一条更换备件（物料 + 数量）。'
      return
    }
    if (filledSpares.some(spareOutOfBounds)) {
      completeError.value = '备件数量需为正数。'
      return
    }
    // 单位来自备件的物料主档（选完物料自动带出），主档没维护基本单位时要人工选，不替它兜一个通用单位：
    // 按 kg / l 计量的备件用错单位，后端单位换算会直接失败。
    if (filledSpares.some((row) => !row.uomCode.trim())) {
      completeError.value = '备件单位缺失，请为每条备件选择计量单位。'
      return
    }
    // 备件从设备所在工厂的已登记库位领出，库存按工厂 + 库位扣减（#3902）。
    const siteCode = completeSiteCode.value
    if (!siteCode) {
      completeError.value =
        '该设备未登记所属工厂，无法确定备件从哪里领出，请先在设备台账维护所属工厂。'
      return
    }
    if (filledSpares.some((row) => !row.locationCode.trim())) {
      completeError.value = '请为每条备件选择领出库位。'
      return
    }
    const spareParts: BusinessConsoleMaintenanceSparePartInput[] = filledSpares.map((row) => ({
      skuCode: row.skuCode.trim(),
      quantity: Number(row.quantity),
      uomCode: row.uomCode.trim(),
      siteCode,
      locationCode: row.locationCode.trim(),
    }))
    try {
      await completeWorkOrder(target.workOrderId, {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        result: completeForm.result,
        downtimeReasonCode: completeForm.downtimeReasonCode,
        downtimeMinutes: minutes,
        spareParts,
        ...(actualLaborMinutes !== undefined ? { actualLaborMinutes } : {}),
        ...(sparePartCostAmount !== undefined ? { sparePartCostAmount } : {}),
        ...(externalServiceCostAmount !== undefined ? { externalServiceCostAmount } : {}),
        costCurrencyCode: completeForm.costCurrencyCode,
        // #897：完工登记实际执行技师（userId）；空则不带该字段。
        actualTechnicianUserId: completeForm.actualTechnicianUserId.trim() || undefined,
      })
      completeOpen.value = false
      notifySuccess(`维护工单 ${workOrderNo(target)} 已完成`)
    } catch (error) {
      if (
        await recoverLifecycleAction(error, {
          reset: () => {
            completeOpen.value = false
            completeTarget.value = undefined
            completeError.value = ''
          },
          refresh: refreshWorkOrders,
          notify: (message) => notifyError(message),
        })
      ) {
        return
      }
      notifyOperationFailure('维护工单完成失败', error, '维护工单完成失败，请稍后重试。')
    }
  }

  // 非负整数：空 → undefined（不带该字段）；非法 → false；合法 → number。
  // number 输入框经 v-model 可能回传 number（非 string），故统一 String() 归一再判。
  function optionalNonNegativeInt(value: string | number): number | undefined | false {
    const trimmed = String(value ?? '').trim()
    if (!trimmed) return undefined
    const n = Number(trimmed)
    return Number.isInteger(n) && n >= 0 ? n : false
  }
  function optionalNonNegativeNumber(value: string | number): number | undefined | false {
    const trimmed = String(value ?? '').trim()
    if (!trimmed) return undefined
    const n = Number(trimmed)
    return Number.isFinite(n) && n >= 0 ? round2(n) : false
  }

  function formatOpenedAt(value?: string | null) {
    return value ? formatLocalDateTime(value) : '—'
  }

  function formatDateTime(value?: string | null) {
    if (!value) return '—'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
  }
  function formatDate(value?: string | null) {
    if (!value) return '—'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString()
  }
  watch(
    () => route.query,
    (query) => {
      if (queryPrefilled.value || !canManageWorkOrders.value) return
      const deviceAssetId = typeof query.deviceAssetId === 'string' ? query.deviceAssetId : ''
      const sourceAlarmId = typeof query.sourceAlarmId === 'string' ? query.sourceAlarmId : ''
      if (!deviceAssetId && !sourceAlarmId) return
      queryPrefilled.value = true
      openCreate({ deviceAssetId, sourceAlarmId })
    },
    { immediate: true },
  )

  return {
    addSpareRow,
    alarmOptions,
    alarmsPending,
    applySpareRowSku,
    autoSparePartCost,
    canComplete,
    canManageWorkOrders,
    canUpdateExpectedRestore,
    columns,
    completeCarriedItems,
    completeErrorMessage,
    completeForm,
    completeOpen,
    completeReasonKeyword,
    completeReasonMessage,
    completeReasonOptions,
    completeReasonState,
    completeReasonsTotal,
    completeSiteCode,
    completeTarget,
    completeWorkOrderPending,
    createCarried,
    createCarriedItems,
    createDeviceModel,
    createErrorMessage,
    createForm,
    createOpen,
    createWorkOrderPending,
    deviceLabel,
    directoryMessage,
    directoryState,
    downtimeReasonOptions,
    downtimeReasons,
    expectedRestoreAtLocal,
    expectedRestoreOpen,
    expectedRestorePending,
    expectedRestoreScope,
    expectedRestoreScopeKey,
    expectedRestoreScopeOptions,
    expectedRestoreScopes,
    expectedRestoreTarget,
    highPriorityPending,
    knownReasonCodes,
    listErrorMessage,
    onReasonSaved,
    openComplete,
    openCreate,
    openExpectedRestore,
    openReasonCreate,
    page,
    pageSize,
    pendingCount,
    priorityLabel,
    priorityOptions,
    reasonFormOpen,
    reasonFormSession,
    reasonKeyword,
    reasonsTotal,
    refreshWorkOrders,
    removeSpareRow,
    resultOptions,
    round2,
    router,
    rowKey,
    sparePartCostDisplay,
    spareRows,
    submitComplete,
    submitCreate,
    submitExpectedRestore,
    uomOptions,
    uomsPending,
    warrantyStatusLabel,
    workOrderNo,
    workOrders,
    workOrdersPending,
    workOrdersTotal,
    workerOptions,
    workersPending,
  }
}
