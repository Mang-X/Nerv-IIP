import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrdersListPage from './index.vue'

const releaseState = vi.hoisted(() => ({
  items: [] as Array<Record<string, unknown>>,
  manageScope: { kind: 'work-center', id: 'WC-1', displayName: '一号线' },
  readScope: { kind: 'work-center', id: 'WC-1', displayName: '一号线' },
  scopeMessage: '',
  scopeReady: true,
}))
const releaseWorkOrder = vi.hoisted(() => vi.fn())
const releaseWorkOrderErrorState = vi.hoisted(() => ({ value: undefined as unknown }))
const readWorkOrderForRelease = vi.hoisted(() => vi.fn())
const refreshWorkOrders = vi.hoisted(() => vi.fn())
const refreshOperationTasks = vi.hoisted(() => vi.fn())
const notifyOperationFailure = vi.hoisted(() => vi.fn())
const notifySuccess = vi.hoisted(() => vi.fn())

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: vi.fn() }),
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: () => '',
  notifyOperationFailure,
  notifySuccess,
}))

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({
    byReference: ref(new Map()),
    error: ref(undefined),
    refresh: vi.fn(),
  }),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSku: (value?: string | null) => value ?? '无',
    resolveWorkCenter: (value?: string | null) => value ?? '无',
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({ resources: ref([]) }),
  useBusinessSkus: () => ({ skus: ref([]) }),
}))

vi.mock('@/composables/useMesPickerCatalog', () => ({
  useMesMaterialVersionCatalog: () => ({
    productionVersionOptions: () => [],
    productionVersionsPending: ref(false),
  }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  describeMesReadinessReason: (reason: string) => ({
    code: reason.split(':')[0],
    detail: reason,
    label: reason.includes('MATERIAL') ? '物料就绪条件未满足' : '设备就绪条件未满足',
    nextStep: '',
  }),
  useMesWorkScopeSelection: () => ({
    scopeOptions: ref([]),
    scopeSelectionValue: ref(undefined),
    scopeReady: ref(true),
    scopeMessage: ref(''),
    scopePending: ref(false),
    scopeUnavailable: ref(false),
    selectedScope: ref(undefined),
    principalIdentity: ref('principal-test'),
    requireSelectedScope: vi.fn(),
  }),
  useMesOperationTasks: () => ({
    operationTasks: ref([]),
    operationTasksPending: ref(false),
    refreshOperationTasks,
  }),
  useMesWorkOrders: () => ({
    createRushWorkOrder: vi.fn(),
    createRushWorkOrderError: ref(undefined),
    createRushWorkOrderPending: ref(false),
    filters: reactive({
      organizationId: 'org-1',
      environmentId: 'prod',
      status: undefined,
      skip: 0,
      take: 20,
    }),
    refreshWorkOrders,
    readWorkOrderForRelease,
    releaseWorkOrder,
    releaseWorkOrderError: releaseWorkOrderErrorState,
    releaseWorkOrderPending: ref(false),
    workOrders: ref(releaseState.items),
    workOrdersError: ref(undefined),
    workOrdersHasFailedResponse: ref(false),
    workOrdersHasSuccessfulResponse: ref(true),
    workOrdersLastUpdatedAt: ref('2026-08-25T00:00:00.000Z'),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(releaseState.items.length),
    workOrderManageScope: ref(releaseState.manageScope),
    workOrderManageScopeMessage: ref(releaseState.scopeMessage),
    workOrderManageScopePending: ref(false),
    workOrderManageScopeReady: ref(releaseState.scopeReady),
    workOrderReadScope: ref(releaseState.readScope),
    workOrderReadScopeMessage: ref(''),
    workOrderReadScopeReady: ref(true),
  }),
  useMesWorkOrderTransformations: () => ({
    splitWorkOrder: vi.fn(),
    mergeWorkOrders: vi.fn(),
    readTransformation: vi.fn(),
    splitWorkOrderPending: ref(false),
    mergeWorkOrdersPending: ref(false),
  }),
}))

const uiStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  ListScopeMeta: true,
  MesWorkScopeSelect: true,
  OrderUrgencyBadge: true,
  ProductionReportDialog: true,
  UrgencyDisplayModeSelect: true,
  WorkOrderDetailSheet: true,
  NvPageHeader: { template: '<header><slot name="actions" /></header>' },
  NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
  NvDataTable: {
    props: ['rows'],
    template:
      '<div><template v-for="row in rows" :key="row.workOrderId"><slot name="cell-actions" :row="row" /></template></div>',
  },
  NvRowActions: { template: '<div><slot /></div>' },
  RowActions: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" type="button" :disabled="disabled"><slot /></button>',
  },
  DropdownMenuItem: {
    props: ['disabled'],
    template: '<button v-bind="$attrs" type="button" :disabled="disabled"><slot /></button>',
  },
  NvDropdownMenuSeparator: true,
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvCheckbox: {
    props: ['modelValue', 'disabled'],
    emits: ['update:modelValue'],
    template:
      '<input type="checkbox" :checked="modelValue" :disabled="disabled" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  },
  NvButton: {
    props: ['disabled', 'type'],
    template: '<button :type="type || \'button\'" :disabled="disabled"><slot /></button>',
  },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvEntityPicker: true,
  NvInput: true,
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button type="button"><slot /></button>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectValue: true,
  NvStatusBadge: true,
  Spinner: true,
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

/**
 * 基础夹具是 `status: 'created'` + 一道 `queued` 工序，因此 `blockReasons` **必须**带上
 * `WORK_ORDER_NOT_RELEASED`——#3119 的守卫让 `MesOperationTaskActionReadinessEvaluator`
 * 对这个组合无条件产出该码，「created + 空 blockReasons」这个输入服务端已经回不出来了。
 * 上一版夹具停在空数组，于是本文件的下达用例整体寄生在一份与服务端契约漂移的 mock 上
 * （它们对 `blockReasons` 本来是有鉴别力的，失真的是输入，不是断言）。
 */
function workOrder(overrides: Record<string, unknown> = {}) {
  return {
    workOrderId: 'WO-1',
    workOrderNo: 'WO-20260825-001',
    skuId: 'FG-1',
    productionVersionId: 'PV-1',
    quantity: 10,
    status: 'created',
    operationTasks: [
      {
        operationTaskId: 'OP-1',
        operationSequence: 10,
        status: 'queued',
        blockReasons: ['WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。'],
        evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
      },
    ],
    ...overrides,
  }
}

function mountPage(
  permissionCodes = ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'user-1',
      principalType: 'user',
      organizationId: 'org-1',
      environmentId: 'prod',
      loginName: 'planner',
      permissionCodes,
    },
  })
  return mount(WorkOrdersListPage, {
    global: { plugins: [pinia], stubs: uiStubs },
  })
}

