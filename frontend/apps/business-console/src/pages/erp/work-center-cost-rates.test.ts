import type {
  BusinessConsoleErpWorkCenterCostRateItem,
  BusinessConsoleErpWorkCenterCostRateListResponse,
} from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import WorkCenterCostRatesPage from './finance/work-center-cost-rates.vue'

const state = vi.hoisted(() => ({
  current: undefined as unknown,
  permissionCodes: [] as string[],
}))
vi.mock('@/composables/useBusinessErp', () => ({
  useErpWorkCenterCostRates: () => state.current,
}))
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({
    filters: { take: 10 },
    resources: shallowRef([{ code: 'WC-CNC-01', displayName: '数控加工中心一线' }]),
    resourcesPending: shallowRef(false),
  }),
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: state.permissionCodes } }),
}))

function rate(
  revision: number,
  patch: Partial<BusinessConsoleErpWorkCenterCostRateItem>,
): BusinessConsoleErpWorkCenterCostRateItem {
  return {
    workCenterCostRateId: `rate-${revision}`,
    workCenterId: 'WC-CNC-01',
    hourlyRate: 60,
    currencyCode: 'USD',
    effectiveFromUtc: '2026-01-01T00:00:00Z',
    effectiveToUtc: null,
    revision,
    changedBy: 'user:finance-01',
    reason: '年度人工费率核定',
    changedAtUtc: '2026-01-01T02:00:00Z',
    effectiveStatus: 'effective',
    isEffectiveAtUtc: true,
    isCurrentEffectiveRevision: false,
    ...patch,
  }
}

function listResponse(
  items: BusinessConsoleErpWorkCenterCostRateItem[],
): BusinessConsoleErpWorkCenterCostRateListResponse {
  return {
    workCenterId: 'WC-CNC-01',
    atUtc: '2026-09-23T02:00:00Z',
    currentEffectiveRevision: items.find((row) => row.isCurrentEffectiveRevision)?.revision ?? null,
    items,
  }
}

function createState(items: BusinessConsoleErpWorkCenterCostRateItem[]) {
  return {
    ready: shallowRef(true),
    workCenterId: shallowRef('WC-CNC-01'),
    rates: shallowRef<BusinessConsoleErpWorkCenterCostRateListResponse | undefined>(
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
  NvDialog: {
    props: ['open'],
    emits: ['update:open'],
    template: '<section v-if="open" class="dialog"><slot /></section>',
  },
  NvDialogClose: { template: '<span><slot /></span>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
}
const render = () => mount(WorkCenterCostRatesPage, { global: { stubs } })

let data: ReturnType<typeof createState>
const history = () => [
  rate(3, {
    hourlyRate: 72,
    effectiveFromUtc: '2026-10-01T00:00:00Z',
    effectiveStatus: 'future',
    isEffectiveAtUtc: false,
    reason: '四季度调薪',
  }),
  rate(2, { hourlyRate: 66, isCurrentEffectiveRevision: true, reason: '年中复核' }),
  rate(1, {}),
  rate(0, {
    effectiveFromUtc: '2025-01-01T00:00:00Z',
    effectiveToUtc: '2026-01-01T00:00:00Z',
    effectiveStatus: 'expired',
    isEffectiveAtUtc: false,
  }),
]

describe('工作中心费率', () => {
  beforeEach(() => {
    state.permissionCodes = ['business.erp.finance.read', 'business.erp.finance.manage']
    data = createState(history())
    state.current = data
  })

  it('以当前生效修订为当前费率，并区分被取代、未生效与已过期的修订', async () => {
    const wrapper = render()
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('US$66.00 / 小时')
    expect(text).toContain('第 2 版')
    const rows = wrapper.findAll('tbody tr').map((row) => row.text())
    expect(rows[0]).toContain('未到生效时间')
    expect(rows[1]).toContain('当前生效')
    expect(rows[2]).toContain('已被新修订取代')
    expect(rows[3]).toContain('已过期')
  })

  it('没有覆盖当前时间的修订时明确未配置', async () => {
    data.rates.value = listResponse([history()[3]])
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).toContain('未配置')
  })

  it('新增修订沿用已固定的币种，生效日期按本地零点提交', async () => {
    const wrapper = render()
    await flushPromises()
    const add = wrapper.findAll('button').find((button) => button.text().includes('新增修订'))
    await add!.trigger('click')
    const currency = wrapper.get<HTMLInputElement>('#erp-wcr-currency')
    expect(currency.element.value).toBe('USD')
    expect(currency.attributes('disabled')).toBeDefined()

    await wrapper.get('#erp-wcr-rate').setValue('70.5')
    await wrapper.get('#erp-wcr-from').setValue('2026-11-01')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(data.addRevision).not.toHaveBeenCalled()

    await wrapper.get('#erp-wcr-reason').setValue('十一月起执行新工价')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(data.addRevision).toHaveBeenCalledWith({
      hourlyRate: 70.5,
      currencyCode: 'USD',
      effectiveFromUtc: new Date(2026, 10, 1).toISOString(),
      reason: '十一月起执行新工价',
    })
  })

  it('没有财务维护权限时不提供新增修订', async () => {
    state.permissionCodes = ['business.erp.finance.read']
    const wrapper = render()
    await flushPromises()
    expect(wrapper.text()).not.toContain('新增修订')
  })
})
