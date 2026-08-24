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
      {
        code: 'FIXTURE-002',
        name: '左门槛总成焊接夹具',
        toolingType: 'fixture',
        status: 'maintenance',
        maintenanceLifeCount: 15000,
        usageCount: 12640,
        isSchedulable: false,
        workCenterCodes: ['WC-PRESS'],
        skuCodes: ['SKU-SILL'],
      },
      {
        code: 'GAUGE-003',
        name: '左前门内板检具',
        toolingType: 'gauge',
        status: 'maintenance',
        maintenanceLifeCount: 30000,
        usageCount: 30000,
        isSchedulable: false,
        workCenterCodes: ['WC-PRESS'],
        skuCodes: ['SKU-FLOOR'],
      },
      {
        code: 'CUTTER-004',
        name: '侧围冲孔刀具',
        toolingType: 'cutting-tool',
        status: 'available',
        maintenanceLifeCount: 10000,
        usageCount: 10000,
        isSchedulable: true,
        workCenterCodes: ['WC-PRESS'],
        skuCodes: ['SKU-SILL'],
      },
    ]),
    toolingTotal: computed(() => 4),
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
  NvSheet: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  DialogRoot: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvSheetContent: { template: '<section><slot /></section>' },
  NvSheetHeader: { template: '<header><slot /></header>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetFooter: { template: '<footer><slot /></footer>' },
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvAlertDialogContent: {
    template: '<section data-testid="retire-alert"><slot /></section>',
  },
  NvAlertDialogHeader: { template: '<header><slot /></header>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogFooter: { template: '<footer><slot /></footer>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value=""></option><slot /></select>',
  },
  NvSelectTrigger: {
    props: ['invalid'],
    template: '<span data-testid="select-trigger" :data-invalid="invalid || undefined" />',
  },
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
    const rowActions = wrapper.find('[data-testid="row-actions"]')
    expect(rowActions.exists()).toBe(true)
    expect(rowActions.text()).toContain('转保养')
    expect(rowActions.text()).toContain('退役')
    expect(wrapper.text()).not.toContain('适用工作中心')
    const codeButton = button(wrapper, 'MOULD-001')
    expect(codeButton).toBeTruthy()
    await codeButton!.trigger('click')
    expect(wrapper.text()).toContain('适用工作中心')
    expect(wrapper.text()).toContain('WC-PRESS')
    expect(button(wrapper, '登记使用')).toBeTruthy()
  })

  it('点击注册工装后才打开注册表单', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    expect(wrapper.find('form').exists()).toBe(false)
    await button(wrapper, '注册工装')!.trigger('click')
    const form = wrapper.find('form')
    expect(form.exists()).toBe(true)
    expect(form.attributes('novalidate')).toBeDefined()
  })

  it('未达到寿命时完成保养只要求填写原因', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    const completionButtons = wrapper
      .findAll('button')
      .filter((candidate) => candidate.text().trim() === '完成保养')
    await completionButtons[0]!.trigger('click')

    expect(wrapper.text()).toContain('请说明本次状态变更原因。')
    expect(wrapper.text()).not.toContain('完成保养后将清零累计使用次数，并恢复为可用状态。')
  })

  it('达到寿命时完成保养会在提交前披露累计使用次数清零', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    const completionButtons = wrapper
      .findAll('button')
      .filter((candidate) => candidate.text().trim() === '完成保养')
    await completionButtons[1]!.trigger('click')

    expect(wrapper.text()).toContain('完成保养后将清零累计使用次数，并恢复为可用状态。')
  })

  it('可用工装达到寿命后转保养不会披露完成保养清零', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    const transferButtons = wrapper
      .findAll('button')
      .filter((candidate) => candidate.text().trim() === '转保养')
    await transferButtons[1]!.trigger('click')

    expect(wrapper.text()).toContain('请说明本次状态变更原因。')
    expect(wrapper.text()).not.toContain('完成保养后将清零累计使用次数，并恢复为可用状态。')
  })

  it('注册提交后同时展示校验汇总与对应字段错误', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()
    await button(wrapper, '注册工装')!.trigger('click')
    await wrapper.find('#tooling-life').setValue('0')
    await wrapper.find('form').trigger('submit')

    expect(state.register).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请修正已标红的字段，并完整填写带 * 的必填项')
    expect(wrapper.text()).toContain('请填写工装名称。')
    expect(wrapper.text()).toContain('请选择工装类型。')
    expect(wrapper.text()).toContain('使用寿命必须是正整数。')
    expect(wrapper.text()).toContain('请至少选择一个适用工作中心。')
    expect(wrapper.text()).toContain('请至少选择一个适用 SKU。')
    for (const selector of ['#tooling-name', '#tooling-life']) {
      const input = wrapper.find(selector)
      expect(input.element.parentElement?.getAttribute('data-invalid')).toBe('true')
      expect(input.element.closest('[data-slot="nv-field"]')?.getAttribute('data-invalid')).toBe(
        'true',
      )
    }
    expect(wrapper.find('label[for="tooling-name"] > span').classes()).toContain('text-destructive')
    expect(wrapper.find('form [data-testid="select-trigger"]').attributes('data-invalid')).toBe(
      'true',
    )

    for (const errorText of ['请至少选择一个适用工作中心。', '请至少选择一个适用 SKU。']) {
      const error = wrapper.findAll('[role="alert"]').find((item) => item.text() === errorText)!
      const field = error.element.closest('[data-slot="nv-field"]')
      expect(field?.getAttribute('data-invalid')).toBeNull()
      expect(field?.getAttribute('aria-invalid')).toBe('true')
      expect(field?.querySelector('[data-invalid="true"]')).not.toBeNull()
    }
  })

  it('注册校验工作中心、SKU 与正整数寿命，合法时显示组合数并提交真实编码', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()
    await button(wrapper, '注册工装')!.trigger('click')
    await wrapper.find('#tooling-name').setValue('前地板拉延模')
    await wrapper.find('form select').setValue('mould')
    await wrapper.find('#tooling-life').setValue('0')
    await wrapper.find('form').trigger('submit')
    expect(state.register).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('使用寿命必须是正整数')

    await wrapper.find('#tooling-life').setValue('80000')
    const checks = wrapper.findAll('input[type="checkbox"]')
    await checks[0]!.setValue(true)
    await checks[1]!.setValue(true)
    expect(wrapper.text()).toContain('1 个适用组合')
    expect(wrapper.text()).toContain('工作中心目录共 201 项，当前候选加载上限为 200 项')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

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
      await wrapper.find('form select').setValue('mould')
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
    const statusReason = wrapper.find('#tooling-status-reason')
    expect(statusReason.element.parentElement?.getAttribute('data-invalid')).toBe('true')
    expect(
      statusReason.element.closest('[data-slot="nv-field"]')?.getAttribute('data-invalid'),
    ).toBe('true')
    expect(wrapper.find('label[for="tooling-status-reason"] > span').classes()).toContain(
      'text-destructive',
    )
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
    const usageInput = wrapper.find('#tooling-usage-count')
    expect(usageInput.element.parentElement?.getAttribute('data-invalid')).toBe('true')
    expect(usageInput.element.closest('[data-slot="nv-field"]')?.getAttribute('data-invalid')).toBe(
      'true',
    )
    expect(wrapper.find('label[for="tooling-usage-count"] > span').classes()).toContain(
      'text-destructive',
    )

    await wrapper.find('#tooling-usage-count').setValue('800')
    expect(wrapper.text()).toContain('保存后工装将自动转为保养中，并停止参与排程')
    await button(wrapper, '确认登记')!.trigger('click')
    await flushPromises()
    expect(state.recordUsage).toHaveBeenCalledWith('MOULD-001', 800)
  })

  it('退役使用破坏性确认，原因纯空白时不可确认', async () => {
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    await button(wrapper, '退役')!.trigger('click')
    expect(wrapper.find('[data-testid="retire-alert"]').exists()).toBe(true)
    const confirmRetire = button(wrapper, '确认退役')!
    expect(confirmRetire.attributes('disabled')).toBeDefined()
    expect(confirmRetire.classes()).toContain('bg-destructive')

    await wrapper.find('#tooling-retire-reason').setValue('  ')
    expect(confirmRetire.attributes('disabled')).toBeDefined()
    await wrapper.find('#tooling-retire-reason').setValue('达到报废标准')
    expect(confirmRetire.attributes('disabled')).toBeUndefined()
    await confirmRetire.trigger('click')
    await flushPromises()
    expect(state.changeStatus).toHaveBeenCalledWith('MOULD-001', 'retired', '达到报废标准')
  })

  it('退役请求失败后保留确认框与已填写原因', async () => {
    state.changeStatus.mockRejectedValueOnce(new Error('暂时不可用'))
    const wrapper = mount(ToolingPage, { global: { stubs } })
    await flushPromises()

    await button(wrapper, '退役')!.trigger('click')
    await wrapper.find('#tooling-retire-reason').setValue('达到报废标准')
    await button(wrapper, '确认退役')!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="retire-alert"]').exists()).toBe(true)
    expect((wrapper.find('#tooling-retire-reason').element as HTMLInputElement).value).toBe(
      '达到报废标准',
    )
    expect(state.toastError).toHaveBeenCalled()
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
