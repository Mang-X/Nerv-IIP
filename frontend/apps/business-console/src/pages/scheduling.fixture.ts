import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, vi } from 'vitest'
import type {
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleSchedulingMaterialShortageSummary,
  SchedulingCandidateSelection,
  BusinessConsoleSchedulePlan,
  SchedulingWorkingDraft,
} from '@nerv-iip/api-client'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 让断言不依赖真实名录查询。挂载仍装一个新 Pinia（见各 mount 的 plugins）：
// SchedulingPlanGantt 里未被 mock 的 useMesDisplayNames() 需要 active Pinia。
vi.mock('@/composables/master-data/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/master-data/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/master-data/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const routeStub = vi.hoisted(() => ({ query: {} as Record<string, string> }))
vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ query: routeStub.query }),
  useRouter: () => ({ replace: vi.fn() }),
}))

vi.mock('@/composables/urgency/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() }, refresh: vi.fn() }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: {
    props: ['orderReference', 'mode', 'urgency'],
    template:
      '<span data-testid="order-urgency" :data-ref="orderReference" :data-mode="mode">未计算</span>',
  },
}))
const authState = vi.hoisted(() => ({
  permissionCodes: [] as string[],
  principalId: undefined as string | undefined,
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: authState }),
}))
const stub = vi.hoisted(() => ({
  releasePlan: vi
    .fn()
    .mockResolvedValue({ success: true, data: { planId: 'plan-001', status: 'released' } }),
  revokePlan: vi
    .fn()
    .mockResolvedValue({ success: true, data: { planId: 'plan-released', status: 'revoked' } }),
  upsertOperationOverride: vi.fn().mockResolvedValue({ success: true, data: {} }),
  generatePlan: vi.fn(),
  revisePlan: vi.fn(),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

const localCandidateCallback = vi.hoisted(() => ({
  onSelected: undefined as ((selection: SchedulingCandidateSelection) => Promise<void>) | undefined,
  readSelectedPlan: vi.fn(),
  preview: vi.fn(),
  select: vi.fn(),
}))
vi.mock('@/composables/scheduling/useSchedulingCandidates', async (importOriginal) => {
  const actual =
    await importOriginal<typeof import('@/composables/scheduling/useSchedulingCandidates')>()
  return {
    useSchedulingCandidates: (options: Parameters<typeof actual.useSchedulingCandidates>[0]) => {
      localCandidateCallback.onSelected = options.onSelected
      return actual.useSchedulingCandidates(options)
    },
  }
})

// #1288 作业范围选择入口是独立组件（自带 work-context 查询），页面测试不铺网络桩，整件打桩。
vi.mock('@/components/mes/MesWorkScopeSelect.vue', () => ({
  default: {
    name: 'MesWorkScopeSelect',
    template: '<div data-testid="mes-work-scope-select" />',
  },
}))

const downtimeImpact = shallowRef<import('@nerv-iip/api-client').SchedulingDowntimeImpactResponse>()
const downtimeNow = shallowRef(new Date('2026-10-09T08:00:00.000Z'))
let downtimeBaseline: () => string | undefined
const planTimeContextError = shallowRef<unknown>()
const factoryTimezone = shallowRef('Asia/Shanghai')
const assemblyTimezone = shallowRef<string>()
vi.mock('@/composables/scheduling/useSchedulingTimeContext', () => ({
  useSchedulingTimeContext: (planId: () => string | undefined) => ({
    items: computed(() =>
      ['WC-CNC', 'WC-ASSEMBLY'].map((workCenterCode) => ({
        workCenterCode,
        plantCode:
          workCenterCode === 'WC-ASSEMBLY' && assemblyTimezone.value ? 'NB-ASSEMBLY' : 'NB',
        timezone:
          workCenterCode === 'WC-ASSEMBLY'
            ? (assemblyTimezone.value ?? factoryTimezone.value)
            : factoryTimezone.value,
      })),
    ),
    error: computed(() => (planId() ? planTimeContextError.value : undefined)),
  }),
}))
vi.mock('@/composables/scheduling/useSchedulingDowntime', () => ({
  useSchedulingDowntime: (planId: () => string | undefined) => {
    downtimeBaseline = planId
    return { impact: downtimeImpact, now: downtimeNow, error: shallowRef() }
  },
}))
const savedDrafts = shallowRef<SchedulingWorkingDraft[]>([])
const savedPlans = new Map<string, BusinessConsoleSchedulePlan>()
let pagePersistence: ReturnType<
  typeof import('@/composables/scheduling/useSchedulingDraftPersistence').useSchedulingDraftPersistence
>
vi.mock('@/composables/scheduling/useSchedulingDraftPersistence', async (original) => {
  const actual =
    await original<typeof import('@/composables/scheduling/useSchedulingDraftPersistence')>()
  return {
    useSchedulingDraftPersistence: (
      ...args: Parameters<typeof actual.useSchedulingDraftPersistence>
    ) => {
      pagePersistence = actual.useSchedulingDraftPersistence(...args)
      return pagePersistence
    },
  }
})
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleSchedulingWorkingDrafts: async () => ({
    data: { success: true, data: savedDrafts.value },
  }),
  getBusinessConsoleSchedulingPlan: localCandidateCallback.readSelectedPlan,
  previewBusinessConsoleSchedulingCandidates: localCandidateCallback.preview,
  selectBusinessConsoleSchedulingCandidate: localCandidateCallback.select,
}))
const associatedOrders = shallowRef<BusinessConsoleMesWorkOrderItem[]>([])
const associatedError = shallowRef<unknown>()
let associatedOrderIds: () => (string | undefined)[]
const candidatesEmpty = shallowRef(false)
const generatedPlan = shallowRef()
const generatePending = shallowRef(false)
const firstPlanJob = shallowRef()
const generationError = shallowRef()
const capacityCandidates = shallowRef<BusinessConsoleMesWorkOrderItem[]>()
const releasePending = shallowRef(false)
const insertionJob =
  shallowRef<import('@nerv-iip/api-client').BusinessConsoleSchedulingInsertionPreviewJob>()
