import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPage from './scheduling.vue'
import SchedulingMaterialShortageSummary from '@/components/scheduling/SchedulingMaterialShortageSummary.vue'
import type {
  BusinessConsoleMesWorkOrderItem,
  BusinessConsoleSchedulingMaterialShortageSummary,
  SchedulingCandidateSelection,
} from '@nerv-iip/api-client'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 让断言不依赖真实名录查询。挂载仍装一个新 Pinia（见各 mount 的 plugins）：
// SchedulingPlanGantt 里未被 mock 的 useMesDisplayNames() 需要 active Pinia。
vi.mock('@/composables/useSkuNames', async () => {
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
vi.mock('@/composables/useBusinessPartnerNames', async () => {
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
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
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
}))

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() }, refresh: vi.fn() }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: {
    props: ['orderReference', 'mode', 'urgency'],
    template:
      '<span data-testid="order-urgency" :data-ref="orderReference" :data-mode="mode">未计算</span>',
  },
}))
const authState = vi.hoisted(() => ({ permissionCodes: [] as string[] }))
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
}))
vi.mock('@/composables/useSchedulingCandidates', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/composables/useSchedulingCandidates')>()
  return {
    useSchedulingCandidates: (options: Parameters<typeof actual.useSchedulingCandidates>[0]) => {
      localCandidateCallback.onSelected = options.onSelected
      return actual.useSchedulingCandidates(options)
    },
  }
})
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleSchedulingPlan: localCandidateCallback.readSelectedPlan,
}))