function button(wrapper: VueWrapper, label: string) {
  const target = wrapper.findAll('button').find((item) => item.text().includes(label))
  if (!target) {
    throw new Error(
      `未找到按钮：${label}；现有按钮：${wrapper
        .findAll('button')
        .map((item) => item.text())
        .join(' | ')}`,
    )
  }
  return target
}

describe('work-order list — release entry', () => {
  beforeEach(() => {
    releaseState.items = [workOrder()]
    releaseState.manageScope = { kind: 'work-center', id: 'WC-1', displayName: '一号线' }
    releaseState.readScope = { kind: 'work-center', id: 'WC-1', displayName: '一号线' }
    releaseState.scopeMessage = ''
    releaseState.scopeReady = true
    releaseWorkOrder.mockReset()
    releaseWorkOrderErrorState.value = undefined
    releaseWorkOrder.mockResolvedValue({
      data: { accepted: true, downstreamDocumentId: 'WO-1' },
    })
    readWorkOrderForRelease.mockReset()
    readWorkOrderForRelease.mockImplementation(async (workOrderId: string) => {
      const candidate = releaseState.items.find((item) => item.workOrderId === workOrderId)
      if (!candidate) throw new Error(`未找到工单 ${workOrderId}`)
      return candidate
    })
    refreshWorkOrders.mockReset().mockResolvedValue(undefined)
    refreshOperationTasks.mockReset().mockResolvedValue(undefined)
    notifyOperationFailure.mockReset()
    notifySuccess.mockReset()
  })

  it('submits the selected work order with warning confirmation and no actor or scope claims', async () => {
    releaseState.items = [
      workOrder(),
      workOrder({
        workOrderId: 'WO-2',
        workOrderNo: 'WO-20260825-002',
        operationTasks: [
          {
            operationTaskId: 'OP-2',
            operationSequence: 20,
            status: 'queued',
            blockReasons: ['WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。'],
            evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
          },
        ],
      }),
    ]
    const wrapper = mountPage()

    await wrapper.get('[aria-label="下达工单 WO-20260825-002"]').trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('确认下达工单')
    expect(wrapper.text()).toContain('WO-20260825-002')

    await wrapper.get('input[type="checkbox"]').setValue(true)
    const submit = button(wrapper, '确认下达')
    expect(submit.attributes('disabled')).toBeUndefined()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(releaseWorkOrder).toHaveBeenCalledTimes(1)
    expect(releaseWorkOrder).toHaveBeenCalledWith('WO-2', {
      organizationId: 'org-1',
      environmentId: 'prod',
      confirmWarnings: true,
      idempotencyKey: expect.stringMatching(/^release-work-order-WO-2-/),
    })
    expect(releaseWorkOrder.mock.calls[0]?.[1]).not.toHaveProperty('actor')
    expect(releaseWorkOrder.mock.calls[0]?.[1]).not.toHaveProperty('principalId')
    expect(releaseWorkOrder.mock.calls[0]?.[1]).not.toHaveProperty('scopeId')
    expect(releaseWorkOrder.mock.calls[0]?.[1]).not.toHaveProperty('scopeKind')
    expect(readWorkOrderForRelease).toHaveBeenCalledTimes(1)
    expect(refreshWorkOrders).toHaveBeenCalledTimes(1)
    expect(refreshOperationTasks).toHaveBeenCalledTimes(1)
    expect(notifySuccess).toHaveBeenCalledWith(expect.stringContaining('WO-20260825-002'))
  })

  /**
   * #3119 回归。本文件其余下达用例的夹具都写死 `blockReasons: []`，
   * **而守卫上线后后端对 `created` 工单的 queued 工序恒回 `WORK_ORDER_NOT_RELEASED`**——
   * 那个组合已经不是服务端回得出的输入，18 条绿全部寄生在一份漂移的 mock 上。
   * 这一条用后端真正会回的载荷，钉住「下达」按钮**不被那条『你还没下达』的理由禁掉」。
   */
  it('keeps the release action enabled when the row carries WORK_ORDER_NOT_RELEASED', async () => {
    releaseState.items = [
      workOrder({
        operationTasks: [
          {
            operationTaskId: 'OP-1',
            operationSequence: 10,
            status: 'queued',
            blockReasons: ['WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。'],
            evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
          },
        ],
      }),
    ]
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeUndefined()
    await action.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('确认下达工单')
  })

  it('allows a covering workshop manage scope to preflight a work-center list row', async () => {
    releaseState.manageScope = { kind: 'workshop', id: 'WS-1', displayName: '总装车间' }
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeUndefined()
    await action.trigger('click')
    await flushPromises()

    expect(readWorkOrderForRelease).toHaveBeenCalledWith('WO-1')
    expect(wrapper.text()).toContain('确认下达工单')
    expect(releaseWorkOrder).not.toHaveBeenCalled()
  })

  it('does not send when the selected manage scope cannot read back the target work order', async () => {
    releaseState.manageScope = { kind: 'work-center', id: 'WC-2', displayName: '二号线' }
    const scopeError = new Error('403：工单不在当前管理范围')
    readWorkOrderForRelease.mockRejectedValue(scopeError)
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeUndefined()
    await action.trigger('click')
    await flushPromises()

    expect(readWorkOrderForRelease).toHaveBeenCalledWith('WO-1')
    expect(wrapper.text()).not.toContain('确认下达工单')
    expect(releaseWorkOrder).not.toHaveBeenCalled()
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '工单下达前置检查失败',
      scopeError,
      expect.any(String),
    )
  })

  it.each([
    ['设备不可用', 'equipment.downtime: 工作中心停机'],
    ['物料短缺', 'MATERIAL_SHORTAGE: 物料 MAT-1 缺口 5'],
  ])('does not send when the authoritative task readiness reports %s', async (_name, reason) => {
    releaseState.items = [
      workOrder({
        operationTasks: [
          {
            operationTaskId: 'OP-1',
            operationSequence: 10,
            status: 'queued',
            blockReasons: [
              reason,
              'WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。',
            ],
            evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
          },
        ],
      }),
    ]
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeDefined()
    await action.trigger('click')
    await flushPromises()

    expect(releaseWorkOrder).not.toHaveBeenCalled()
  })

  it('fails closed when the task readiness snapshot is missing', async () => {
    releaseState.items = [
      workOrder({
        operationTasks: [
          {
            operationTaskId: 'OP-1',
            operationSequence: 10,
            status: 'queued',
          },
        ],
      }),
    ]
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeDefined()
    await action.trigger('click')
    await flushPromises()

    expect(releaseWorkOrder).not.toHaveBeenCalled()
  })

  it('fails closed when the material requirement snapshot is not proven', async () => {
    const latest = workOrder({
      operationTasks: [
        {
          operationTaskId: 'OP-1',
          operationSequence: 10,
          status: 'queued',
          blockReasons: [
            'MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照',
            'WORK_ORDER_NOT_RELEASED: 工单尚未下达，请先下达工单后再开工或报工。',
          ],
          evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
        },
      ],
    })
    readWorkOrderForRelease.mockResolvedValue(latest)
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeUndefined()
    await action.trigger('click')
    await flushPromises()

    expect(readWorkOrderForRelease).toHaveBeenCalledWith('WO-1')
    expect(wrapper.text()).not.toContain('确认下达工单')
    expect(releaseWorkOrder).not.toHaveBeenCalled()
  })

  // #3118：后端 `ReleaseWorkOrderCommandHandler` 的下达守卫不看工序状态，工序在制的
  // created 工单照样受理。界面此前要求全部工序 queued，比后端更严，把「事后补下达」
  // 这条自愈路径整个藏掉；这里钉住的是「界面不得比后端守卫更严」。
  it('releases a work order whose operation is already in progress, and the dialog states the premise', async () => {
    releaseState.items = [
      workOrder({
        operationTasks: [
          {
            operationTaskId: 'OP-1',
            operationSequence: 10,
            status: 'inProgress',
            blockReasons: [],
            evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
          },
        ],
      }),
    ]
    const wrapper = mountPage()

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeUndefined()
    await action.trigger('click')
    await flushPromises()

    expect(readWorkOrderForRelease).toHaveBeenCalledWith('WO-1')
    expect(wrapper.text()).toContain('确认下达工单')
    expect(wrapper.get('[data-testid="release-retroactive-notice"]').text()).toContain(
      '该工单已有工序不在排队中。',
    )
    expect(wrapper.find('[data-testid="release-validation-message"]').exists()).toBe(false)

    await wrapper.get('input[type="checkbox"]').setValue(true)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(releaseWorkOrder).toHaveBeenCalledTimes(1)
    expect(releaseWorkOrder).toHaveBeenCalledWith(
      'WO-1',
      expect.objectContaining({ confirmWarnings: true }),
    )
  })

  // 全部工序仍在排队时不该背上这句前提说明。
  it('does not show the not-all-queued notice when every operation is still queued', async () => {
    const wrapper = mountPage()

    await button(wrapper, '下达').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('确认下达工单')
    expect(wrapper.find('[data-testid="release-retroactive-notice"]').exists()).toBe(false)
  })

  it.each([
    ['无管理权限', ['business.mes.work-orders.read'], {}, true],
    [
      '管理范围未就绪',
      ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
      {},
      false,
    ],
    [
      '状态不允许',
      ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
      { status: 'released' },
      true,
    ],
    [
      '缺少生产版本',
      ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
      { productionVersionId: null },
      true,
    ],
    [
      '缺少工序任务',
      ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
      { operationTasks: [] },
      true,
    ],
    [
      '存在有效质量保留',
      ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
      { hasActiveQualityHold: true },
      true,
    ],
  ])('blocks the request when %s', async (_name, permissions, overrides, scopeReady) => {
    releaseState.scopeReady = scopeReady as boolean
    releaseState.scopeMessage = scopeReady ? '' : '尚未选择主体授权工单范围'
    releaseState.items = [workOrder(overrides as Record<string, unknown>)]
    const wrapper = mountPage(permissions as string[])

    const action = button(wrapper, '下达')
    expect(action.attributes('disabled')).toBeDefined()
    await action.trigger('click')
    await flushPromises()

    expect(releaseWorkOrder).not.toHaveBeenCalled()
  })

  it('requires an explicit warning acknowledgement before sending', async () => {
    const wrapper = mountPage()

    await button(wrapper, '下达工单').trigger('click')
    await flushPromises()
    const submit = button(wrapper, '确认下达')
    expect(submit.attributes('disabled')).toBeDefined()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(releaseWorkOrder).not.toHaveBeenCalled()
  })

  it('keeps the server reason and reuses the same idempotency key for retry', async () => {
    const serverError = new Error('质量方案缺失：QUALITY_PLAN_MISSING')
    releaseWorkOrder
      .mockRejectedValueOnce(serverError)
      .mockResolvedValueOnce({ data: { accepted: true, downstreamDocumentId: 'WO-1' } })
    const wrapper = mountPage()

    await button(wrapper, '下达工单').trigger('click')
    await flushPromises()
    await wrapper.get('input[type="checkbox"]').setValue(true)
    const submit = button(wrapper, '确认下达')
    expect(submit.attributes('disabled')).toBeUndefined()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '工单下达失败',
      serverError,
      expect.any(String),
    )
    expect(wrapper.text()).toContain('确认下达工单')

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(releaseWorkOrder).toHaveBeenCalledTimes(2)
    expect(releaseWorkOrder.mock.calls[1]?.[1]?.idempotencyKey).toBe(
      releaseWorkOrder.mock.calls[0]?.[1]?.idempotencyKey,
    )
  })

  it('reports the current preflight failure instead of a previous mutation error', async () => {
    const previousMutationError = new Error('上一次 POST 失败')
    const currentPreflightError = new Error('当前管理范围已失效')
    releaseWorkOrderErrorState.value = previousMutationError
    releaseWorkOrder
      .mockRejectedValueOnce(previousMutationError)
      .mockRejectedValueOnce(currentPreflightError)
    const wrapper = mountPage()

    await button(wrapper, '下达工单').trigger('click')
    await flushPromises()
    await wrapper.get('input[type="checkbox"]').setValue(true)
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    notifyOperationFailure.mockClear()

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '工单下达失败',
      currentPreflightError,
      expect.any(String),
    )
  })
})
