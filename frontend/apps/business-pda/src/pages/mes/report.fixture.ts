import { clearPendingBusinessIntent } from '@nerv-iip/business-core'
import { flushPromises, mount } from '@vue/test-utils'
import { vi } from 'vitest'
import { computed, reactive, ref } from 'vue'
import ReportPage from './report.vue'

const route = reactive<{ query: Record<string, unknown> }>({ query: {} })
async function applyNavigation(to: string | { query?: Record<string, unknown> }) {
  if (typeof to === 'string') {
    if (to === '/mes/report') route.query = {}
    return
  }
  route.query = { ...to.query }
}
const push = vi.fn(applyNavigation)
const replace = vi.fn(applyNavigation)
vi.mock('vue-router', () => ({
  useRouter: () => ({ push, replace }),
  useRoute: () => route,
}))

// --- composable mock: multiple work orders + operation tasks + recordReport spy ---
type ReportEnvelope = {
  success: boolean
  message?: string
  data?: {
    productionReportId?: string
    reportNo?: string
    serialNumbers?: string[]
    printBatchId?: string
    printStatus?: string
    printingPreparationPending?: boolean
  } | null
}
const successfulReceipt: ReportEnvelope = {
  success: true,
  data: {
    productionReportId: '019f-report-default',
    reportNo: 'RPT-DEFAULT',
  },
}
const recordReport = vi.fn(
  async (_input: Record<string, unknown>): Promise<ReportEnvelope> => successfulReceipt,
)
const confirmReport = vi.fn(
  async (input: {
    reportNo: string
    productionReportId: string
    workOrderId: string
    operationTaskId: string
  }) => ({ ...input }),
)
const refreshWorkOrders = vi.fn(async () => {})
const refreshTasks = vi.fn(async () => {})
const refreshExactTask = vi.fn(async () => {})
const cancelPendingTasks = vi.fn()
const workOrdersErrorRef = ref<unknown>(null)
const tasksErrorRef = ref<unknown>(null)
const workOrdersPendingRef = ref(false)
const tasksPendingRef = ref(false)
const reportScopeMessageRef = ref('')
const reportScopePendingRef = ref(false)
const reportScopeReadyRef = ref(true)
const contextGenerationRef = ref(1)
const reportableTasksPendingRef = ref(false)
const reportableTasksErrorRef = ref<unknown>(null)
const reportableTasksReadyRef = ref(true)
const reportScopeRef = ref({ kind: 'organization', id: 'org-001' })
const reportContextRef = ref({
  principalId: 'principal-1',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  scopeKind: 'organization',
  scopeId: 'org-001',
  generation: 1,
})
let operationTaskDiscoveryCalls = 0

const workOrderFilters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})
const taskFilters = reactive({
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
})

const defaultWorkOrders = [
  {
    workOrderId: 'WO-2026-0001',
    workOrderNo: 'WONO-A',
    skuId: 'SKU-A',
    quantity: 100,
    status: 'Released',
  },
  {
    workOrderId: 'WO-2026-0002',
    workOrderNo: 'WONO-B',
    skuId: 'SKU-B',
    quantity: 50,
    status: 'Released',
  },
]
const workOrdersRef = ref<Array<Record<string, unknown>>>(defaultWorkOrders)
const telemetryCandidatesRef = ref<Array<Record<string, unknown>>>([])
const telemetryTargetTasksRef = ref<Array<Record<string, unknown>>>([])
const telemetryPromote = vi.fn()
const telemetryDismiss = vi.fn()

