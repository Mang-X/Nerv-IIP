import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, reactive, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPage from './scheduling.vue'

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
      deviceByCode: emptyIndex,
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
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      permissionCodes: [
        'business.scheduling.plans.read',
        'business.scheduling.plans.manage',
        'business.scheduling.plans.release',
      ],
    },
  }),
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
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

vi.mock('@/composables/useSchedulingWorkbench', () => ({
  useSchedulingWorkbench: () => ({
    // 甘特工序详情用它把物料/数量/交期 join 到工序上（工单级事实，见 SchedulingPlanGantt）。
    candidates: computed(() => [
      {
        workOrderId: 'WO-20260701-001',
        skuCode: 'SKU-PISTON-01',
        quantity: 120,
        dueUtc: '2026-07-06T00:00:00Z',
        status: 'released',
        productionVersionId: 'pv-001',
      },
    ]),
    candidatesError: shallowRef(undefined),
    candidatesPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    generatePending: shallowRef(false),
    generatePlan: stub.generatePlan,
    refreshCandidates: vi.fn(),
    revisionPending: shallowRef(false),
    revisePlan: vi.fn(),
    // 草案工作区要有可选工单才能生成首版方案（持久化 override 用例的前置条件）。
    schedulableCandidates: computed(() => [
      {
        workOrderId: 'WO-20260701-001',
        productionVersionId: 'PV-001',
        skuCode: 'SKU-PISTON-ROD',
        status: 'released',
        priority: 100,
      },
    ]),
  }),
}))

const detailSelection = reactive({ planId: '' })
const detailError = shallowRef<unknown>()
// plan-001 的方案明细：既作为 planDetail 返回值，也作为「生成首版」的返回方案，
// 让草案工作区拿到真实任务（持久化 override 用例要按 taskId 找回工序）。
const planOne = {
  planId: 'plan-001',
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
  useBusinessScheduling: () => ({
    detailSelection,
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    page: shallowRef(1),
    pageSize: shallowRef('100'),
    planDetail: detail,
    planDetailError: detailError,
    planDetailPending: shallowRef(false),
    plans: computed(() => [
      {
        status: 'generated',
        generatedAtUtc: '2026-07-01T08:30:00Z',
        assignmentCount: 1,
        conflictCount: 0,
        unscheduledOperationCount: 0,
      },
      {
        planId: 'plan-001',
        status: 'generated',
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
    ]),
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
  DialogRoot: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<aside><slot /></aside>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
}

beforeEach(() => {
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

  it('uses a single-page table while the facade does not return a total count', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)

    const table = wrapper.findComponent({ name: 'NvDataTable' })
    expect(table.props('pagination')).toBe(false)
    expect(table.props('manual')).not.toBe(true)
    expect(wrapper.text()).toContain('工序数')
    expect(wrapper.text()).not.toContain('资源 / 工序')
  })

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
    expect(wrapper.text()).toContain('班次级')
    expect(wrapper.text()).toContain('日级')
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
    expect(detailPanel.text()).toContain('齐套率排程契约未返回')
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
    expect(wrapper.findAllComponents({ name: 'NvSelectItem' })).toHaveLength(6)
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
    expect(stub.toastSuccess).toHaveBeenCalledWith('工序 override 已持久化，重排程自动继承')
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
