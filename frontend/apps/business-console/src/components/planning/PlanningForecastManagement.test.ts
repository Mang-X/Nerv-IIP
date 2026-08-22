import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, reactive, shallowRef } from 'vue'
import PlanningForecastManagement from './PlanningForecastManagement.vue'
import { useAuthStore } from '@/stores/auth'

const spies = vi.hoisted(() => ({
  saveForecast: vi.fn(async (_form: Record<string, unknown>) => ({ success: true })),
  refreshForecasts: vi.fn(async () => undefined),
  forecastsError: undefined as unknown as { value: unknown },
  success: vi.fn(),
  error: vi.fn(),
  failure: vi.fn(),
}))

vi.mock('@/composables/useBusinessForecasts', () => ({
  useBusinessForecasts: () => {
    spies.forecastsError = shallowRef(null)
    return {
      filters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: '',
        siteCode: '',
        fromDate: '',
        toDate: '',
      }),
      forecasts: shallowRef([
        {
          forecastInputId: 'forecast-1',
          forecastReference: 'FC-2026-09-FG-1000',
          skuCode: 'FG-1000',
          uomCode: 'pcs',
          siteCode: 'SITE-01',
          periodStartDate: '2026-09-01',
          periodEndDate: '2026-09-30',
          quantity: 120,
          backwardConsumptionDays: 7,
          forwardConsumptionDays: 3,
        },
      ]),
      forecastsError: spies.forecastsError,
      forecastsPending: shallowRef(false),
      refreshForecasts: spies.refreshForecasts,
      saveForecast: spies.saveForecast,
      saveForecastPending: shallowRef(false),
    }
  },
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    skus: shallowRef([{ resourceType: 'sku', code: 'FG-1000', displayName: '减振器总成' }]),
  }),
  useBusinessMasterDataResources: () => ({
    resources: shallowRef([
      { resourceType: 'site', code: 'SITE-01', displayName: '上海工厂' },
      { resourceType: 'unit-of-measure', code: 'pcs', displayName: '件' },
    ]),
  }),
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: (error: unknown) => String(error ?? ''),
  serverErrorMessage: (error: unknown) =>
    typeof error === 'object' && error && 'message' in error ? String(error.message) : '',
  notifySuccess: spies.success,
  notifyError: spies.error,
  notifyOperationFailure: spies.failure,
}))

