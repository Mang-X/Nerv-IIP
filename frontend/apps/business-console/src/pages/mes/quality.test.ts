import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, reactive, ref, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import QualityPage from './quality.vue'

const state = vi.hoisted(() => ({
  operationTasks: [] as Array<Record<string, unknown>>,
  qualityItems: [] as Array<Record<string, unknown>>,
  routeQuery: {} as Record<string, string>,
  organizationId: 'org-1',
  environmentId: 'prod',
  writeScopeReady: true,
  writeScopeId: 'WC-WRITE',
  writeScopeMessage: '',
}))
const recordDefect = vi.hoisted(() => vi.fn())
const refreshOperationTasks = vi.hoisted(() => vi.fn())
const refreshQualityItems = vi.hoisted(() => vi.fn())
const refreshQualityWriteScope = vi.hoisted(() => vi.fn())
const coversWorkOrder = vi.hoisted(() =>
  vi.fn(
    (
      _candidate: { operationTasks: Array<Record<string, unknown>> },
      _scope: Record<string, unknown>,
    ) => true,
  ),
)
const makeIdempotencyKey = vi.hoisted(() => vi.fn(() => 'defect-intent-key'))
const notifyOperationFailure = vi.hoisted(() => vi.fn())
const notifySuccess = vi.hoisted(() => vi.fn())

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: state.routeQuery }),
  useRouter: () => ({ push: vi.fn() }),
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: () => '',
  notifyOperationFailure,
  notifySuccess,
}))

vi.mock('@/composables/usePromotedCatalogs', () => ({
  useQualityReasonCodes: () => ({
    reasons: computed(() => [
      { reasonCode: 'DIMENSION', reasonName: '尺寸超差', enabled: true },
      { reasonCode: 'SCRATCH', reasonName: '表面划伤', enabled: true },
    ]),
    reasonsPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref('20') }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey,
  mesWorkScopeKindLabel: (kind: string) => (kind === 'work-center' ? '工作中心' : kind),
  useMesWorkScopeSelection: () => ({
    coversWorkOrder,
    selectedScope: {
      get value() {
        return state.writeScopeReady
          ? { kind: 'work-center', id: state.writeScopeId, displayName: '质量责任区' }
          : undefined
      },
    },
    refreshScope: refreshQualityWriteScope,
    scopeMessage: computed(() => state.writeScopeMessage),
    scopePending: shallowRef(false),
    scopeReady: computed(() => state.writeScopeReady),
  }),
  useMesOperationTasks: () => {
    const operationTasks = shallowRef([...state.operationTasks])
    return {
      filters: reactive({
        get organizationId() {
          return state.organizationId
        },
        get environmentId() {
          return state.environmentId
        },
        skip: 0,
        take: 20,
      }),
      operationTasks,
      operationTasksError: shallowRef(undefined),
      operationTasksPending: shallowRef(false),
      operationListScopeMessage: computed(() => ''),
      operationListScopeReady: computed(() => true),
      refreshOperationTasks: async () => {
        const result = await refreshOperationTasks()
        operationTasks.value = [...state.operationTasks]
        return result
      },
    }
  },
  useMesRelatedQualityItems: () => ({
    filters: reactive({
      get organizationId() {
        return state.organizationId
      },
      get environmentId() {
        return state.environmentId
      },
      skip: 0,
      take: 20,
    }),
    qualityItems: computed(() => state.qualityItems),
    qualityItemsError: shallowRef(undefined),
    qualityItemsPending: shallowRef(false),
    qualityItemsTotal: computed(() => state.qualityItems.length),
    recordDefect,
    recordDefectPending: shallowRef(false),
    refreshQualityItems,
  }),
}))

const passthrough = { template: '<div><slot /></div>' }
const uiStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvPageHeader: { template: '<header><slot name="actions" /></header>' },
  NvMetricCard: true,
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvDataTable: true,
  NvStatusBadge: true,
  NvSelect: passthrough,
  NvSelectTrigger: passthrough,
  NvSelectValue: true,
  SelectValue: true,
  NvSelectContent: passthrough,
  NvSelectItem: passthrough,
  // NvDialog 是 reka-ui DialogRoot 的重命名再导出（frontend/packages/ui/src/components/pc/dialog/index.ts），
  // <script setup> 编译期直接绑定导入标识符，不经过按名解析；stub 必须按其真实组件名 DialogRoot 匹配，
  // 否则真实 DialogRoot（内容始终无条件渲染 slot）会绕过本 stub，dialog 的开合状态在测试里恒为"已渲染"。
  DialogRoot: {
    props: ['open'],
    emits: ['update:open'],
    template: '<div v-if="open"><slot /></div>',
  },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvFieldGroup: passthrough,
  NvField: passthrough,
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvEntityPicker: {
    inheritAttrs: false,
    props: ['modelValue', 'options', 'disabled'],
    emits: ['update:modelValue'],
    template: `
      <select
        v-bind="$attrs"
        :value="modelValue"
        :disabled="disabled"
        @change="$emit('update:modelValue', $event.target.value)"
      >
        <option value="">请选择</option>
        <option v-for="option in options" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
    `,
  },
  NvInput: {
    inheritAttrs: false,
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvButton: {
    inheritAttrs: false,
    props: ['disabled', 'type'],
    template:
      '<button v-bind="$attrs" :type="type || \'button\'" :disabled="disabled"><slot /></button>',
  },
  Spinner: true,
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

function operationTask(overrides: Record<string, unknown> = {}) {
  return {
    operationTaskId: 'OP-1',
    operationTaskNo: 'OP-20260825-001',
    workOrderId: 'WO-1',
    workOrderNo: 'WO-20260825-001',
    operationSequence: 10,
    workCenterId: 'WC-1',
    workCenterName: '精加工一线',
    status: 'started',
    ...overrides,
  }
}

function mountPageWithAuth(
  permissionCodes = [
    'business.mes.quality.read',
    'business.mes.quality.write',
    'business.mes.operations.read',
  ],
) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'qa-1',
      principalType: 'user',
      organizationId: 'org-1',
      environmentId: 'prod',
      loginName: 'quality.engineer',
      permissionCodes,
    },
  })
  const wrapper = mount(QualityPage, {
    global: { plugins: [pinia], stubs: uiStubs },
  })
  return { wrapper, auth }
}