const defaultOperationTasks = [
  {
    operationTaskId: 'OP-1',
    operationTaskNo: 'OPNO-1',
    workOrderId: 'WO-2026-0001',
    workOrderNo: 'WONO-A',
    status: 'InProgress',
    allowedActions: ['report'],
    operationSequence: 10,
    workCenterId: 'WC-A',
  },
  {
    operationTaskId: 'OP-2',
    operationTaskNo: 'OPNO-2',
    workOrderId: 'WO-2026-0001',
    workOrderNo: 'WONO-A',
    status: 'Queued',
    allowedActions: ['report'],
    operationSequence: 20,
    workCenterId: 'WC-B',
  },
  {
    operationTaskId: 'OP-3',
    operationTaskNo: 'OPNO-3',
    workOrderId: 'WO-2026-0002',
    workOrderNo: 'WONO-B',
    status: 'Ready',
    allowedActions: ['report'],
    operationSequence: 10,
    workCenterId: 'WC-C',
  },
]
const operationTasksRef = ref<Array<Record<string, unknown>>>(defaultOperationTasks)
const reportableTasksOverrideRef = ref<Array<Record<string, unknown>> | null>(null)
const workOrderDetailRef = ref<Record<string, unknown> | null>({
  ...defaultWorkOrders[0],
  operationTasks: defaultOperationTasks.filter(
    (task) => task.workOrderId === defaultWorkOrders[0].workOrderId,
  ),
})
const workOrderDetailPendingRef = ref(false)
const workOrderDetailErrorRef = ref<unknown>(null)
const workOrderDetailHasSuccessfulResponseRef = ref(true)
const workOrderDetailHasFailedResponseRef = ref(false)
const refreshWorkOrderDetail = vi.fn(async () => {})
const exactTaskRef = ref<Record<string, unknown> | null | undefined>(undefined)
const exactTaskPendingRef = ref(false)
const exactTaskErrorRef = ref<unknown>(null)
const exactTaskScopeReadyRef = ref(true)
const exactTaskScopeMessageRef = ref('')
const materialLotsRef = ref<Array<Record<string, unknown>>>([])
const materialsReadPermissionRef = ref(true)
const materialLotsPendingRef = ref(false)
const materialLotsErrorRef = ref<unknown>(null)
const scrapReasonCodesRef = ref<Array<Record<string, unknown>>>([
  {
    reasonCode: 'SCRAP-SURFACE',
    reasonName: '表面缺陷',
    groupName: '外观',
    severity: 'major',
    defaultDisposition: 'scrap',
    enabled: true,
    snapshotVersion: 'v1',
  },
])
const qualityInspectionRecordsReadPermissionRef = ref(true)
const scrapReasonCodesPendingRef = ref(false)
const scrapReasonCodesErrorRef = ref<unknown>(null)
const workScopeOptionsRef = ref([
  { label: '精加工一线（工作中心）', value: 'work-center:WC-A' },
  { label: '精加工二线（工作中心）', value: 'work-center:WC-B' },
])
const workScopeSelectionRef = ref<string | undefined>('work-center:WC-A')
const workOrderListScopeReadyRef = ref(true)
const workOrderListScopeUnavailableRef = ref(false)
const workOrderListScopeMessageRef = ref('')