vi.mock('@nerv-iip/ui', () => {
  const Shell = defineComponent({
    template: '<div><slot /><slot name="filters" /><slot name="actions" /></div>',
  })
  const Button = defineComponent({
    props: ['type', 'disabled'],
    emits: ['click'],
    template:
      '<button :type="type || \'button\'" :disabled="disabled" @click="$emit(\'click\', $event)"><slot /></button>',
  })
  const Input = defineComponent({
    name: 'NvInput',
    props: ['modelValue', 'disabled', 'type', 'invalid'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" :disabled="disabled" :type="type || \'text\'" :data-invalid="invalid || undefined" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  })
  const SearchSelect = defineComponent({
    name: 'NvSearchSelect',
    props: ['modelValue', 'options', 'ariaLabel', 'searchPlaceholder'],
    emits: ['update:modelValue'],
    template:
      '<button type="button" :aria-label="ariaLabel" aria-haspopup="listbox" :data-search-placeholder="searchPlaceholder" @click="$emit(\'update:modelValue\', options.find((option) => option.value !== \'all\')?.value || \'\')">{{ options.map((option) => option.label).join("|") }}</button>',
  })
  const DatePicker = defineComponent({
    name: 'NvDatePicker',
    props: ['modelValue', 'id', 'ariaLabel'],
    emits: ['update:modelValue'],
    template:
      '<button type="button" :id="id" :aria-label="ariaLabel" @click="$emit(\'update:modelValue\', id.includes(\'start\') ? \'2026-09-01\' : \'2026-09-30\')">{{ modelValue }}</button>',
  })
  const DataTable = defineComponent({
    props: ['columns', 'rows'],
    setup(props, { slots }) {
      return () =>
        h(
          'div',
          (props.rows ?? []).flatMap((row: Record<string, unknown>) =>
            (props.columns ?? []).map((column: { key: string }) => {
              const slot = slots[`cell-${column.key}`]
              return h('div', slot ? slot({ row }) : String(row[column.key] ?? ''))
            }),
          ),
        )
    },
  })
  return {
    NvButton: Button,
    NvDataTable: DataTable,
    NvDialog: Shell,
    NvDialogContent: Shell,
    NvDialogDescription: Shell,
    NvDialogFooter: Shell,
    NvDialogHeader: Shell,
    NvDialogTitle: Shell,
    NvField: Shell,
    NvFieldError: defineComponent({
      props: ['errors'],
      template: '<p role="alert">{{ errors.join("；") }}</p>',
    }),
    NvFieldGroup: Shell,
    NvFieldLabel: Shell,
    NvInput: Input,
    NvDatePicker: DatePicker,
    NvSearchSelect: SearchSelect,
    NvSelect: Shell,
    NvSelectContent: Shell,
    NvSelectItem: Shell,
    NvSelectTrigger: Shell,
    NvSelectValue: Shell,
    NvToolbar: Shell,
    Spinner: Shell,
  }
})

describe('PlanningForecastManagement', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    spies.saveForecast.mockReset()
    spies.saveForecast.mockResolvedValue({ success: true })
    spies.refreshForecasts.mockClear()
    spies.success.mockClear()
    spies.error.mockClear()
    spies.failure.mockClear()
    vi.stubGlobal('crypto', { randomUUID: () => 'forecast-create-uuid' })
  })

  it('只读账号能查看预测，但看不到新建和编辑入口', () => {
    const wrapper = mount(PlanningForecastManagement)

    expect(wrapper.text()).toContain('FC-2026-09-FG-1000')
    expect(wrapper.text()).toContain('减振器总成')
    expect(wrapper.text()).toContain('2026-09-01 ~ 2026-09-30')
    expect(wrapper.text()).not.toContain('新建预测')
    expect(wrapper.text()).not.toContain('编辑')
  })

  it('管理账号可从现有行进入编辑，并以同一预测编号提交完整期间和冲减窗口', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    const wrapper = mount(PlanningForecastManagement)

    await wrapper.get('[aria-label="编辑预测 FC-2026-09-FG-1000"]').trigger('click')
    expect(wrapper.find('#forecast-reference').exists()).toBe(false)
    expect(wrapper.text()).toContain('FC-2026-09-FG-1000')
    await wrapper.get('form').trigger('submit')

    expect(spies.saveForecast).toHaveBeenCalledWith({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      forecastReference: 'FC-2026-09-FG-1000',
      skuCode: 'FG-1000',
      uomCode: 'pcs',
      siteCode: 'SITE-01',
      periodStartDate: '2026-09-01',
      periodEndDate: '2026-09-30',
      quantity: 120,
      backwardConsumptionDays: 7,
      forwardConsumptionDays: 3,
    })
    expect(spies.success).toHaveBeenCalledWith('预测已更新。')
  })

  it('新建表单点击提交后同时显示字段内联错误和校验汇总，且不发送无效请求', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    const wrapper = mount(PlanningForecastManagement)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建预测'))!
      .trigger('click')
    const formDates = wrapper.findAllComponents({ name: 'NvDatePicker' })
    formDates
      .find((component) => component.props('id') === 'forecast-start')!
      .vm.$emit('update:modelValue', '')
    formDates
      .find((component) => component.props('id') === 'forecast-end')!
      .vm.$emit('update:modelValue', '')
    await wrapper.vm.$nextTick()
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    await wrapper.get('form').trigger('submit')

    expect(wrapper.get('#forecast-sku-error').text()).toContain('请选择 SKU')
    expect(wrapper.get('#forecast-site-error').text()).toContain('请选择工厂')
    expect(wrapper.get('#forecast-uom-error').text()).toContain('请选择单位')
    expect(wrapper.get('#forecast-quantity-error').text()).toContain('预测数量必须大于 0')
    expect(wrapper.get('[aria-label="预测 SKU"]').classes()).toContain('border-destructive')
    expect(wrapper.get('[aria-label="预测工厂"]').classes()).toContain('border-destructive')
    expect(wrapper.get('[aria-label="预测单位"]').classes()).toContain('border-destructive')
    expect(wrapper.get('#forecast-start').classes()).toContain('border-destructive')
    expect(wrapper.get('#forecast-end').classes()).toContain('border-destructive')
    expect(wrapper.get('[aria-label="预测 SKU"]').attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('[aria-label="预测 SKU"]').attributes('aria-describedby')).toBe(
      'forecast-sku-error',
    )
    expect(wrapper.get('#forecast-start').attributes('aria-invalid')).toBe('true')
    expect(wrapper.get('#forecast-start').attributes('aria-describedby')).toBe(
      'forecast-start-error',
    )
    expect(wrapper.get('#forecast-quantity').attributes('data-invalid')).toBe('true')
    expect(wrapper.get('#forecast-validation-summary').text()).not.toContain('请填写预测编号')
    expect(wrapper.get('#forecast-validation-summary').text()).toContain('预测数量必须大于 0')
    expect(spies.saveForecast).not.toHaveBeenCalled()
  })

  it('预测列表加载失败只显示 toast，不在页面保留常驻错误条', async () => {
    const wrapper = mount(PlanningForecastManagement)
    const error = new Error('upstream unavailable')

    spies.forecastsError.value = error
    await wrapper.vm.$nextTick()

    expect(spies.error).toHaveBeenCalledWith(error, '预测列表加载失败，请稍后重试。')
    expect(wrapper.text()).not.toContain('upstream unavailable')
  })

  it('新建使用可搜索选择器与日期组件，并隔离工厂和单位选项', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    const wrapper = mount(PlanningForecastManagement)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建预测'))!
      .trigger('click')

    expect(wrapper.find('#forecast-reference').exists()).toBe(false)
    expect(wrapper.text()).toContain('保存后自动生成')
    const sku = wrapper.get('[aria-label="预测 SKU"]')
    expect(sku.attributes('data-search-placeholder')).toBe('搜索 SKU 编码或名称')
    expect(sku.text()).toBe('减振器总成 · FG-1000')
    expect(wrapper.get('[aria-label="预测工厂"]').text()).toBe('上海工厂 · SITE-01')
    expect(wrapper.get('[aria-label="预测单位"]').text()).toBe('件 · pcs')
    expect(wrapper.find('#forecast-start').exists()).toBe(true)
    expect(wrapper.find('#forecast-end').exists()).toBe(true)
  })

  it('预测期间筛选使用日期组件并写入查询筛选状态', async () => {
    const wrapper = mount(PlanningForecastManagement)

    const dateFilters = wrapper.findAllComponents({ name: 'NvDatePicker' })
    expect(dateFilters).toHaveLength(2)
    await wrapper.get('[aria-label="预测开始日期筛选"]').trigger('click')
    await wrapper.get('[aria-label="预测结束日期筛选"]').trigger('click')

    expect(wrapper.get('[aria-label="预测开始日期筛选"]').text()).toBe('2026-09-01')
    expect(wrapper.get('[aria-label="预测结束日期筛选"]').text()).toBe('2026-09-30')
  })

  it('新建提交不要求预测编号，并复用当前对话框的幂等键', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    const wrapper = mount(PlanningForecastManagement)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建预测'))!
      .trigger('click')
    await wrapper.get('[aria-label="预测 SKU"]').trigger('click')
    await wrapper.get('[aria-label="预测工厂"]').trigger('click')
    await wrapper.get('[aria-label="预测单位"]').trigger('click')
    await wrapper.get('#forecast-start').trigger('click')
    await wrapper.get('#forecast-end').trigger('click')
    await wrapper.get('#forecast-quantity').setValue('120')
    await wrapper.get('form').trigger('submit')

    expect(spies.saveForecast).toHaveBeenCalledWith({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      forecastReference: undefined,
      skuCode: 'FG-1000',
      uomCode: 'pcs',
      siteCode: 'SITE-01',
      periodStartDate: '2026-09-01',
      periodEndDate: '2026-09-30',
      quantity: 120,
      backwardConsumptionDays: 0,
      forwardConsumptionDays: 0,
      idempotencyKey: 'forecast-create-forecast-create-uuid',
    })
  })

  it('新建失败后在同一对话框重试仍复用首次幂等键', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    spies.saveForecast.mockRejectedValueOnce(new Error('network error'))
    const randomUUID = vi
      .fn()
      .mockReturnValueOnce('setup-key')
      .mockReturnValueOnce('dialog-key')
      .mockReturnValueOnce('first-submit-key')
      .mockReturnValueOnce('retry-submit-key')
    vi.stubGlobal('crypto', { randomUUID })
    const wrapper = mount(PlanningForecastManagement)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建预测'))!
      .trigger('click')
    await wrapper.get('[aria-label="预测 SKU"]').trigger('click')
    await wrapper.get('[aria-label="预测工厂"]').trigger('click')
    await wrapper.get('[aria-label="预测单位"]').trigger('click')
    await wrapper.get('#forecast-start').trigger('click')
    await wrapper.get('#forecast-end').trigger('click')
    await wrapper.get('#forecast-quantity').setValue('120')

    await wrapper.get('form').trigger('submit')
    await wrapper.get('form').trigger('submit')

    expect(spies.saveForecast).toHaveBeenCalledTimes(2)
    expect(spies.saveForecast.mock.calls[0]?.[0].idempotencyKey).toBe('forecast-create-dialog-key')
    expect(spies.saveForecast.mock.calls[1]?.[0].idempotencyKey).toBe('forecast-create-dialog-key')
  })

  it('幂等冲突提示先刷新确认结果并重新打开新建对话框', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    spies.saveForecast.mockRejectedValueOnce({
      message:
        "Idempotency key 'forecast-create-1' conflicts with a different forecast input create payload.",
    })
    const wrapper = mount(PlanningForecastManagement)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建预测'))!
      .trigger('click')
    await wrapper.get('[aria-label="预测 SKU"]').trigger('click')
    await wrapper.get('[aria-label="预测工厂"]').trigger('click')
    await wrapper.get('[aria-label="预测单位"]').trigger('click')
    await wrapper.get('#forecast-start').trigger('click')
    await wrapper.get('#forecast-end').trigger('click')
    await wrapper.get('#forecast-quantity').setValue('120')
    await wrapper.get('form').trigger('submit')

    expect(spies.failure).toHaveBeenCalledWith(
      '保存预测失败',
      expect.objectContaining({
        message:
          '本次填写内容与先前提交不一致。请先刷新预测列表确认首次提交结果；如需重新创建，请关闭当前窗口后再次新建。',
      }),
      '保存预测失败，请稍后重试。',
    )
  })
})