vi.mock('@/composables/scheduling/useSchedulingRushInsertion', () => ({
  useSchedulingRushInsertion: () => ({
    task: {
      job: insertionJob,
      result: computed(() => insertionJob.value?.result),
      pending: shallowRef(false),
      error: shallowRef(),
    },
    reset: () => {
      insertionJob.value = undefined
    },
    message: shallowRef(''),
    retry: vi.fn(),
    saveOrder: vi.fn(),
  }),
}))
vi.mock('@/composables/mes', () => ({
  useMesWorkOrderFacts: (ids: () => (string | undefined)[]) => {
    associatedOrderIds = ids
    return { workOrders: associatedOrders, error: associatedError }
  },
}))

vi.mock('@/composables/scheduling/useSchedulingWorkbench', () => ({
  useSchedulingWorkbench: () => ({
    // 甘特工序详情用它把物料/数量/交期 join 到工序上（工单级事实，见 SchedulingPlanGantt）。
    candidates: computed(() =>
      candidatesEmpty.value
        ? []
        : (capacityCandidates.value ?? [
            {
              workOrderId: 'WO-20260701-001',
              skuCode: 'SKU-PISTON-01',
              quantity: 120,
              dueUtc: '2026-07-06T00:00:00Z',
              status: 'released',
              productionVersionId: 'pv-001',
            },
          ]),
    ),
    priorityScopeReady: shallowRef(true),
    saveOrderPriority: vi.fn(),
    candidatesError: shallowRef(undefined),
    candidatesPending: shallowRef(false),
    // #1288 待排池 scope gate 事实：本文件不测未就绪分支，按就绪打桩。
    candidatesScopeMessage: computed(() => ''),
    candidatesScopeReady: computed(() => true),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    generatePending,
    firstPlanJob,
    generatedPlan: generatedPlan,
    generationError,
    generatePlan: async (body: unknown) => {
      generatePending.value = true
      try {
        generatedPlan.value = await stub.generatePlan(body)
      } finally {
        generatePending.value = false
      }
    },
    refreshCandidates: vi.fn(),
    revisionPending: shallowRef(false),
    revisePlan: stub.revisePlan,
    // 草案工作区要有可选工单才能生成首版方案（持久化 override 用例的前置条件）。
    schedulableCandidates: computed(
      () =>
        capacityCandidates.value ?? [
          {
            workOrderId: 'WO-20260701-001',
            productionVersionId: 'PV-001',
            skuCode: 'SKU-PISTON-ROD',
            status: 'released',
            priority: 100,
          },
        ],
    ),
  }),
}))

