import type {
  BusinessConsoleErpWorkCenterMachineOverheadRateItem,
  BusinessConsoleErpWorkCenterMachineOverheadRateListResponse,
} from '@nerv-iip/api-client'
import { NvMetricStrip } from '@nerv-iip/ui'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import MachineOverheadRatesPage from './finance/machine-overhead-rates.vue'

const state = vi.hoisted(() => ({
  current: undefined as unknown,
  permissionCodes: [] as string[],
}))
vi.mock('@/composables/useBusinessErp', () => ({
  useErpWorkCenterMachineOverheadRates: () => state.current,
}))
vi.mock('@/composables/useEquipmentPickerCatalog', () => ({
  useEquipmentWorkCenterCatalog: () => ({
    workCenterOptions: shallowRef([{ value: 'WC-CNC-01', label: '数控加工中心一线' }]),
    workCentersPending: shallowRef(false),
  }),
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

function rate(
  revision: number,
  patch: Partial<BusinessConsoleErpWorkCenterMachineOverheadRateItem> = {},
): BusinessConsoleErpWorkCenterMachineOverheadRateItem {
  return {
    workCenterMachineOverheadRateId: `rate-${revision}`,
    accountingPeriodCode: '2026-09',
    applicability: 'applicable',
    fixedOverheadBudget: 12000,
    variableOverheadBudget: 3600,
    normalCapacityMachineHours: 400,
    fixedHourlyRate: 30,
    variableHourlyRate: 9,
    totalHourlyRate: 39,
    currencyCode: 'USD',
    revision,
    changedBy: 'user:finance-01',
    reason: '九月预算核定',
    changedAtUtc: '2026-09-01T02:00:00Z',
    ...patch,
  }
}

function listResponse(
  items: BusinessConsoleErpWorkCenterMachineOverheadRateItem[],
): BusinessConsoleErpWorkCenterMachineOverheadRateListResponse {
  return {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    workCenterId: 'WC-CNC-01',
    accountingPeriodCode: '2026-09',
    currentRevision: items[0]?.revision ?? 0,
    pageNumber: 1,
    pageSize: 50,
    totalCount: items.length,
    items,
  }
}

function createState(items: BusinessConsoleErpWorkCenterMachineOverheadRateItem[]) {
  return {
    ready: shallowRef(true),
    workCenterId: shallowRef('WC-CNC-01'),
    accountingPeriodCode: shallowRef(''),
    rates: shallowRef<BusinessConsoleErpWorkCenterMachineOverheadRateListResponse | undefined>(
      listResponse(items),
    ),
    pending: shallowRef(false),
    error: shallowRef<Error | undefined>(undefined),
    refresh: vi.fn(),
    addRevision: vi.fn().mockResolvedValue({}),
    addRevisionPending: shallowRef(false),
    addRevisionError: shallowRef<Error | undefined>(undefined),
  }
}

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvSheet: {
    props: ['open'],
    emits: ['update:open'],
    template: '<section v-if="open" class="sheet"><slot /></section>',
  },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetFooter: { template: '<footer><slot /></footer>' },
  NvSheetHeader: { template: '<header><slot /></header>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
}
const render = () => mount(MachineOverheadRatesPage, { global: { stubs } })

async function openSheet(wrapper: ReturnType<typeof render>) {
  const add = wrapper.findAll('button').find((button) => button.text().includes('新增修订'))
  await add!.trigger('click')
}

let data: ReturnType<typeof createState>

describe('机器制造费用率', () => {
  beforeEach(() => {
    state.permissionCodes = ['business.erp.finance.read', 'business.erp.finance.manage']
    data = createState([
      rate(2, {
        variableOverheadBudget: 4000,
        variableHourlyRate: 10,
        totalHourlyRate: 40,
        reason: '变动预算上调',
      }),
      rate(1),
    ])
    state.current = data
  })

  it('默认按本月会计期间查询，输入完成后才按新期间查询', async () => {
    const now = new Date()
    const month = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
    const wrapper = render()
    await flushPromises()
    expect(data.accountingPeriodCode.value).toBe(month)

    const period = wrapper.get('input[aria-label="会计期间"]')
    await period.setValue(' 2026-10 ')
    expect(data.accountingPeriodCode.value).toBe(month)
    await period.trigger('change')
    expect(data.accountingPeriodCode.value).toBe('2026-10')
  })

  it('以本期最高版本为当前费率，其余修订显示已被取代', async () => {
    const wrapper = render()
    await flushPromises()
    const strip = wrapper.findComponent(NvMetricStrip).text()
    expect(strip).toContain('USD 40.00 / 机器小时')
    expect(strip).toContain('第 2 版')
    const rows = wrapper.findAll('tbody tr').map((row) => row.text())
    expect(rows[0]).toContain('当前生效')
    expect(rows[1]).toContain('已被新修订取代')
  })

  it('本期没有修订时提示未配置，明确不适用时显示不适用', async () => {
    data.rates.value = listResponse([])
    const empty = render()
    await flushPromises()
    expect(empty.findComponent(NvMetricStrip).text()).toContain('未配置')

    data.rates.value = listResponse([
      rate(3, {
        applicability: 'notApplicable',
        fixedOverheadBudget: 0,
        variableOverheadBudget: 0,
        normalCapacityMachineHours: 0,
        fixedHourlyRate: 0,
        variableHourlyRate: 0,
        totalHourlyRate: 0,
      }),
    ])
    const notApplicable = render()
    await flushPromises()
    expect(notApplicable.findComponent(NvMetricStrip).text()).toContain('不适用')
  })

  it('新增适用修订时校验预算与产能，沿用已固定的币种', async () => {
    const wrapper = render()
    await flushPromises()
    await openSheet(wrapper)
    const currency = wrapper.get<HTMLInputElement>('#erp-mor-currency')
    expect(currency.element.value).toBe('USD')
    expect(currency.attributes('disabled')).toBeDefined()

    await wrapper.get('#erp-mor-capacity').setValue('0')
    await wrapper.get('#erp-mor-reason').setValue('十月预算')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(data.addRevision).not.toHaveBeenCalled()

    await wrapper.get('#erp-mor-capacity').setValue('500')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(data.addRevision).toHaveBeenCalledWith({
      applicability: 'applicable',
      fixedOverheadBudget: 12000,
      variableOverheadBudget: 4000,
      normalCapacityMachineHours: 500,
      currencyCode: 'USD',
      reason: '十月预算',
    })
  })

  it('新增不适用修订时预算与产能按零提交', async () => {
    const wrapper = render()
    await flushPromises()
    await openSheet(wrapper)
    const notApplicable = wrapper
      .findAll('[data-slot="nv-radio-group-item"]')
      .find((item) => item.attributes('value') === 'notApplicable')
    await notApplicable!.trigger('click')
    expect(wrapper.find('#erp-mor-capacity').exists()).toBe(false)

    await wrapper.get('#erp-mor-reason').setValue('该线本期停用设备')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(data.addRevision).toHaveBeenCalledWith({
      applicability: 'notApplicable',
      fixedOverheadBudget: 0,
      variableOverheadBudget: 0,
      normalCapacityMachineHours: 0,
      currencyCode: 'USD',
      reason: '该线本期停用设备',
    })
  })

  it('没有财务维护权限时不提供新增修订', async () => {
    state.permissionCodes = ['business.erp.finance.read']
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).not.toContain('新增修订')
  })
})