// #1288 作业范围选择入口是独立组件（自带 work-context 查询），页面测试不铺网络桩，整件打桩。
vi.mock('@/components/mes/MesWorkScopeSelect.vue', () => ({
  default: {
    name: 'MesWorkScopeSelect',
    template: '<div data-testid="mes-work-scope-select" />',
  },
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
const insertionJob =
  shallowRef<import('@nerv-iip/api-client').BusinessConsoleSchedulingInsertionPreviewJob>()
vi.mock('@/composables/useSchedulingRushInsertion', () => ({
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
vi.mock('@/composables/useBusinessMes', () => ({
  useMesWorkOrderFacts: (ids: () => (string | undefined)[]) => {
    associatedOrderIds = ids
    return { workOrders: associatedOrders, error: associatedError }
  },
}))

vi.mock('@/composables/useSchedulingWorkbench', () => ({
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

vi.mock('@/composables/useBusinessScheduling', () => ({
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
    releasePlanPending: shallowRef(false),
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
  localCandidateCallback.readSelectedPlan.mockReset()
  insertionJob.value = undefined
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

describe('APS scheduling workbench page', () => {
  it.each(['confirm', 'switch'])('选定保存实际插单候选并绑定确认身份：%s', async (action) => {
    const candidate = {
      ...planOne,
      planId: 'insertion-current',
      status: 'generated' as const,
      conflicts: [],
      unscheduledOperations: [],
    }
    insertionJob.value = {
      jobId: 'job-current',
      status: 'completed',
      input: {
        ...historyFilters,
        planId: 'plan-001',
        workOrderId: 'WO-12',
        workOrderIds: ['WO-12'],
      },
      result: {
        baselinePlanId: 'plan-001',
        candidatePlanId: candidate.planId,
        candidate,
        snapshot: {
          baseline: { ...candidate, planId: 'plan-001' },
          problem: {
            organizationId: 'org-001',
            environmentId: 'env-dev',
            orders: [{ orderId: 'WO-11' }, { orderId: 'WO-12' }],
          },
        },
      },
    }
    stub.revisePlan.mockResolvedValueOnce({ candidate })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    expect(stub.releasePlan).not.toHaveBeenCalled()
    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text().includes('发布新版'))!
        .attributes('disabled'),
    ).toBeDefined()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('选定候选并保存修订'))!
      .trigger('click')
    await flushPromises()
    expect(stub.revisePlan).toHaveBeenCalledWith(
      'insertion-current',
      expect.objectContaining({ includedOrderIds: ['WO-11', 'WO-12'], lockedAssignments: [] }),
    )
    expect(stub.releasePlan).not.toHaveBeenCalled()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('发布新版'))!
      .trigger('click')
    await flushPromises()
    expect(stub.releasePlan).not.toHaveBeenCalled()
    const confirm = [...document.body.querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === '确认发布',
    )!
    expect(confirm).toBeTruthy()
    if (action === 'switch') {
      generatedPlan.value = { ...candidate, planId: 'plan-other' }
      await flushPromises()
      expect(confirm.disabled).toBe(true)
      confirm.click()
      await flushPromises()
      expect(stub.releasePlan).not.toHaveBeenCalled()
      wrapper.unmount()
      return
    }
    confirm.click()
    await flushPromises()
    expect(stub.releasePlan).toHaveBeenCalledWith('insertion-current')
    wrapper.unmount()
  })

  it.each([true, false])(
    'shows the same batch shortages in draft and saved plan (shortage=%s)',
    async (hasShortage) => {
      // #3996：后端已完成共享库存分配，前端只呈现方案级缺口 3，不能按两个工单重复累加。
      planOne.materialShortageSummary = hasShortage
        ? [
            {
              materialId: 'MAT-SHARED',
              materialLotId: 'LOT-1',
              uomCode: 'kg',
              shortageQuantity: 3,
              affectedOperations: [
                { orderId: 'WO-20260701-001', operationId: 'OP-10' },
                { orderId: 'WO-20260701-002', operationId: 'OP-20' },
              ],
            },
          ]
        : []
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      expect(wrapper.findComponent(SchedulingMaterialShortageSummary).exists()).toBe(false)
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((button) => button.text().includes('生成首版'))!
        .trigger('click')
      await flushPromises()
      const draftSummary = wrapper.find(
        '[data-testid="scheduling-draft-board"] [data-testid="scheduling-material-shortage-summary"]',
      )
      if (hasShortage) {
        expect(draftSummary.findAll('tbody tr')).toHaveLength(1)
        expect(draftSummary.findAll('tbody td')[0]!.text()).toContain('MAT-SHARED')
        expect(draftSummary.findAll('tbody td')[1]!.text()).toBe('3 kg')
        expect(draftSummary.text()).toContain('LOT-1')
        expect(draftSummary.text()).toContain('WO-20260701-001 · OP-10')
        expect(draftSummary.text()).toContain('WO-20260701-002 · OP-20')
      } else {
        expect(draftSummary.text()).toContain('方案物料齐套 · 无缺料')
      }
      const expected = draftSummary.text()
      const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特'))!
      await ganttTab.trigger('focus')
      await ganttTab.trigger('mousedown')
      await flushPromises()
      const savedSummary = wrapper.find(
        '[data-testid="scheduling-plan-gantt"] [data-testid="scheduling-material-shortage-summary"]',
      )
      expect(savedSummary.text()).toBe(expected)
      expect(
        wrapper.find('aside [data-testid="scheduling-material-shortage-summary"]').text(),
      ).toBe(expected)
      wrapper.unmount()
    },
  )

  it('preserves completed work-order commercial facts after it leaves the candidate pool', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()
    candidatesEmpty.value = true
    associatedOrders.value = [
      {
        workOrderId: 'WO-20260701-001',
        commercialSourceFacts: {
          status: 'available',
          salesOrders: [{ salesOrderNo: 'SO-COMPLETED', customerCode: 'CUSTOMER-COMPLETED' }],
        },
      },
    ]
    await flushPromises()
    const draftBoard = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
    const tableTab = draftBoard
      .findAll('[role=tab]')
      .find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()
    expect(draftBoard.text()).toContain('SO-COMPLETED')
    expect(draftBoard.text()).toContain('CUSTOMER-COMPLETED')
    await openPlanTable(wrapper)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '明细')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('SO-COMPLETED')
    expect(wrapper.text()).toContain('CUSTOMER-COMPLETED')
    wrapper.unmount()
  })

  it('notifies actual commercial fact read failure and distinguishes unavailable from no source', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '明细')!
      .trigger('click')
    await flushPromises()
    expect(wrapper.text()).not.toContain('暂不可读取')
    associatedError.value = { status: 503, detail: 'Service Unavailable' }
    await flushPromises()
    expect(stub.toastError).toHaveBeenCalledWith(expect.stringContaining('服务暂时不可用'))
    expect(wrapper.text()).toContain('商业关联')
    expect(wrapper.text()).toContain('暂不可读取')
    associatedError.value = undefined
    await flushPromises()
    expect(wrapper.text()).not.toContain('暂不可读取')
    wrapper.unmount()
  })

  it('renders the official scheduling entry with plan summary columns from facade data', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    expect(wrapper.text()).toContain('排产工作台')
    expect(wrapper.text()).toContain('plan-001')
    expect(wrapper.text()).toContain('已生成')
    expect(wrapper.text()).toContain('8')
    expect(wrapper.text()).toContain('1 项冲突')
    expect(wrapper.text()).toContain('2 项未排')
  })

  it('exposes the complete leader-demo workbench loop from one route', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const workbenchTab = wrapper
      .findAll('[role="tab"]')
      .find((tab) => tab.text().includes('排程总览'))!
    await workbenchTab.trigger('focus')
    await workbenchTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('批量待排 → 编辑锁定 → 重预览 → 对比发布')
    expect(wrapper.text()).toContain('待排工单池')
    expect(wrapper.text()).toContain('排程草案工作区')
    expect(wrapper.text()).toContain('工序待排池')
    expect(wrapper.text()).toContain('锁定重预览')
    expect(wrapper.text()).toContain('发布新版')
    // 排程窗口不再写死「现在起 7 天」，工作台上必须有可改的窗口控件（MAN-694 / #1262）。
    expect(wrapper.find('[data-testid="scheduling-horizon-fields"]').exists()).toBe(true)
  })

  it('按用户指定的窗口生成首版，而不是提交时现算的固定 7 天', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()

    // 把窗口换成 1 天：生成请求必须跟着变。
    const horizon = wrapper.findComponent({ name: 'SchedulingHorizonFields' })
    horizon.vm.$emit('update:modelValue', {
      ...(horizon.props('modelValue') as Record<string, unknown>),
      mode: 'preset',
      days: 1,
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()

    const body = stub.generatePlan.mock.calls.at(-1)?.[0] as {
      horizonStartUtc: string
      horizonEndUtc: string
    }
    const span =
      (new Date(body.horizonEndUtc).getTime() - new Date(body.horizonStartUtc).getTime()) /
      86_400_000
    expect(span).toBe(1)
  })

  it('shows server total, real horizons and controlled pagination in the history table', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const table = wrapper.findComponent({ name: 'NvDataTable' })
    expect(table.props('manual')).toBe(true)
    expect(table.props('totalItems')).toBe(137)
    expect(wrapper.text()).toContain('2026-09-01')
    table.vm.$emit('update:page', 2)
    await flushPromises()
    expect(table.props('page')).toBe(2)
    expect(wrapper.text()).toContain('137 个方案')
    expect(wrapper.text()).toContain('发布日（UTC）')
    expect(wrapper.text()).toContain('发布时间从新到旧')
    expect(wrapper.text()).not.toContain('明细中确认')
    expect(wrapper.text()).toContain('工序数')
    expect(wrapper.text()).not.toContain('资源 / 工序')
  })

  it('distinguishes filtered no-match results and lets the user clear all server filters', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)
    historyFilters.status = 'revoked'
    historyFilters.releasedOn = '2026-09-30'
    historyFilters.isInvalidated = true
    historyEmpty.value = true
    await flushPromises()
    expect(wrapper.text()).toContain('没有符合条件的方案')
    expect(wrapper.text()).not.toContain('还没有排程方案')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '清空筛选')!
      .trigger('click')
    expect(historyFilters.status).toBeUndefined()
    expect(historyFilters.releasedOn).toBe('')
    expect(historyFilters.isInvalidated).toBeUndefined()
    wrapper.unmount()
  })

  it.each(['page', 'filter'])(
    'keeps selected released plan revocable after its summary leaves the %s window',
    async (change) => {
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      await openPlanTable(wrapper)
      const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-released'))!
      await row
        .findAll('button')
        .find((button) => button.text().includes('明细'))!
        .trigger('click')
      await flushPromises()
      if (change === 'page') {
        wrapper.findComponent({ name: 'NvDataTable' }).vm.$emit('update:page', 2)
      } else {
        historyFilters.status = 'revoked'
        historyEmpty.value = true
      }
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes('甘特图'))!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      expect(detailSelection.planId).toBe('plan-released')
      const revoke = wrapper.findAll('button').find((button) => button.text().includes('撤销发布'))
      expect(revoke).toBeDefined()
      await revoke!.trigger('click')
      await flushPromises()
      expect(stub.revokePlan).not.toHaveBeenCalled()
      expect(document.body.textContent).toContain('确认撤销发布该排程方案？')
      clickConfirmRevoke()
      await flushPromises()
      expect(stub.revokePlan).toHaveBeenCalledWith('plan-released')
      wrapper.unmount()
    },
  )

  it.each(['page', 'filter'])(
    'publishes the selected generated plan after its summary leaves the %s window',
    async (change) => {
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      await openPlanTable(wrapper)
      const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-001'))!
      await row
        .findAll('button')
        .find((button) => button.text().includes('明细'))!
        .trigger('click')
      await flushPromises()
      if (change === 'page')
        wrapper.findComponent({ name: 'NvDataTable' }).vm.$emit('update:page', 2)
      else {
        historyFilters.status = 'revoked'
        historyEmpty.value = true
      }
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes('甘特图'))!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      const publish = wrapper
        .findAll('button')
        .find((button) => button.text().includes('发布当前方案'))!
      expect(publish.attributes('disabled')).toBeUndefined()
      await publish.trigger('click')
      await flushPromises()
      expect(stub.releasePlan).toHaveBeenCalledWith('plan-001')
      wrapper.unmount()
    },
  )

  it.each(['plan-invalid', 'plan-released'])(
    'preserves the selected %s release restriction after filtering away its summary',
    async (planId) => {
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
      })
      await flushPromises()
      await openPlanTable(wrapper)
      const row = wrapper.findAll('tbody tr').find((item) => item.text().includes(planId))!
      await row
        .findAll('button')
        .find((button) => button.text().includes('明细'))!
        .trigger('click')
      await flushPromises()
      historyFilters.status = 'revoked'
      historyEmpty.value = true
      await flushPromises()
      const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes('甘特图'))!
      await tab.trigger('focus')
      await tab.trigger('mousedown')
      await flushPromises()
      const publish = wrapper
        .findAll('button')
        .find((button) => button.text().includes('发布当前方案'))!
      expect(publish.attributes('disabled')).toBeDefined()
      expect(stub.releasePlan).not.toHaveBeenCalled()
      if (planId === 'plan-invalid') expect(publish.attributes('title')).toContain('设备不可用')
      wrapper.unmount()
    },
  )

  it('renders the selected APS plan as a read-only resource timeline', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub } },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.find('[data-testid="readonly-schedule-timeline"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-resource-lane]')).toHaveLength(2)
    expect(wrapper.findAll('[data-task-id]')).toHaveLength(5)
    expect(wrapper.findAll('[data-conflict="true"]')).toHaveLength(1)
    expect(wrapper.findAll('[data-locked="true"]')).toHaveLength(1)
    // 时间刻度控件改由包内 SchedulingToolbar 提供（#1399 M4）：此前是页面手搓的
    // 「自动适配 / 班次级 / 日级」三个按钮，只有 3 档；现在挂的是引擎那套完整 5 档下拉。
    // 断言相应改成「刻度控件在、四档真实刻度都可选」——要考的是能不能换时间线，
    // 不是按钮上写的哪两个字。
    expect(wrapper.find('[aria-label="时间刻度"]').exists()).toBe(true)
    expect(wrapper.findAllComponents({ name: 'NvSelectItem' }).map((item) => item.text())).toEqual(
      expect.arrayContaining(['自适应', '小时', '日', '周', '月']),
    )
    expect(wrapper.text()).toContain('冲突')
    expect(wrapper.text()).toContain('锁定')
    expect(wrapper.text()).not.toContain('甘特可视化待接入')
  })

  it('opens the clicked operation detail beside the Gantt instead of the whole-plan drawer', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub } },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.find('[data-testid="scheduling-task-detail"]').exists()).toBe(false)

    await wrapper.find('[data-task-id="assign-002"]').trigger('click')
    await flushPromises()

    const detailPanel = wrapper.find('[data-testid="scheduling-task-detail"]')
    expect(detailPanel.exists()).toBe(true)
    // 粒度要对：点的是一道工序，标题与字段就走工序形态（工单汇总行/资源时间块另有形态）。
    expect(detailPanel.attributes('data-detail-kind')).toBe('operation')
    expect(detailPanel.text()).toContain('工序详情')
    // 工序级事实：工单、工序、资源、时间、锁定状态、工单级物料。
    expect(detailPanel.text()).toContain('WO-20260701-001')
    expect(detailPanel.text()).toContain('OP-20')
    expect(detailPanel.text()).toContain('RES-CNC-01')
    expect(detailPanel.text()).toContain('已锁定')
    expect(detailPanel.text()).toContain('SKU-PISTON-01')
    // 齐套没有权威来源：说明去哪儿看，不给估算数。
    expect(detailPanel.text()).toContain('齐套情况请到「领料与齐套」页核对')
    // 甘特没有被遮挡，仍然在场且可继续换选。
    expect(wrapper.find('[data-testid="readonly-schedule-timeline"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('排程方案明细')

    await wrapper.find('[data-task-id="assign-003"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="scheduling-task-detail"]').text()).toContain(
      'WO-20260701-002',
    )
  })

  it('keeps the whole-plan drawer behind the plan-level entry on the Gantt tab', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('方案明细'))!
      .trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('排程方案明细')
    expect(wrapper.text()).toContain('资源分配')
  })

  it('does not render summaries without a plan id as Gantt selector options', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    // 只数**方案选择器**里的选项。原先数的是整页 NvSelectItem 总数，甘特工具栏一挂上来
    // （#1399 M4，多 5 个刻度选项）这条就红了——它考的其实是「无 planId 的 summary 不该
    // 变成方案选项」，与页面上还有几个别的下拉无关。按选项文本前缀锁定方案选项。
    const planOptions = wrapper
      .findAllComponents({ name: 'NvSelectItem' })
      .filter((item) => item.text().startsWith('plan-'))
    expect(planOptions).toHaveLength(6)
  })

  it('opens plan detail and releases the selected plan through the composable', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('明细'))!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.text()).toContain('资源分配')
    expect(wrapper.text()).toContain('RES-CNC-01')
    expect(wrapper.text()).toContain('关键物料到货晚于计划开工')
    expect(wrapper.text()).toContain('瓶颈资源产能不足')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
      .trigger('click')
    await flushPromises()

    expect(stub.releasePlan).toHaveBeenCalledWith('plan-001')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('maps the assignment order id into the shared urgency badge inside plan detail', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('明细'))!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    const refs = wrapper
      .findAll('[data-testid="order-urgency"]')
      .map((badge) => badge.attributes('data-ref'))
    // Assignment.orderId is the real reference fed to the shared badge.
    expect(refs).toContain('WO-20260701-001')
    expect(refs).toContain('WO-20260701-002')
    expect(wrapper.get('[data-testid="order-urgency"]').attributes('data-mode')).toBe('level')
  })

  it('consumes the order reference route and opens the matching assignment in plan detail', async () => {
    routeStub.query = { orderReference: 'WO-20260701-001' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.find('[data-targeted-order="true"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('已定位订单 WO-20260701-001')
  })

  it('单单排产落点：带 planId 进入页面直接打开该方案明细（MAN-694 / #1262）', async () => {
    // 不用列表首个方案（plan-001），特意点名一个靠后的方案，证明是路由说了算。
    routeStub.query = { planId: 'plan-invalid', orderReference: 'WO-20260701-004' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    // 明细查询锁定路由点名的方案，抽屉已打开（而不是停在列表让用户自己找）。
    expect(detailSelection.planId).toBe('plan-invalid')
    expect(wrapper.text()).toContain('排程方案明细')
    expect(wrapper.text()).toContain('已定位订单 WO-20260701-004')
    expect(wrapper.find('[data-targeted-order="true"]').exists()).toBe(true)
  })

  it('带 planId 时不再走「逐个方案找订单」的兜底，方案选择保持路由点名的那个', async () => {
    // 明细里没有这张工单：没有 planId 时页面会翻下一个方案；点名了就不该改选。
    routeStub.query = { planId: 'plan-invalid', orderReference: 'WO-NOT-IN-PLAN' }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-invalid')
    expect(wrapper.text()).toContain('正在定位订单 WO-NOT-IN-PLAN')
  })

  it('refreshes selected Gantt invalidation when the uninvalidated history filter removes it', async () => {
    detailSelection.planId = 'plan-001'
    historyFilters.isInvalidated = false
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()
    const release = wrapper
      .findAll('button')
      .find((button) => button.text().includes('发布当前方案'))!
    expect(release.attributes('disabled')).toBeUndefined()
    planOneInvalidated.value = true
    historyEmpty.value = true
    await flushPromises()
    expect(detailSelection.planId).toBe('plan-001')
    expect(wrapper.text()).toContain('方案已失效，不能从甘特发布')
    expect(release.attributes('disabled')).toBeDefined()
    wrapper.findComponent({ name: 'SchedulingPlanGantt' }).vm.$emit('release')
    await flushPromises()
    expect(stub.releasePlan).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('marks invalidated plans with their reason and blocks release', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    // 失效方案:标记 + 失效原因列展示中文原因
    expect(wrapper.text()).toContain('已失效')
    expect(wrapper.text()).toContain('设备不可用')

    // 失效方案那一行的发布按钮被禁用(须重排后再发布)
    const rows = wrapper.findAll('tbody tr')
    const invalidRow = rows.find((row) => row.text().includes('plan-invalid'))!
    const releaseButton = invalidRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
    expect(releaseButton.attributes('disabled')).toBeDefined()
  })

  it('localizes terminal plan statuses and explains why they cannot be released', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const rows = wrapper.findAll('tbody tr')
    const supersededRow = rows.find((row) => row.text().includes('plan-superseded'))!
    const revokedRow = rows.find((row) => row.text().includes('plan-revoked'))!
    const supersededRelease = supersededRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
    const revokedRelease = revokedRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!

    expect(supersededRow.text()).toContain('已取代')
    expect(supersededRelease.attributes('disabled')).toBeDefined()
    expect(supersededRelease.attributes('title')).toBe('方案已被后续方案取代')
    expect(revokedRow.text()).toContain('已撤销')
    expect(revokedRelease.attributes('disabled')).toBeDefined()
    expect(revokedRelease.attributes('title')).toBe('方案已撤销')
  })

  it('explains invalidated, invalid-time, and missing-resource assignments in the Gantt view', async () => {
    detailSelection.planId = 'plan-invalid'
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('方案已失效')
    expect(wrapper.text()).toContain('设备不可用')
    expect(wrapper.text()).toContain('1 项时间异常')
    expect(wrapper.text()).toContain('1 项缺少资源')
    expect(wrapper.findAll('[data-task-id]')).toHaveLength(1)
    const ganttPublish = wrapper
      .findAll('button')
      .find((button) => button.text().includes('发布当前方案'))!
    expect(ganttPublish.attributes('disabled')).toBeDefined()

    wrapper.findComponent({ name: 'SchedulingPlanGantt' }).vm.$emit('release')
    await flushPromises()
    expect(stub.releasePlan).not.toHaveBeenCalled()
  })

  it('shows a permission-specific Gantt error state', async () => {
    detailSelection.planId = 'plan-empty'
    detailError.value = { response: { status: 403 } }
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('权限不足，无法查看该排程方案')
  })

  it('handles cyclic error causes without overflowing the render stack', async () => {
    detailSelection.planId = 'plan-empty'
    const cyclicError: Record<string, unknown> = {}
    cyclicError.cause = cyclicError
    detailError.value = cyclicError

    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    const ganttTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('甘特图'))!
    await ganttTab.trigger('focus')
    await ganttTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('排程甘特加载失败')
  })

  it('shows explicit detail feedback when a plan detail request fails', async () => {
    detailError.value = new Error('network')
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .filter((button) => button.text().includes('明细'))[1]!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-empty')
    expect(wrapper.text()).toContain('明细加载失败，请稍后重试')
  })

  it('revokes a released plan only after the confirmation dialog is accepted', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const rows = wrapper.findAll('tbody tr')
    // 撤销发布只对已发布方案开放：其他状态行不得出现该入口。
    expect(
      rows
        .find((row) => row.text().includes('plan-001'))!
        .findAll('button')
        .some((button) => button.text().includes('撤销发布')),
    ).toBe(false)

    const releasedRow = rows.find((row) => row.text().includes('plan-released'))!
    await releasedRow
      .findAll('button')
      .find((button) => button.text().includes('撤销发布'))!
      .trigger('click')
    await flushPromises()

    // 只开了确认框，还没真撤销——误触不能直接改状态。
    expect(stub.revokePlan).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('确认撤销发布该排程方案？')

    clickConfirmRevoke()
    await flushPromises()

    expect(stub.revokePlan).toHaveBeenCalledWith('plan-released')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('surfaces the service message and changes nothing when revoke fails', async () => {
    stub.revokePlan.mockRejectedValueOnce(new Error('方案不处于已发布状态'))
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const releasedRow = wrapper
      .findAll('tbody tr')
      .find((row) => row.text().includes('plan-released'))!
    await releasedRow
      .findAll('button')
      .find((button) => button.text().includes('撤销发布'))!
      .trigger('click')
    await flushPromises()
    clickConfirmRevoke()
    await flushPromises()

    // 诚实失败：透传服务端说法，不冒充成功；方案行仍留在「已发布」，用户可重试。
    expect(stub.toastError).toHaveBeenCalledWith('撤销失败：方案不处于已发布状态')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('plan-released')
    wrapper.unmount()
  })

  it('explains why the workbench actions are disabled instead of leaving grey buttons', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    const generate = wrapper.findAll('button').find((button) => button.text().includes('生成首版'))!
    const repreview = wrapper
      .findAll('button')
      .find((button) => button.text().includes('锁定重预览'))!
    const publish = wrapper.findAll('button').find((button) => button.text().includes('发布新版'))!

    // 还没勾工单、还没有草案：三个主操作都灰着，hover 必须说得清缺什么。
    expect(generate.attributes('disabled')).toBeDefined()
    expect(generate.attributes('title')).toBe('还没有选中工单：先在待排工单池里勾选要排的工单')
    expect(repreview.attributes('disabled')).toBeDefined()
    expect(repreview.attributes('title')).toBe('还没有草案方案：先生成首版方案，再做锁定重预览')
    expect(publish.attributes('disabled')).toBeDefined()
    expect(publish.attributes('title')).toBe('还没有可发布的版本：先生成首版或重预览出一版方案')

    // 勾上工单后按钮可用，提示改成"这一步会做什么"。
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()

    const enabledGenerate = wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
    expect(enabledGenerate.attributes('disabled')).toBeUndefined()
    // 「这一步会做什么」现在还带上当前排程窗口（MAN-694 / #1262）：窗口可改之后，
    // 只说"生成首版方案"不足以让人确认排到哪一天。
    expect(enabledGenerate.attributes('title')).toContain('按当前勾选的工单生成首版排程方案')
    expect(enabledGenerate.attributes('title')).toContain('至')
  })

  it('排程窗口非法时并入禁用原因表：按钮直接灰掉并说清改哪里（MAN-694 / #1262）', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()

    // 自定义窗口但起止倒置：#1278 的 firstBlockingReason 要认这条新原因。
    const horizon = wrapper.findComponent({ name: 'SchedulingHorizonFields' })
    horizon.vm.$emit('update:modelValue', {
      ...(horizon.props('modelValue') as Record<string, unknown>),
      mode: 'custom',
      startLocal: '2026-08-05T08:00',
      endLocal: '2026-08-04T08:00',
    })
    await flushPromises()

    const generate = wrapper.findAll('button').find((button) => button.text().includes('生成首版'))!
    expect(generate.attributes('disabled')).toBeDefined()
    expect(generate.attributes('title')).toBe('排程窗口不可用：排程窗口结束时间必须晚于开始时间。')

    // 灰按钮点下去也不能发请求（disabled 与动作函数同一处事实）。
    await generate.trigger('click')
    await flushPromises()
    expect(stub.generatePlan).not.toHaveBeenCalled()
  })

  it('surfaces the service message when generating the first plan fails', async () => {
    // generated client 在 throwOnError 下抛的是响应体对象（不是 Error）：以前这里被吞成猜测文案。
    stub.generatePlan.mockRejectedValueOnce({
      title: 'Bad Request',
      detail: '工单 WO-20260701-001 缺少生产版本，无法排程',
      status: 400,
    })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalledWith(
      '生成失败：工单 WO-20260701-001 缺少生产版本，无法排程',
    )
    expect(stub.toastSuccess).not.toHaveBeenCalled()
  })

  it('surfaces the service message when releasing a plan fails', async () => {
    stub.releasePlan.mockRejectedValueOnce({ message: '方案已被后续方案取代，不能发布' })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const planRow = wrapper.findAll('tbody tr').find((row) => row.text().includes('plan-001'))!
    await planRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
      .trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalledWith('发布失败：方案已被后续方案取代，不能发布')
  })

  it('never puts an English 500 body on screen — falls back to the domain hint', async () => {
    // 反馈规范禁止英文错误码 / 5xx 原文上屏：这类通用文案只进 console，界面用领域兜底。
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    stub.releasePlan.mockRejectedValueOnce({ title: 'Internal Server Error', status: 500 })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const planRow = wrapper.findAll('tbody tr').find((row) => row.text().includes('plan-001'))!
    await planRow
      .findAll('button')
      .find((button) => button.text().includes('发布'))!
      .trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalledWith('发布失败，请稍后重试')
    expect(stub.toastError).not.toHaveBeenCalledWith(expect.stringContaining('Internal Server'))
    expect(consoleError).toHaveBeenCalled()
    consoleError.mockRestore()
  })

  it('states that the history plan table is read-only and routes edits to the draft workbench', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const notice = wrapper.find('[data-testid="plan-table-readonly-notice"]')
    expect(notice.exists()).toBe(true)
    expect(notice.text()).toContain('只读查阅')
    expect(notice.text()).toContain('回草案工作区')

    await notice
      .findAll('button')
      .find((button) => button.text().includes('去草案工作区修改'))!
      .trigger('click')
    await flushPromises()

    // 引导入口要真的把人送回可编辑的地方，不是一句说明。
    expect(wrapper.text()).toContain('批量待排 → 编辑锁定 → 重预览 → 对比发布')
    expect(wrapper.text()).toContain('待排工单池')
  })

  it('does not apply a selected candidate or report success when restoring its plan fails', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()
    const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
    expect(board.props('model').meta.planId).toBe('plan-001')
    const previousDetail = detailSelection.planId
    stub.toastSuccess.mockClear()
    localCandidateCallback.readSelectedPlan.mockRejectedValueOnce(
      new Error('GET selected plan failed: 500'),
    )
    await localCandidateCallback.onSelected!({
      plan: { ...planOne, planId: 'selected-plan', status: 'generated' },
      workingDraft: {
        planId: 'selected-plan',
        savedAtUtc: '2026-10-08T08:00:00Z',
        state: { contractVersion: 1, orders: [], tasks: [] },
      },
      comparison: {
        basePlanId: 'plan-001',
        candidatePlanId: 'selected-plan',
        movedOperationCount: 1,
      },
    } as SchedulingCandidateSelection)
    await flushPromises()
    expect(board.props('model').meta.planId).toBe('plan-001')
    expect(detailSelection.planId).toBe(previousDetail)
    expect(
      wrapper.findComponent({ name: 'ScheduleRevisionReview' }).props('revision'),
    ).toBeUndefined()
    expect(stub.toastSuccess).not.toHaveBeenCalled()
  })

  it('compares each revision against its persisted base rather than the edited draft', async () => {
    // Regression / DomainInvariant: #3625 原值来自持久化版本，连续修订以刚生成的候选为下一次基线。
    const candidate = {
      ...planOne,
      planId: 'plan-002',
      assignments: planOne.assignments.map((assignment) =>
        assignment.assignmentId === 'assign-001'
          ? { ...assignment, resourceId: 'RES-CNC-02' }
          : assignment,
      ),
      changeSummary: [
        {
          orderId: 'WO-20260701-001',
          operationId: 'OP-10',
          changeType: 'moved',
          message: '改派备用设备',
        },
      ],
    }
    stub.revisePlan.mockResolvedValueOnce({ candidate }).mockResolvedValueOnce({
      candidate: {
        ...candidate,
        planId: 'plan-003',
        assignments: candidate.assignments.map((assignment) =>
          assignment.assignmentId === 'assign-001'
            ? { ...assignment, resourceId: 'RES-CNC-03' }
            : assignment,
        ),
      },
    })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()
    const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
    board.vm.$emit('update', 'assign-001', { resourceId: 'RES-CNC-02' })
    board.vm.$emit('lock', 'assign-001', true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('锁定重预览'))!
      .trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-change-row]').text()).toContain('RES-CNC-01 → RES-CNC-02')
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('锁定重预览'))!
      .trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-change-row]').text()).toContain('RES-CNC-02 → RES-CNC-03')
  })

  it.each(['other-plan', 'filtered-history'])(
    'keeps draft invalidation and edits independent of %s',
    async (scenario) => {
      // #4072：已有方案失效只刷新读面与操作状态，人工资源修改继续留在草案。
      const wrapper = mount(SchedulingPage, {
        global: { plugins: [createPinia()], stubs: layoutStub },
      })
      await flushPromises()
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((button) => button.text().includes('生成首版'))!
        .trigger('click')
      await flushPromises()
      const board = wrapper.findComponent({ name: 'SchedulingDraftBoard' })
      board.vm.$emit('update', 'assign-001', { resourceId: 'RES-CNC-02' })
      await flushPromises()
      const editedModel = board.props('model')
      const publish = wrapper
        .findAll('button')
        .find((button) => button.text().includes('发布新版'))!
      expect(publish.attributes('disabled')).toBeUndefined()

      if (scenario === 'filtered-history') {
        historyFilters.isInvalidated = false
        historyEmpty.value = true
        await flushPromises()
      }
      planOneInvalidated.value = true
      await flushPromises()
      if (scenario === 'other-plan') {
        detailSelection.planId = 'plan-invalid'
        await flushPromises()
      }
      expect(publish.attributes('disabled')).toBeDefined()
      expect(publish.attributes('title')).toContain('设备不可用')
      expect(wrapper.text()).toContain('方案已失效（设备不可用），请重排后再发布')
      expect(board.props('model')).toBe(editedModel)
      expect(
        board.props('model').tasks.find((task: { id: string }) => task.id === 'assign-001')
          .resourceId,
      ).toBe('RES-CNC-02')
      wrapper.unmount()
    },
  )

  it('persists a draft operation override with the plan id and the operation behind the task', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    // 挑工单 → 生成首版 → 草案工作区才有工序可持久锁定。
    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()

    expect(stub.generatePlan).toHaveBeenCalled()

    // 组件回的是甘特 taskId（= assignmentId）；页面必须把它映回 operationId 再落库。
    wrapper
      .findComponent({ name: 'SchedulingDraftBoard' })
      .vm.$emit('persistOverride', 'assign-001')
    await flushPromises()

    expect(stub.upsertOperationOverride).toHaveBeenCalledWith({
      planId: 'plan-001',
      operationId: 'OP-10',
      resourceId: 'RES-CNC-01',
      startUtc: '2026-07-02T08:00:00Z',
      endUtc: '2026-07-02T10:00:00Z',
    })
    expect(stub.toastSuccess).toHaveBeenCalledWith('工序已持久锁定，之后重新排程也保持不变')
  })

  it('refuses to persist an override for a task the draft does not know', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    // 草案还没生成方案：不能静默吞掉这次点击，也绝不能发请求。
    wrapper
      .findComponent({ name: 'SchedulingDraftBoard' })
      .vm.$emit('persistOverride', 'assign-001')
    await flushPromises()

    expect(stub.upsertOperationOverride).not.toHaveBeenCalled()
  })

  it('shows explicit detail feedback when the facade returns no detail payload', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: { ...layoutStub, ...sheetStubs } },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    await wrapper
      .findAll('button')
      .filter((button) => button.text().includes('明细'))[1]!
      .trigger('click')
    await flushPromises()

    expect(detailSelection.planId).toBe('plan-empty')
    expect(wrapper.text()).toContain('未返回方案明细')
  })
})