function mountPage(permissionCodes?: string[]) {
  return mountPageWithAuth(permissionCodes).wrapper
}

function button(wrapper: VueWrapper, label: string) {
  const target = wrapper.findAll('button').find((item) => item.text().includes(label))
  if (!target) throw new Error(`未找到按钮：${label}`)
  return target
}

async function fillValidForm(wrapper: VueWrapper, targetKey = 'operation:OP-2') {
  await wrapper.get('[aria-label="工单与工序"]').setValue(targetKey)
  await wrapper.get('[aria-label="缺陷码"]').setValue('SCRATCH')
  await wrapper.get('[aria-label="缺陷数量"]').setValue('2.5')
}

async function submitForm(wrapper: VueWrapper) {
  await wrapper.get('form').trigger('submit')
}

describe('MES 质量页 — 缺陷登记入口', () => {
  beforeEach(() => {
    state.operationTasks = [
      operationTask(),
      operationTask({
        operationTaskId: 'OP-2',
        operationTaskNo: 'OP-20260825-002',
        workOrderId: 'WO-2',
        workOrderNo: 'WO-20260825-002',
        operationSequence: 20,
        workCenterId: 'WC-2',
        workCenterName: '装配二线',
      }),
    ]
    state.qualityItems = []
    state.routeQuery = {}
    state.organizationId = 'org-1'
    state.environmentId = 'prod'
    state.writeScopeReady = true
    state.writeScopeId = 'WC-WRITE'
    state.writeScopeMessage = ''
    vi.clearAllMocks()
    coversWorkOrder.mockImplementation(() => true)
    makeIdempotencyKey.mockReturnValue('defect-intent-key')
    refreshOperationTasks.mockResolvedValue(undefined)
    refreshQualityItems.mockResolvedValue(undefined)
    refreshQualityWriteScope.mockResolvedValue(undefined)
    recordDefect.mockResolvedValue({
      success: true,
      data: {
        accepted: true,
        downstreamService: 'BusinessMes',
        downstreamDocumentType: 'Defect',
        downstreamDocumentId: 'DEF-20260825-001',
      },
    })
  })

  it('submits the selected real operation context and refreshes both related read models', async () => {
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'operation:OP-2')
    await submitForm(wrapper)
    await flushPromises()

    expect(coversWorkOrder).toHaveBeenCalledWith(
      { operationTasks: [expect.objectContaining({ operationTaskId: 'OP-2' })] },
      { kind: 'work-center', id: 'WC-WRITE', displayName: '质量责任区' },
    )
    expect(recordDefect).toHaveBeenCalledWith({
      workOrderId: 'WO-2',
      operationTaskId: 'OP-2',
      defectCode: 'SCRATCH',
      quantity: 2.5,
      recordedAtUtc: expect.stringMatching(/^\d{4}-\d{2}-\d{2}T/),
      idempotencyKey: 'defect-intent-key',
      scopeKind: 'work-center',
      scopeId: 'WC-WRITE',
    })
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('actor')
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('organizationId')
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('environmentId')
    expect(refreshQualityWriteScope).toHaveBeenCalledTimes(1)
    expect(refreshOperationTasks).toHaveBeenCalledTimes(2)
    expect(refreshQualityItems).toHaveBeenCalledTimes(1)
    expect(notifySuccess).toHaveBeenCalledWith('缺陷 DEF-20260825-001 已登记。')
  })

  it('submits a real work-order context without inventing an operation task', async () => {
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'work-order:WO-2')
    await submitForm(wrapper)
    await flushPromises()

    expect(recordDefect).toHaveBeenCalledWith(
      expect.objectContaining({
        workOrderId: 'WO-2',
        defectCode: 'SCRATCH',
        quantity: 2.5,
        scopeKind: 'work-center',
        scopeId: 'WC-WRITE',
      }),
    )
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('operationTaskId')
  })

  it('fails closed for a missing defect code or non-positive quantity', async () => {
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await wrapper.get('[aria-label="工单与工序"]').setValue('operation:OP-2')
    await wrapper.get('[aria-label="缺陷数量"]').setValue('0')
    await submitForm(wrapper)

    expect(wrapper.text()).toContain('请完整填写工单上下文、缺陷码和大于 0 的缺陷数量')
    expect(refreshOperationTasks).not.toHaveBeenCalled()
    expect(recordDefect).not.toHaveBeenCalled()
  })

  it('does not mutate when the latest preflight no longer contains the selected context', async () => {
    const wrapper = mountPage()
    await flushPromises()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'operation:OP-2')
    refreshOperationTasks.mockReset()
    refreshOperationTasks.mockImplementation(async () => {
      state.operationTasks.splice(0, state.operationTasks.length, operationTask())
    })
    await submitForm(wrapper)
    await flushPromises()

    expect(recordDefect).not.toHaveBeenCalled()
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '缺陷登记前置检查失败',
      expect.any(Error),
      expect.stringContaining('当前主体可见范围'),
    )
  })

  it('does not mutate when the latest task is no longer covered by the quality write scope', async () => {
    coversWorkOrder.mockImplementation(({ operationTasks }) =>
      operationTasks.every((task) => task.workCenterId !== 'WC-OUT'),
    )
    const wrapper = mountPage()
    await flushPromises()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'operation:OP-2')
    refreshOperationTasks.mockReset()
    refreshOperationTasks.mockImplementation(async () => {
      const selected = state.operationTasks.find((task) => task.operationTaskId === 'OP-2')
      if (selected) selected.workCenterId = 'WC-OUT'
    })
    await submitForm(wrapper)
    await flushPromises()

    expect(recordDefect).not.toHaveBeenCalled()
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '缺陷登记前置检查失败',
      expect.any(Error),
      expect.stringContaining('当前主体可见范围'),
    )
  })

  it('does not mutate when the refreshed quality write context no longer covers the task', async () => {
    state.writeScopeId = 'WC-2'
    coversWorkOrder.mockImplementation(({ operationTasks }, scope) =>
      operationTasks.some((task) => task.workCenterId === scope.id),
    )
    const wrapper = mountPage()
    await flushPromises()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'operation:OP-2')
    refreshQualityWriteScope.mockReset()
    refreshQualityWriteScope.mockImplementation(async () => {
      state.writeScopeId = 'WC-REVOKED'
    })
    await submitForm(wrapper)
    await flushPromises()

    expect(refreshQualityWriteScope).toHaveBeenCalledTimes(1)
    expect(recordDefect).not.toHaveBeenCalled()
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '缺陷登记前置检查失败',
      expect.any(Error),
      expect.stringContaining('当前主体可见范围'),
    )
  })

  it('does not open the defect dialog when the entry guard trips between render and click (isolated stale-DOM interleave)', async () => {
    // 以确定性交错模拟 stale-DOM 时序（同族手法见 downtime/handovers 同名用例）：按钮渲染时守卫
    // 未拦截，权限在同一 tick 内失效——DOM 的 disabled 属性还没来得及重渲染，trigger() 读到的
    // 仍是旧值，点击得以派发，从而真正跑进 openDefectDialog 内部的
    // `if (defectEntryBlocker.value) return`。不能证明项：本用例只证明「blocker 为真时该行会
    // 早返回」，不证明真实浏览器点击与 Vue microtask flush 之间确有这个时序窗口——那属于
    // ProviderBehavior，本 lane（jsdom + vue-test-utils）证不到。
    // principal 由 shallowRef 承载（frontend/packages/auth/src/store.ts），只有对 principal 本身
    // 重新赋值才会被追踪；改嵌套字段（如 permissionCodes）不会触发依赖更新，必须整体替换。
    // 断言取对话框内容是否挂载而非 recordDefect 是否被调用：defectTargets/eligibleOperationTasks
    // 对权限有独立判空，缺权限时无论 openDefectDialog 内部守卫在不在，目标列表都会先一步清空，
    // 用「表单是否有值」无法专门证伪这一行；对话框开合状态才是这条守卫独有、不与其它防线重叠的信号。
    const { wrapper, auth } = mountPageWithAuth()
    const btn = button(wrapper, '登记缺陷')
    expect(btn.attributes('disabled')).toBeUndefined()

    auth.principal = { ...auth.principal!, permissionCodes: [] }
    await btn.trigger('click')

    expect(wrapper.find('[aria-label="工单与工序"]').exists()).toBe(false)
    expect(recordDefect).not.toHaveBeenCalled()
  })

  it('gates missing permission and a write scope that does not cover the selected operation', async () => {
    const withoutPermission = mountPage([
      'business.mes.quality.read',
      'business.mes.operations.read',
    ])
    expect(button(withoutPermission, '登记缺陷').attributes('disabled')).toBeDefined()
    // 此刻组织/环境/写入范围/工序全部就绪，唯独缺 quality.write：eligibleOperationTasks
    // 内部同样判空这条权限，故仅凭 disabled 状态抓不住 defectEntryBlocker 里
    // `!canWriteQuality.value` 这第一条早返回——删掉它后 blocker 会落到
    // eligibleOperationTasks.length === 0 分支，给出另一句文案，disabled 依旧为真。
    // 精确文案断言能抓住这条差异。
    expect(button(withoutPermission, '登记缺陷').attributes('title')).toBe('没有缺陷登记权限')

    coversWorkOrder.mockImplementation(({ operationTasks }) =>
      operationTasks.some((task: Record<string, unknown>) => task.operationTaskId === 'OP-2'),
    )
    const scoped = mountPage()
    await button(scoped, '登记缺陷').trigger('click')
    const options = scoped.get('[aria-label="工单与工序"]').findAll('option')
    expect(options.map((option) => option.attributes('value'))).not.toContain('operation:OP-1')
    expect(options.map((option) => option.attributes('value'))).toContain('operation:OP-2')
  })

  it('replays the same complete payload after the server accepted but the response was lost', async () => {
    vi.useFakeTimers()
    try {
      vi.setSystemTime(new Date('2026-08-26T01:02:03.000Z'))
      const rejection = { message: '响应丢失', status: 0 }
      recordDefect.mockRejectedValueOnce(rejection)
      const wrapper = mountPage()
      await button(wrapper, '登记缺陷').trigger('click')
      await fillValidForm(wrapper, 'operation:OP-2')

      await submitForm(wrapper)
      await flushPromises()
      expect(notifyOperationFailure).toHaveBeenCalledWith(
        '缺陷登记失败',
        rejection,
        '缺陷登记失败，请根据服务端原因检查后重试。',
      )
      expect(wrapper.text()).toContain('登记生产过程缺陷')
      const firstPayload = structuredClone(recordDefect.mock.calls[0]?.[0])

      vi.setSystemTime(new Date('2026-08-26T02:02:03.000Z'))
      await submitForm(wrapper)
      await flushPromises()

      expect(recordDefect).toHaveBeenCalledTimes(2)
      expect(recordDefect.mock.calls[1]?.[0]).toEqual(firstPayload)
      expect(firstPayload.recordedAtUtc).toBe('2026-08-26T01:02:03.000Z')
      expect(firstPayload.idempotencyKey).toBe('defect-intent-key')
      expect(makeIdempotencyKey).toHaveBeenCalledTimes(1)
    } finally {
      vi.useRealTimers()
    }
  })

  it('starts a new frozen intent when Business Context changes after a lost response', async () => {
    vi.useFakeTimers()
    try {
      makeIdempotencyKey
        .mockReturnValueOnce('defect-intent-org-a')
        .mockReturnValueOnce('defect-intent-org-b')
      vi.setSystemTime(new Date('2026-08-26T01:02:03.000Z'))
      recordDefect.mockRejectedValueOnce({ message: '响应丢失', status: 0 })
      const wrapper = mountPage()
      await button(wrapper, '登记缺陷').trigger('click')
      await fillValidForm(wrapper, 'operation:OP-2')

      await submitForm(wrapper)
      await flushPromises()

      state.organizationId = 'org-2'
      state.environmentId = 'staging'
      vi.setSystemTime(new Date('2026-08-26T02:02:03.000Z'))
      await submitForm(wrapper)
      await flushPromises()

      expect(recordDefect).toHaveBeenCalledTimes(2)
      expect(recordDefect.mock.calls[0]?.[0]).toMatchObject({
        idempotencyKey: 'defect-intent-org-a',
        recordedAtUtc: '2026-08-26T01:02:03.000Z',
      })
      expect(recordDefect.mock.calls[1]?.[0]).toMatchObject({
        idempotencyKey: 'defect-intent-org-b',
        recordedAtUtc: '2026-08-26T02:02:03.000Z',
      })
      expect(makeIdempotencyKey).toHaveBeenCalledTimes(2)
    } finally {
      vi.useRealTimers()
    }
  })
})
