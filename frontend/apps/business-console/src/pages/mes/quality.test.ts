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
  writeScopeReady: true,
  writeScopeMessage: '',
}))
const recordDefect = vi.hoisted(() => vi.fn())
const refreshOperationTasks = vi.hoisted(() => vi.fn())
const refreshQualityItems = vi.hoisted(() => vi.fn())
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
    selectedScope: computed(() =>
      state.writeScopeReady
        ? { kind: 'work-center', id: 'WC-WRITE', displayName: '质量责任区' }
        : undefined,
    ),
    scopeMessage: computed(() => state.writeScopeMessage),
    scopePending: shallowRef(false),
    scopeReady: computed(() => state.writeScopeReady),
  }),
  useMesOperationTasks: () => ({
    filters: reactive({ organizationId: 'org-1', environmentId: 'prod', skip: 0, take: 20 }),
    operationTasks: computed(() => state.operationTasks),
    operationTasksError: shallowRef(undefined),
    operationTasksPending: shallowRef(false),
    operationListScopeMessage: computed(() => ''),
    operationListScopeReady: computed(() => true),
    refreshOperationTasks,
  }),
  useMesRelatedQualityItems: () => ({
    filters: reactive({ organizationId: 'org-1', environmentId: 'prod', skip: 0, take: 20 }),
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
  NvDialog: {
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

function mountPage(
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
  return mount(QualityPage, {
    global: { plugins: [pinia], stubs: uiStubs },
  })
}

function button(wrapper: VueWrapper, label: string) {
  const target = wrapper.findAll('button').find((item) => item.text().includes(label))
  if (!target) throw new Error(`未找到按钮：${label}`)
  return target
}

async function fillValidForm(wrapper: VueWrapper, operationTaskId = 'OP-2') {
  await wrapper.get('[aria-label="工单与工序"]').setValue(operationTaskId)
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
    state.writeScopeReady = true
    state.writeScopeMessage = ''
    vi.clearAllMocks()
    coversWorkOrder.mockImplementation(() => true)
    makeIdempotencyKey.mockReturnValue('defect-intent-key')
    refreshOperationTasks.mockResolvedValue(undefined)
    refreshQualityItems.mockResolvedValue(undefined)
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
    await fillValidForm(wrapper, 'OP-2')
    await submitForm(wrapper)
    await flushPromises()

    expect(coversWorkOrder).toHaveBeenCalledWith(
      { operationTasks: [expect.objectContaining({ operationTaskId: 'OP-2' })] },
      { kind: 'work-center', id: 'WC-WRITE', displayName: '质量责任区' },
    )
    expect(recordDefect).toHaveBeenCalledWith({
      organizationId: 'org-1',
      environmentId: 'prod',
      workOrderId: 'WO-2',
      operationTaskId: 'OP-2',
      defectCode: 'SCRATCH',
      defectQuantity: 2.5,
      materialLotId: null,
      batchOrSerial: null,
      idempotencyKey: 'defect-intent-key',
    })
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('actor')
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('scopeKind')
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('scopeId')
    expect(recordDefect.mock.calls[0]?.[0]).not.toHaveProperty('recordedAtUtc')
    expect(refreshOperationTasks).toHaveBeenCalledTimes(2)
    expect(refreshQualityItems).toHaveBeenCalledTimes(1)
    expect(notifySuccess).toHaveBeenCalledWith('缺陷 DEF-20260825-001 已登记。')
  })

  it('fails closed for a missing defect code or non-positive quantity', async () => {
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await wrapper.get('[aria-label="工单与工序"]').setValue('OP-2')
    await wrapper.get('[aria-label="缺陷数量"]').setValue('0')
    await submitForm(wrapper)

    expect(wrapper.text()).toContain('请完整填写工单与工序、缺陷码和大于 0 的缺陷数量')
    expect(refreshOperationTasks).not.toHaveBeenCalled()
    expect(recordDefect).not.toHaveBeenCalled()
  })

  it('does not mutate when the latest preflight no longer contains the selected context', async () => {
    refreshOperationTasks.mockImplementationOnce(async () => {
      state.operationTasks.splice(0, state.operationTasks.length, operationTask())
    })
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'OP-2')
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
    refreshOperationTasks.mockImplementationOnce(async () => {
      const selected = state.operationTasks.find((task) => task.operationTaskId === 'OP-2')
      if (selected) selected.workCenterId = 'WC-OUT'
    })
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'OP-2')
    await submitForm(wrapper)
    await flushPromises()

    expect(recordDefect).not.toHaveBeenCalled()
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '缺陷登记前置检查失败',
      expect.any(Error),
      expect.stringContaining('当前主体可见范围'),
    )
  })

  it('gates missing permission and a write scope that does not cover the selected operation', async () => {
    const withoutPermission = mountPage([
      'business.mes.quality.read',
      'business.mes.operations.read',
    ])
    expect(button(withoutPermission, '登记缺陷').attributes('disabled')).toBeDefined()

    coversWorkOrder.mockImplementation(({ operationTasks }) =>
      operationTasks.some((task: Record<string, unknown>) => task.operationTaskId === 'OP-2'),
    )
    const scoped = mountPage()
    await button(scoped, '登记缺陷').trigger('click')
    const options = scoped.get('[aria-label="工单与工序"]').findAll('option')
    expect(options.map((option) => option.attributes('value'))).not.toContain('OP-1')
    expect(options.map((option) => option.attributes('value'))).toContain('OP-2')
  })

  it('keeps the same idempotency key and diagnostic error across an unchanged retry', async () => {
    const rejection = { message: '当前工序已关闭，不能登记缺陷', status: 409 }
    recordDefect.mockRejectedValueOnce(rejection)
    const wrapper = mountPage()
    await button(wrapper, '登记缺陷').trigger('click')
    await fillValidForm(wrapper, 'OP-2')

    await submitForm(wrapper)
    await flushPromises()
    expect(notifyOperationFailure).toHaveBeenCalledWith(
      '缺陷登记失败',
      rejection,
      '缺陷登记失败，请根据服务端原因检查后重试。',
    )
    expect(wrapper.text()).toContain('登记生产过程缺陷')

    await submitForm(wrapper)
    await flushPromises()

    expect(recordDefect).toHaveBeenCalledTimes(2)
    expect(recordDefect.mock.calls[0]?.[0].idempotencyKey).toBe('defect-intent-key')
    expect(recordDefect.mock.calls[1]?.[0].idempotencyKey).toBe('defect-intent-key')
    expect(makeIdempotencyKey).toHaveBeenCalledTimes(1)
  })
})