// DomainInvariant: #3634 与产品文档 §4；页面裁剪不替代 Gateway 的最终授权。
describe('排产三级权限', () => {
  it.each([
    ['只读', false, false],
    ['管理', true, false],
    ['发布', true, true],
  ] as const)('%s角色按权限开放动作并说明禁用原因', async (_role, manage, release) => {
    authState.permissionCodes = [
      'business.scheduling.plans.read',
      ...(manage ? ['business.scheduling.plans.manage'] : []),
      ...(release ? ['business.scheduling.plans.release'] : []),
    ]
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    if (manage) {
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
    }
    const generate = wrapper.findAll('button').find((button) => button.text().includes('生成首版'))!
    expect(generate.attributes('disabled') !== undefined).toBe(!manage)
    if (!manage) expect(generate.attributes('title')).toContain('没有排产管理权限')
    expect(wrapper.findComponent({ name: 'SchedulingDraftBoard' }).props('readOnly')).toBe(!manage)

    await openPlanTable(wrapper)
    const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-001'))!
    const publish = row.findAll('button').find((button) => button.text().trim() === '发布')!
    expect(publish.attributes('disabled') !== undefined).toBe(!release)
    expect(publish.attributes('title')).toContain(release ? '发布该排程方案' : '没有排程发布权限')
    const released = wrapper
      .findAll('tbody tr')
      .find((item) => item.text().includes('plan-released'))!
    expect(released.findAll('button').some((button) => button.text().includes('撤销发布'))).toBe(
      release,
    )
    wrapper.unmount()
  })

  it('Gateway 拒绝发布时显示中文权限原因', async () => {
    stub.releasePlan.mockRejectedValueOnce({
      success: false,
      code: 403,
      message: 'Forbidden.',
      data: [],
    })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)
    const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-001'))!
    await row
      .findAll('button')
      .find((button) => button.text().trim() === '发布')!
      .trigger('click')
    await flushPromises()
    expect(stub.toastError).toHaveBeenCalledWith('发布失败：没有权限执行此操作。')
    wrapper.unmount()
  })
})