const detailSelection = reactive({ planId: '' })
const historyPage = shallowRef(1)
const historyEmpty = shallowRef(false)
const planOneInvalidated = shallowRef(false)
const historyFilters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  status: undefined as string | undefined,
  releasedOn: '',
  isInvalidated: undefined as boolean | undefined,
})
const detailError = shallowRef<unknown>()
// plan-001 的方案明细：既作为 planDetail 返回值，也作为「生成首版」的返回方案，
// 让草案工作区拿到真实任务（持久化 override 用例要按 taskId 找回工序）。
const planOne = {
  planId: 'plan-001',
  materialShortageSummary: [] as BusinessConsoleSchedulingMaterialShortageSummary[],
  status: 'generated',
  generatedAtUtc: '2026-07-01T09:30:00Z',
  metrics: {
    scheduledOperationCount: 6,
    unscheduledOperationCount: 1,
    assignedMinutes: 480,
    makespanMinutes: 720,
  },
  assignments: [
    {
      assignmentId: 'assign-001',
      orderId: 'WO-20260701-001',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'RES-CNC-01',
      workCenterId: 'WC-CNC',
      startUtc: '2026-07-02T08:00:00Z',
      endUtc: '2026-07-02T10:00:00Z',
    },
    {
      assignmentId: 'assign-002',
      orderId: 'WO-20260701-001',
      operationId: 'OP-20',
      operationSequence: 20,
      resourceId: 'RES-CNC-01',
      workCenterId: 'WC-CNC',
      startUtc: '2026-07-02T10:00:00Z',
      endUtc: '2026-07-02T12:00:00Z',
      isLocked: true,
    },
    {
      assignmentId: 'assign-003',
      orderId: 'WO-20260701-002',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'RES-ASM-01',
      workCenterId: 'WC-ASSEMBLY',
      startUtc: '2026-07-02T08:30:00Z',
      endUtc: '2026-07-02T11:00:00Z',
    },
    {
      assignmentId: 'assign-004',
      orderId: 'WO-20260701-002',
      operationId: 'OP-20',
      operationSequence: 20,
      resourceId: 'RES-ASM-01',
      workCenterId: 'WC-ASSEMBLY',
      startUtc: '2026-07-02T11:00:00Z',
      endUtc: '2026-07-02T14:00:00Z',
    },
    {
      assignmentId: 'assign-005',
      orderId: 'WO-20260701-003',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'RES-CNC-01',
      workCenterId: 'WC-CNC',
      startUtc: '2026-07-02T12:30:00Z',
      endUtc: '2026-07-02T15:00:00Z',
    },
  ],
  resourceLoads: [
    {
      resourceId: 'RES-CNC-01',
      assignedMinutes: 480,
      availableMinutes: 600,
      utilization: 0.8,
    },
    {
      resourceId: 'RES-ASM-01',
      assignedMinutes: 330,
      availableMinutes: 600,
      utilization: 0.55,
    },
  ],
  conflicts: [
    {
      conflictId: 'conflict-001',
      reasonCode: 'material',
      severity: 'warning',
      orderId: 'WO-20260701-001',
      operationId: 'OP-10',
      resourceId: 'RES-CNC-01',
      message: '关键物料到货晚于计划开工',
    },
  ],
  unscheduledOperations: [
    {
      orderId: 'WO-20260701-002',
      operationId: 'OP-30',
      reasonCode: 'capacity',
      message: '瓶颈资源产能不足',
    },
  ],
}

