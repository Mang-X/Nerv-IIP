import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, reactive, shallowRef } from 'vue'
import PlanningForecastManagement from './PlanningForecastManagement.vue'
import { useAuthStore } from '@/stores/auth'

const spies = vi.hoisted(() => ({
  saveForecast: vi.fn(async () => ({ success: true })),
  refreshForecasts: vi.fn(async () => undefined),
  success: vi.fn(),
  failure: vi.fn(),
}))

vi.mock('@/composables/useBusinessForecasts', () => ({
  useBusinessForecasts: () => ({
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
    forecastsError: shallowRef(null),
    forecastsPending: shallowRef(false),
    refreshForecasts: spies.refreshForecasts,
    saveForecast: spies.saveForecast,
    saveForecastPending: shallowRef(false),
  }),
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
  notifySuccess: spies.success,
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
    props: ['modelValue', 'disabled', 'type'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" :disabled="disabled" :type="type || \'text\'" @input="$emit(\'update:modelValue\', $event.target.value)" />',
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
    props: ['modelValue', 'id'],
    emits: ['update:modelValue'],
    template:
      '<button type="button" :id="id" @click="$emit(\'update:modelValue\', id.includes(\'start\') ? \'2026-09-01\' : \'2026-09-30\')">{{ modelValue }}</button>',
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
    spies.saveForecast.mockClear()
    spies.refreshForecasts.mockClear()
    spies.success.mockClear()
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

  it('新建表单点击提交后才显示校验汇总，且不发送无效请求', async () => {
    useAuthStore().$patch({
      principal: { permissionCodes: ['business.planning.demands.manage'] },
    } as never)
    const wrapper = mount(PlanningForecastManagement)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('新建预测'))!
      .trigger('click')
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
    await wrapper.get('form').trigger('submit')

    expect(wrapper.get('[role="alert"]').text()).not.toContain('请填写预测编号')
    expect(wrapper.get('[role="alert"]').text()).toContain('预测数量必须大于 0')
    expect(spies.saveForecast).not.toHaveBeenCalled()
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
    expect(wrapper.findAllComponents({ name: 'NvDatePicker' })).toHaveLength(2)
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
})