describe('异步首版页面选择容量（#4137 DomainInvariant）', () => {
  it('单选与全部加入拒绝第 501 单，500 单提交使用完整选单', async () => {
    capacityCandidates.value = Array.from({ length: 501 }, (_, i) => ({
      workOrderId: `WO-${i}`,
      productionVersionId: 'PV-001',
      status: 'released',
      priority: 100,
    }))
    const wrapper = mount(SchedulingPage, {
      // 页面边界测完整选单与提交；真实 500/501 checkbox 由 SchedulingOrderPool.test.ts 覆盖。
      global: {
        plugins: [createPinia()],
        stubs: {
          ...layoutStub,
          SchedulingOrderPool: {
            name: 'SchedulingOrderPool',
            props: ['candidates', 'draftOrders'],
            emits: ['include'],
            template: '<div />',
          },
          SchedulingDraftBoard: {
            name: 'SchedulingDraftBoard',
            props: ['model'],
            template: '<div />',
          },
        },
      },
    })
    try {
      await flushPromises()
      const pool = wrapper.findComponent({ name: 'SchedulingOrderPool' })
      const generate = () => wrapper.findAll('button').find((b) => b.text().includes('生成首版'))!
      pool.vm.$emit(
        'include',
        capacityCandidates.value.map((o) => o.workOrderId!),
        true,
      )
      await flushPromises()
      expect(stub.toastError).toHaveBeenCalledWith('首版排程最多选择 500 单，请先移出超出的工单')
      expect(generate().attributes('disabled')).toBeDefined()
      pool.vm.$emit(
        'include',
        capacityCandidates.value.slice(0, 500).map((o) => o.workOrderId!),
        true,
      )
      await flushPromises()
      pool.vm.$emit('include', ['WO-500'], true)
      await flushPromises()
      expect(
        pool.props('draftOrders').filter((o: { included: boolean }) => o.included),
      ).toHaveLength(500)
      await generate().trigger('click')
      await flushPromises()
      expect(stub.generatePlan.mock.calls.at(-1)?.[0].orders).toHaveLength(500)
      expect(
        wrapper
          .findComponent({ name: 'SchedulingDraftBoard' })
          .props('model')
          .tasks.some((task: { orderId: string }) => task.orderId === 'WO-20260701-001'),
      ).toBe(true)
    } finally {
      wrapper.unmount()
    }
  })
})