const detail = computed(() => {
  if (detailSelection.planId === 'plan-released')
    return { ...planOne, planId: 'plan-released', status: 'released' }
  if (detailSelection.planId === 'plan-001') {
    return planOne
  }
  if (detailSelection.planId === 'plan-invalid') {
    return {
      planId: 'plan-invalid',
      status: 'generated',
      assignments: [
        {
          assignmentId: 'valid-locked',
          orderId: 'WO-20260701-004',
          operationId: 'OP-10',
          operationSequence: 10,
          resourceId: 'RES-CNC-01',
          workCenterId: 'WC-CNC',
          startUtc: '2026-07-03T08:00:00Z',
          endUtc: '2026-07-03T10:00:00Z',
          isLocked: true,
        },
        {
          assignmentId: 'invalid-time',
          orderId: 'WO-20260701-004',
          operationId: 'OP-20',
          operationSequence: 20,
          resourceId: 'RES-CNC-01',
          startUtc: '2026-07-03T12:00:00Z',
          endUtc: '2026-07-03T11:00:00Z',
        },
        {
          assignmentId: 'missing-resource',
          orderId: 'WO-20260701-005',
          operationId: 'OP-10',
          operationSequence: 10,
          startUtc: '2026-07-03T08:00:00Z',
          endUtc: '2026-07-03T09:00:00Z',
        },
      ],
      resourceLoads: [],
      conflicts: [],
      unscheduledOperations: [],
    }
  }
  return undefined
})