vi.mock('@/composables/mes', () => ({
  useMesWorkOrders: () => ({
    filters: workOrderFilters,
    workOrders: computed(() => workOrdersRef.value),
    total: computed(() => workOrdersRef.value.length),
    pending: workOrdersPendingRef,
    error: workOrdersErrorRef,
    refresh: refreshWorkOrders,
    hasSuccessfulResponse: computed(() => !workOrdersPendingRef.value && !workOrdersErrorRef.value),
    hasFailedResponse: computed(() => false),
    workOrderReadScope: ref({
      kind: 'work-center',
      id: 'WC-A',
      displayName: '精加工一线',
    }),
    workOrderReadScopeMessage: workOrderListScopeMessageRef,
    workOrderReadScopePending: ref(false),
    workOrderReadScopeReady: workOrderListScopeReadyRef,
    workOrderReadScopeUnavailable: workOrderListScopeUnavailableRef,
  }),
  useMesOperationTasks: () => {
    operationTaskDiscoveryCalls += 1
    return {
      filters: taskFilters,
      operationTasks: computed(() => operationTasksRef.value),
      total: computed(() => operationTasksRef.value.length),
      pending: tasksPendingRef,
      error: tasksErrorRef,
      refresh: refreshTasks,
      cancelPendingTasks,
      startTask: vi.fn(),
      pauseTask: vi.fn(),
      resumeTask: vi.fn(),
      completeTask: vi.fn(),
      actionPending: ref(false),
    }
  },
  useMesWorkOrderDetail: (workOrderId: { value: string }) => ({
    workOrder: computed(() => {
      if (workOrderDetailRef.value?.workOrderId === workOrderId.value) {
        return workOrderDetailRef.value
      }
      const workOrder = workOrdersRef.value.find(
        (candidate) => candidate.workOrderId === workOrderId.value,
      )
      return workOrder
        ? {
            ...workOrder,
            operationTasks: operationTasksRef.value.filter(
              (task) => task.workOrderId === workOrderId.value,
            ),
          }
        : null
    }),
    pending: workOrderDetailPendingRef,
    error: workOrderDetailErrorRef,
    refresh: refreshWorkOrderDetail,
    hasSuccessfulResponse: workOrderDetailHasSuccessfulResponseRef,
    hasFailedResponse: workOrderDetailHasFailedResponseRef,
    workOrderReadScope: ref({
      kind: 'work-center',
      id: 'WC-A',
      displayName: '精加工一线',
    }),
    workOrderReadScopeMessage: ref(''),
  }),
  useMesExactOperationTask: (
    workOrderId: { value: string },
    operationTaskId: { value: string },
    detail: { value?: Record<string, unknown> | null },
  ) => ({
    task: computed(() => {
      if (exactTaskRef.value !== undefined) return exactTaskRef.value
      if (workOrderDetailPendingRef.value || detail.value?.workOrderId !== workOrderId.value) {
        return null
      }
      return (
        operationTasksRef.value.find(
          (task) =>
            task.workOrderId === workOrderId.value &&
            task.operationTaskId === operationTaskId.value,
        ) ?? null
      )
    }),
    pending: exactTaskPendingRef,
    error: exactTaskErrorRef,
    refresh: refreshExactTask,
    reportingReadScopeReady: exactTaskScopeReadyRef,
    reportingReadScopeMessage: exactTaskScopeMessageRef,
  }),
  useMesProductionReports: () => ({
    filters: reactive({}),
    productionReports: computed(() => []),
    total: computed(() => 0),
    pending: ref(false),
    error: ref(null),
    refresh: vi.fn(),
    recordReport,
    confirmReport,
    reportScopeMessage: reportScopeMessageRef,
    reportScopePending: reportScopePendingRef,
    reportScopeReady: reportScopeReadyRef,
    reportScope: reportScopeRef,
    reportContext: reportContextRef,
    contextGeneration: contextGenerationRef,
    reportableTasks: computed(() => reportableTasksOverrideRef.value ?? operationTasksRef.value),
    reportableTasksPending: reportableTasksPendingRef,
    reportableTasksError: reportableTasksErrorRef,
    reportableTasksReady: reportableTasksReadyRef,
    refreshReportableTasks: vi.fn(async () => undefined),
  }),
  useMesProductionMaterialLots: () => ({
    materialsReadPermission: materialsReadPermissionRef,
    materialLotsPending: materialLotsPendingRef,
    materialLotsError: materialLotsErrorRef,
    availableMaterialLots: materialLotsRef,
    refreshMaterialLots: vi.fn(async () => undefined),
  }),
  useMesScrapReasonCodes: () => ({
    qualityInspectionRecordsReadPermission: qualityInspectionRecordsReadPermissionRef,
    scrapReasonCodes: scrapReasonCodesRef,
    scrapReasonCodesPending: scrapReasonCodesPendingRef,
    scrapReasonCodesError: scrapReasonCodesErrorRef,
    refreshScrapReasonCodes: vi.fn(async () => undefined),
  }),
  useMesTelemetryProductionReportCandidates: () => ({
    candidates: computed(() => telemetryCandidatesRef.value),
    total: computed(() => telemetryCandidatesRef.value.length),
    pending: ref(false),
    promote: telemetryPromote,
    dismiss: telemetryDismiss,
  }),
  useMesTelemetryCandidateTargetTasks: () => ({
    tasks: computed(() => telemetryTargetTasksRef.value),
    pending: ref(false),
    error: ref(null),
    refresh: vi.fn(),
  }),
  // 作业范围选择入口自带一个独立实例（#1297）：页面挂载时必须能解析到它。
  useMesWorkScopeSelection: () => ({
    scopeOptions: computed(() => workScopeOptionsRef.value),
    scopeSelectionValue: workScopeSelectionRef,
  }),
}))

