import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ToolingPage from './tooling.vue'

const state = vi.hoisted(() => ({
  permissionCodes: ['business.masterdata.resources.read', 'business.masterdata.resources.manage'],
  register: vi.fn().mockResolvedValue({}),
  changeStatus: vi.fn().mockResolvedValue({}),
  recordUsage: vi.fn().mockResolvedValue({}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

vi.mock('@/composables/useBusinessTooling', () => ({
  useBusinessTooling: () => ({
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      keyword: '',
      status: undefined,
      skip: 0,
      take: 10,
    }),
    toolingAssets: computed(() => [
      {
        code: 'MOULD-001',
        name: '前地板拉延模',
        toolingType: 'mould',
        status: 'available',
        maintenanceLifeCount: 80000,
        usageCount: 79200,
        isSchedulable: true,
        workCenterCodes: ['WC-PRESS'],
        skuCodes: ['SKU-FLOOR', 'SKU-SILL'],
      },
    ]),
    toolingTotal: computed(() => 1),
    toolingPending: shallowRef(false),
    toolingError: shallowRef(),
    refresh: vi.fn(),
    register: state.register,
    registerPending: shallowRef(false),
    changeStatus: state.changeStatus,
    changeStatusPending: shallowRef(false),
    recordUsage: state.recordUsage,
    recordUsagePending: shallowRef(false),
  }),
  toolingStatusLabel: (value: string) =>
    ({ available: '可用', maintenance: '保养中', retired: '已退役' })[value] ?? value,
  toolingTypeLabel: (value: string) => ({ mould: '模具' })[value] ?? value,
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: (resourceType: string) => ({
    filters: reactive({ take: 200 }),
    resources: computed(() =>
      resourceType === 'work-center'
        ? [{ code: 'WC-PRESS', displayName: '冲压工作中心', active: true }]
        : [
            { code: 'SKU-FLOOR', displayName: '前地板总成', active: true },
            { code: 'SKU-SILL', displayName: '左门槛加强板', active: true },
          ],
    ),
    resourcesTotal: computed(() => (resourceType === 'work-center' ? 201 : 2)),
    resourcesPending: shallowRef(false),
  }),
}))

vi.mock('@nerv-iip/ui', async (original) => ({
  ...(await original<typeof import('@nerv-iip/ui')>()),
  toast: { success: state.toastSuccess, error: state.toastError },
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvSheet: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<section><slot /></section>' },
  NvSheetHeader: { template: '<header><slot /></header>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetFooter: { template: '<footer><slot /></footer>' },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span />' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  NvCheckbox: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input type="checkbox" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  },
  NvFieldError: { props: ['errors'], template: '<p role="alert">{{ errors?.join("；") }}</p>' },
  FormSectionTitle: { template: '<h3><slot /></h3>' },
  NvRowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  RowActions: { template: '<div data-testid="row-actions"><slot /></div>' },
  NvDropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}

function button(wrapper: ReturnType<typeof mount>, label: string) {
  return wrapper.findAll('button').find((candidate) => candidate.text().trim() === label)
}

beforeEach(() => {
  state.permissionCodes = [
    'business.masterdata.resources.read',
    'business.masterdata.resources.manage',
  ]
  vi.clearAllMocks()
  state.register.mockReset().mockResolvedValue({})
  state.changeStatus.mockReset().mockResolvedValue({})
  state.recordUsage.mockReset().mockResolvedValue({})
})

describe('工装与模具维护台', () => {
  it('以中文业务语言展示寿命、预警、排程资格与适用范围', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    expect(wrapper.text()).toContain('工装与模具')
    expect(wrapper.text()).toContain('前地板拉延模')
    expect(wrapper.text()).toContain('模具')
    expect(wrapper.text()).toContain('即将达寿命')
    expect(wrapper.text()).toContain('可参与排程')
    expect(wrapper.text()).toContain('1 个工作中心 · 2 个 SKU')
  })

  it('以编码打开详情，并将低频状态动作收纳到行操作菜单', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    expect(button(wrapper, '查看')).toBeUndefined()
    expect(wrapper.find('[data-testid="row-actions"]').exists()).toBe(true)
    const codeButton = button(wrapper, 'MOULD-001')
    expect(codeButton).toBeTruthy()
    await codeButton!.trigger('click')
    expect(wrapper.text()).toContain('前地板拉延模')
    expect(button(wrapper, '登记使用')).toBeTruthy()
    expect(button(wrapper, '转保养')).toBeTruthy()
    expect(button(wrapper, '退役')).toBeTruthy()
  })

  it('注册提交后同时展示校验汇总与对应字段错误', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()
    await button(wrapper, '注册工装')!.trigger('click')
    await wrapper.find('form').trigger('submit')

    expect(state.register).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请检查以下必填项')
    expect(wrapper.text()).toContain('请填写工装名称。')
    expect(wrapper.text()).toContain('请至少选择一个适用工作中心。')
    expect(wrapper.text()).toContain('请至少选择一个适用 SKU。')
  })

  it('注册校验工作中心、SKU 与正整数寿命，合法时显示组合数并提交真实编码', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()
    await button(wrapper, '注册工装')!.trigger('click')
    await wrapper.find('#tooling-name').setValue('前地板拉延模')
    await wrapper.find('#tooling-life').setValue('0')
    await wrapper.find('form').trigger('submit')
    expect(state.register).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('使用寿命必须是正整数')

    await wrapper.find('#tooling-life').setValue('80000')
    const checks = wrapper.findAll('input[type="checkbox"]')
    await checks[0]!.setValue(true)
    await checks[1]!.setValue(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('1 个适用组合')
    expect(wrapper.text()).toContain('工作中心目录共 201 项，当前候选加载上限为 200 项')
    expect(state.register).toHaveBeenCalledWith(
      expect.objectContaining({
        idempotencyKey: expect.any(String),
        name: '前地板拉延模',
        toolingType: 'mould',
        maintenanceLifeCount: 80000,
        workCenterCodes: ['WC-PRESS'],
        skuCodes: ['SKU-FLOOR'],
      }),
    )
    expect(state.toastSuccess).toHaveBeenCalled()
  })

  it('注册失败重试复用幂等键，重新打开表单生成新键', async () => {
    state.register.mockRejectedValueOnce(new Error('暂时不可用')).mockResolvedValue({})
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    async function fillRequiredFields() {
      await wrapper.find('#tooling-name').setValue('前地板拉延模')
      const checks = wrapper.findAll('input[type="checkbox"]')
      await checks[0]!.setValue(true)
      await checks[1]!.setValue(true)
    }

    await button(wrapper, '注册工装')!.trigger('click')
    await fillRequiredFields()
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const firstKey = state.register.mock.calls[0]![0].idempotencyKey
    expect(firstKey).toEqual(expect.any(String))
    expect(state.register.mock.calls[1]![0].idempotencyKey).toBe(firstKey)

    await button(wrapper, '注册工装')!.trigger('click')
    await fillRequiredFields()
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(state.register.mock.calls[2]![0].idempotencyKey).not.toBe(firstKey)
  })

  it('状态原因必填、使用次数必须为正整数，并按状态提供动作', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()
    expect(button(wrapper, '转保养')).toBeTruthy()
    expect(button(wrapper, '退役')).toBeTruthy()

    await button(wrapper, '转保养')!.trigger('click')
    await button(wrapper, '确认转保养')!.trigger('click')
    expect(state.changeStatus).not.toHaveBeenCalled()
    await wrapper.find('#tooling-status-reason').setValue('达到规定冲次，安排保养')
    await button(wrapper, '确认转保养')!.trigger('click')
    await flushPromises()
    expect(state.changeStatus).toHaveBeenCalledWith(
      'MOULD-001',
      'maintenance',
      '达到规定冲次，安排保养',
    )

    await button(wrapper, '登记使用')!.trigger('click')
    await wrapper.find('#tooling-usage-count').setValue('0')
    await button(wrapper, '确认登记')!.trigger('click')
    expect(state.recordUsage).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('使用次数必须是正整数。')

    await wrapper.find('#tooling-usage-count').setValue('800')
    expect(wrapper.text()).toContain('保存后工装将自动转为保养中，并停止参与排程')
    await button(wrapper, '确认登记')!.trigger('click')
    await flushPromises()
    expect(state.recordUsage).toHaveBeenCalledWith('MOULD-001', 800)
  })

  it('仅有只读权限时不展示写操作', async () => {
    state.permissionCodes = ['business.masterdata.resources.read']
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()
    expect(button(wrapper, '注册工装')).toBeUndefined()
    expect(button(wrapper, '转保养')).toBeUndefined()
    expect(button(wrapper, '登记使用')).toBeUndefined()
  })
})