vi.mock('@/composables/scheduling/useBusinessScheduling', () => ({
  useSchedulingPlanSummary: (planId: () => string | undefined) => ({
    summary: computed(() => {
      const id = planId()
      if (!id) return undefined
      return {
        planId: id,
        status: id === 'plan-released' ? 'released' : 'generated',
        isInvalidated: id === 'plan-invalid' || (id === 'plan-001' && planOneInvalidated.value),
        latestInvalidationReasonCode: 'equipmentUnavailable',
      }
    }),
  }),
  useBusinessScheduling: () => ({
    detailSelection,
    filters: historyFilters,
    page: historyPage,
    pageSize: shallowRef('100'),
    planDetail: detail,
    planDetailError: detailError,
    planDetailPending: shallowRef(false),
    plansTotal: computed(() => 137),
    plans: computed(() =>
      (historyEmpty.value
        ? []
        : [
            {
              status: 'generated',
              generatedAtUtc: '2026-07-01T08:30:00Z',
              assignmentCount: 1,
              conflictCount: 0,
              unscheduledOperationCount: 0,
            },
            {
              planId: 'plan-001',
              isInvalidated: planOneInvalidated.value,
              latestInvalidationReasonCode: planOneInvalidated.value
                ? 'equipmentUnavailable'
                : undefined,
              status: 'generated',
              horizonStartUtc: '2026-09-01T00:00:00Z',
              horizonEndUtc: '2026-09-08T00:00:00Z',
              generatedAtUtc: '2026-07-01T09:30:00Z',
              assignmentCount: 8,
              conflictCount: 1,
              unscheduledOperationCount: 2,
            },
            {
              planId: 'plan-empty',
              status: 'preview',
              generatedAtUtc: '2026-07-01T10:00:00Z',
              assignmentCount: 0,
              conflictCount: 0,
              unscheduledOperationCount: 0,
            },
            {
              planId: 'plan-invalid',
              status: 'generated',
              generatedAtUtc: '2026-07-01T11:00:00Z',
              releasedAtUtc: '2026-07-01T11:30:00Z',
              assignmentCount: 5,
              conflictCount: 0,
              unscheduledOperationCount: 0,
              isInvalidated: true,
              latestInvalidationReasonCode: 'equipmentUnavailable',
              latestInvalidatedAtUtc: '2026-07-01T12:00:00Z',
            },
            {
              planId: 'plan-superseded',
              status: 'superseded',
              assignmentCount: 3,
              conflictCount: 0,
              unscheduledOperationCount: 0,
            },
            {
              planId: 'plan-revoked',
              status: 'revoked',
              assignmentCount: 2,
              conflictCount: 0,
              unscheduledOperationCount: 0,
            },
            // 已发布方案：撤销发布入口只对它开放。
            {
              planId: 'plan-released',
              status: 'released',
              generatedAtUtc: '2026-07-01T12:00:00Z',
              releasedAtUtc: '2026-07-01T12:30:00Z',
              assignmentCount: 4,
              conflictCount: 0,
              unscheduledOperationCount: 0,
            },
          ]
      ).filter(
        (row) =>
          historyPage.value === 1 || !['plan-released', 'plan-001'].includes(row.planId ?? ''),
      ),
    ),
    plansError: shallowRef(undefined),
    plansPending: shallowRef(false),
    releasePlan: stub.releasePlan,
    releasePlanPending: releasePending,
    revokePlan: stub.revokePlan,
    revokePlanPending: shallowRef(false),
    upsertOperationOverride: stub.upsertOperationOverride,
    upsertOperationOverridePending: shallowRef(false),
    refreshPlans: vi.fn(),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const sheetStubs = {
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<aside><slot /></aside>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
}

beforeEach(() => {
  planTimeContextError.value = undefined
  factoryTimezone.value = 'Asia/Shanghai'
  assemblyTimezone.value = undefined
  downtimeImpact.value = undefined
  localCandidateCallback.readSelectedPlan.mockReset()
  localCandidateCallback.readSelectedPlan.mockImplementation(
    async ({ path }: { path: { planId: string } }) => ({
      data: { success: true, data: savedPlans.get(path.planId) },
    }),
  )
  insertionJob.value = undefined
  releasePending.value = false
  authState.principalId = undefined
  savedDrafts.value = []
  savedPlans.clear()
  stub.revisePlan.mockReset()
  associatedOrders.value = []
  associatedError.value = undefined
  candidatesEmpty.value = false
  authState.permissionCodes = [
    'business.scheduling.plans.read',
    'business.scheduling.plans.manage',
    'business.scheduling.plans.release',
  ]
  historyPage.value = 1
  historyEmpty.value = false
  planOneInvalidated.value = false
  historyFilters.status = undefined
  historyFilters.releasedOn = ''
  historyFilters.isInvalidated = undefined
  planOne.materialShortageSummary = []
  routeStub.query = {}
  detailSelection.planId = ''
  detailError.value = undefined
  stub.releasePlan.mockClear()
  stub.revokePlan.mockClear()
  stub.revokePlan.mockResolvedValue({
    success: true,
    data: { planId: 'plan-released', status: 'revoked' },
  })
  stub.upsertOperationOverride.mockClear()
  stub.upsertOperationOverride.mockResolvedValue({ success: true, data: {} })
  capacityCandidates.value = undefined
  generatedPlan.value = undefined
  generatePending.value = false
  firstPlanJob.value = undefined
  generationError.value = undefined
  stub.generatePlan.mockClear()
  stub.generatePlan.mockResolvedValue(planOne)
  stub.toastError.mockClear()
  stub.toastSuccess.mockClear()
})

/**
 * 页面默认停在「排程总览」（挑工单 → 生成 → 发布的主线入口），方案表格是查阅面。
 * 断言表格的用例得先切到那个 Tab —— 和用户真实操作一致。
 */
/** 撤销确认框走 reka 的 teleport，渲染在 document.body 而不是挂载根里。 */
function clickConfirmRevoke() {
  const confirm = [...document.body.querySelectorAll('button')].find((button) =>
    button.textContent?.includes('确认撤销'),
  )
  if (!confirm) throw new Error('撤销确认框没有渲染出「确认撤销」按钮')
  confirm.click()
}

async function openPlanTable(wrapper: ReturnType<typeof mount>) {
  const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格'))!
  await tableTab.trigger('focus')
  await tableTab.trigger('mousedown')
  await flushPromises()
}

export {
  authState,
  stub,
  localCandidateCallback,
  downtimeImpact,
  downtimeBaseline,
  savedDrafts,
  savedPlans,
  pagePersistence,
  associatedOrders,
  associatedError,
  candidatesEmpty,
  generatedPlan,
  releasePending,
  insertionJob,
  detailSelection,
  historyFilters,
  planOne,
  detail,
  layoutStub,
  sheetStubs,
  openPlanTable,
  routeStub,
  planTimeContextError,
  factoryTimezone,
  assemblyTimezone,
  historyEmpty,
  planOneInvalidated,
  detailError,
  clickConfirmRevoke,
  associatedOrderIds,
  firstPlanJob,
  generationError,
  capacityCandidates,
}