describe('500 单首版关联事实复用（#4137 Regression）', () => {
  it('使用待排池已返回的商业来源，历史池外工单仍按 ID 读取', async () => {
    capacityCandidates.value = [
      {
        workOrderId: 'WO-20260701-001',
        productionVersionId: 'PV-001',
        status: 'released',
        commercialSourceFacts: null,
      },
    ]
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    try {
      await flushPromises()
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((b) => b.text().includes('生成首版'))!
        .trigger('click')
      await flushPromises()
      expect(associatedOrderIds()).not.toContain('WO-20260701-001')
      expect(associatedOrderIds()).toContain('WO-20260701-002')
    } finally {
      wrapper.unmount()
    }
  })
})

describe('异步首版最终状态与草稿一致性（#4137 Regression）', () => {
  it('完成后方案加载失败仍显示已完成，并提示查看已有方案', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    try {
      await flushPromises()
      firstPlanJob.value = { status: 'completed', planId: 'plan-001' }
      generationError.value = new Error('HTTP 503')
      await flushPromises()
      expect(wrapper.text()).toContain('首版排程已完成，方案加载失败')
      expect(wrapper.text()).not.toContain('重新生成')
      expect(stub.toastError).toHaveBeenCalledWith(expect.stringContaining('方案加载失败'))
    } finally {
      wrapper.unmount()
    }
  })

  it('等待首版时禁止撤销选单、切换或清空草稿，完成后选单与任务一致', async () => {
    let complete!: (plan: typeof planOne) => void
    stub.generatePlan.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          complete = resolve
        }),
    )
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    try {
      await flushPromises()
      const pool = wrapper.findComponent({ name: 'SchedulingOrderPool' })
      pool.vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      const button = (text: string) =>
        wrapper.findAll('button').find((b) => b.text().trim() === text)!
      expect(button('撤销').attributes('disabled')).toBeUndefined()
      await button('生成首版').trigger('click')
      await flushPromises()
      expect(button('撤销').attributes('disabled')).toBeDefined()
      expect(button('重做').attributes('disabled')).toBeDefined()
      expect(button('清空草稿').attributes('disabled')).toBeDefined()
      expect(pool.props('readOnly')).toBe(true)
      await button('撤销').trigger('click')
      expect(
        pool.props('draftOrders').filter((o: { included: boolean }) => o.included),
      ).toHaveLength(1)
      complete({
        ...planOne,
        assignments: planOne.assignments.filter((a) => a.orderId === 'WO-20260701-001'),
      })
      await flushPromises()
      expect(pool.props('readOnly')).toBe(false)
      expect(button('生成首版').attributes('disabled')).toBeUndefined()
      expect(
        pool
          .props('draftOrders')
          .find((o: { workOrderId: string }) => o.workOrderId === 'WO-20260701-001').included,
      ).toBe(true)
      expect(
        wrapper
          .findComponent({ name: 'SchedulingDraftBoard' })
          .props('model')
          .tasks.some((t: { orderId: string }) => t.orderId === 'WO-20260701-001'),
      ).toBe(true)
    } finally {
      wrapper.unmount()
    }
  })
})