const deviceNames: Record<string, string> = {
  'DEV-CNC-01': '数控车床一号（EQ00001）',
  'DEV-CNC-99': '数控车床九号（EQ00099）',
}
vi.mock('@/composables/equipment/useBusinessDeviceDirectory', () => ({
  useDeviceAssetNames: () => ({
    resolveDeviceName: (deviceAssetId?: string | null) =>
      deviceAssetId ? deviceNames[deviceAssetId] : undefined,
  }),
}))

const serialRequired = ref(false)
const serialValid = ref(true)
const serialTemplateId = ref('')
vi.mock('@/composables/mes/useProductionReportSerials', () => ({
  useProductionReportSerials: () => ({
    required: serialRequired,
    valid: serialValid,
    templateId: serialTemplateId,
    pending: ref(false),
    message: ref(''),
    templates: ref([{ templateId: 'tpl-1', templateName: '成品标签', status: 'active' }]),
    pendingCount: ref(2),
    refresh: vi.fn(),
  }),
}))

async function selectWorkOrder(wrapper: ReturnType<typeof mount>, index = 0) {
  const rows = wrapper.findAll('[data-row]')
  await rows[index].trigger('click')
  await flushPromises()
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

async function openTelemetryCandidate(candidate: Record<string, unknown>) {
  telemetryCandidatesRef.value = [
    { goodQuantity: 12, status: 'pending-confirmation', ...candidate },
  ]
  const wrapper = mount(ReportPage, { attachTo: document.body })
  await flushPromises()
  await wrapper
    .findAll('button')
    .find((button) => button.text().includes('12 件'))!
    .trigger('click')
  await flushPromises()
  return wrapper
}
const deviceTasks = [
  { operationTaskId: 'OP-1', workOrderId: 'WO-2026-0001', operationSequence: 10 },
  { operationTaskId: 'OP-3', workOrderId: 'WO-2026-0002', operationSequence: 10 },
]

const resetFixture = () => {
  for (const entry of JSON.parse(
    sessionStorage.getItem('nerv-iip.pending-business-intents.v1') ?? '[]',
  ))
    clearPendingBusinessIntent(entry)
  sessionStorage.clear()
  telemetryCandidatesRef.value = []
  telemetryTargetTasksRef.value = []
  telemetryPromote.mockReset().mockResolvedValue('PRPT-20260927-000001')
  telemetryDismiss.mockReset().mockResolvedValue(undefined)
  serialRequired.value = false
  serialValid.value = true
  serialTemplateId.value = ''
  recordReport.mockReset()
  recordReport.mockResolvedValue(successfulReceipt)
  confirmReport.mockClear()
  confirmReport.mockImplementation(async (input) => ({ ...input }))
  push.mockClear()
  replace.mockClear()
  refreshWorkOrders.mockClear()
  refreshTasks.mockClear()
  refreshWorkOrderDetail.mockClear()
  refreshExactTask.mockClear()
  cancelPendingTasks.mockClear()
  workOrdersErrorRef.value = null
  workOrderListScopeReadyRef.value = true
  workOrderListScopeUnavailableRef.value = false
  workOrderListScopeMessageRef.value = ''
  tasksErrorRef.value = null
  workOrdersPendingRef.value = false
  tasksPendingRef.value = false
  reportScopeMessageRef.value = ''
  reportScopePendingRef.value = false
  reportScopeReadyRef.value = true
  contextGenerationRef.value = 1
  reportableTasksPendingRef.value = false
  reportableTasksErrorRef.value = null
  reportableTasksReadyRef.value = true
  reportScopeRef.value = { kind: 'organization', id: 'org-001' }
  reportContextRef.value = {
    principalId: 'principal-1',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    scopeKind: 'organization',
    scopeId: 'org-001',
    generation: 1,
  }
  workOrdersRef.value = defaultWorkOrders
  operationTasksRef.value = defaultOperationTasks
  reportableTasksOverrideRef.value = null
  workOrderDetailRef.value = {
    ...defaultWorkOrders[0],
    operationTasks: defaultOperationTasks.filter(
      (task) => task.workOrderId === defaultWorkOrders[0].workOrderId,
    ),
  }
  workOrderDetailPendingRef.value = false
  workOrderDetailErrorRef.value = null
  workOrderDetailHasSuccessfulResponseRef.value = true
  workOrderDetailHasFailedResponseRef.value = false
  refreshWorkOrderDetail.mockClear()
  exactTaskRef.value = undefined
  exactTaskPendingRef.value = false
  exactTaskErrorRef.value = null
  exactTaskScopeReadyRef.value = true
  exactTaskScopeMessageRef.value = ''
  materialLotsRef.value = []
  materialsReadPermissionRef.value = true
  materialLotsPendingRef.value = false
  materialLotsErrorRef.value = null
  scrapReasonCodesRef.value = [
    {
      reasonCode: 'SCRAP-SURFACE',
      reasonName: '表面缺陷',
      groupName: '外观',
      severity: 'major',
      defaultDisposition: 'scrap',
      enabled: true,
      snapshotVersion: 'v1',
    },
  ]
  qualityInspectionRecordsReadPermissionRef.value = true
  scrapReasonCodesPendingRef.value = false
  scrapReasonCodesErrorRef.value = null
  operationTaskDiscoveryCalls = 0
  workOrderFilters.keyword = undefined
  workOrderFilters.workOrderId = undefined
  taskFilters.workOrderId = undefined
  route.query = {}
}

export {
  route,
  successfulReceipt,
  recordReport,
  confirmReport,
  contextGenerationRef,
  reportContextRef,
  serialRequired,
  serialTemplateId,
  resetFixture,
  replace,
  refreshWorkOrders,
  refreshExactTask,
  workOrdersErrorRef,
  tasksErrorRef,
  tasksPendingRef,
  reportScopeMessageRef,
  reportScopeReadyRef,
  reportableTasksPendingRef,
  reportableTasksErrorRef,
  reportableTasksReadyRef,
  reportScopeRef,
  operationTaskDiscoveryCalls,
  workOrderFilters,
  defaultWorkOrders,
  workOrdersRef,
  defaultOperationTasks,
  operationTasksRef,
  reportableTasksOverrideRef,
  workOrderDetailRef,
  workOrderDetailPendingRef,
  workOrderDetailErrorRef,
  workOrderDetailHasSuccessfulResponseRef,
  workOrderDetailHasFailedResponseRef,
  refreshWorkOrderDetail,
  exactTaskRef,
  exactTaskScopeReadyRef,
  exactTaskScopeMessageRef,
  materialLotsRef,
  workOrderListScopeReadyRef,
  workOrderListScopeUnavailableRef,
  workOrderListScopeMessageRef,
  selectWorkOrder,
  type ReportEnvelope,
  deferred,
  telemetryCandidatesRef,
  telemetryTargetTasksRef,
  telemetryPromote,
  telemetryDismiss,
  openTelemetryCandidate,
  deviceTasks,
}
