import type { EffectScope } from 'vue'
import { effectScope, nextTick, reactive } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  useInventoryScopeCatalog,
  useInventoryScopeDefaults,
  useInventorySiteExpiryOverview,
} from './useInventoryScope'

const state = vi.hoisted(() => ({
  skus: undefined as { value: unknown[] } | undefined,
  sites: undefined as { value: unknown[] } | undefined,
  skuFilters: undefined as { take: number } | undefined,
  siteFilters: undefined as { take: number } | undefined,
  expiryFilters: undefined as { siteCode: string } | undefined,
  expiryPageSize: undefined as { value: number } | undefined,
  expiryResponse: undefined as { value: unknown } | undefined,
  enabledWhen: undefined as (() => boolean) | undefined,
  resourceTypes: [] as string[],
}))

vi.mock('../master-data', async () => {
  const { reactive, shallowRef } = await import('vue')
  state.skus = shallowRef<unknown[]>([])
  state.sites = shallowRef<unknown[]>([])
  state.skuFilters = reactive({ take: 100 })
  state.siteFilters = reactive({ take: 100 })
  return {
    useBusinessSkus: () => ({
      filters: state.skuFilters,
      skus: state.skus,
      skusPending: shallowRef(false),
    }),
    useBusinessMasterDataResources: (resourceType: string) => {
      state.resourceTypes.push(resourceType)
      return {
        filters: state.siteFilters,
        resources: state.sites,
        resourcesPending: shallowRef(false),
      }
    },
  }
})

vi.mock('./useBusinessInventory', async () => {
  const { reactive, shallowRef } = await import('vue')
  state.expiryFilters = reactive({ siteCode: '' })
  state.expiryPageSize = shallowRef(50)
  state.expiryResponse = shallowRef(undefined)
  return {
    useInventoryExpiryAlerts: (enabledWhen: () => boolean) => {
      state.enabledWhen = enabledWhen
      return {
        expiryAlerts: shallowRef([]),
        expiryAlertsResponse: state.expiryResponse,
        expiryAlertsPage: shallowRef(1),
        expiryAlertsPageSize: state.expiryPageSize,
        expiryAlertsTotal: shallowRef(0),
        expiryAlertsError: shallowRef(undefined),
        expiryAlertsPending: shallowRef(false),
        expiryAlertsSuccessful: shallowRef(false),
        filters: state.expiryFilters,
        refreshExpiryAlerts: vi.fn(),
      }
    },
  }
})

const context = reactive({
  subject: 'original',
  organizationId: 'org',
  environmentId: 'env',
  siteCode: '',
})
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return { principalId: context.subject }
    },
  }),
}))
vi.mock('@/stores/businessContext', () => ({ useBusinessContextStore: () => context }))

describe('useInventoryScope', () => {
  let scope: EffectScope

  beforeEach(() => {
    scope = effectScope()
    state.skus!.value = [
      { code: 'SKU-SHOCK-FR-01', displayName: '前减振器总成', baseUomCode: 'pcs' },
      { code: 'RM-BAR-45-01', displayName: '45号钢棒料', baseUomCode: 'kg' },
      { code: '  ', displayName: '无编码不进目录' },
    ]
    state.sites!.value = [{ code: 'SITE-002', displayName: '苏州工厂' }]
    state.resourceTypes.length = 0
  })

  afterEach(() => {
    scope.stop()
  })

  it('物料目录只收有编码的行，并把基本单位作为选择弹窗的辅助信息', () => {
    const catalog = scope.run(() => useInventoryScopeCatalog())!

    expect(state.resourceTypes).toContain('site')
    expect(catalog.skuOptions.value).toEqual([
      { value: 'SKU-SHOCK-FR-01', label: '前减振器总成', hint: 'pcs' },
      { value: 'RM-BAR-45-01', label: '45号钢棒料', hint: 'kg' },
    ])
    expect(catalog.siteOptions.value).toEqual([{ value: 'SITE-002', label: '苏州工厂' }])
  })

  it('只在目录有唯一工厂时自动选择，无工厂时保持空白', () => {
    const filters = reactive({ skuCode: '', uomCode: '', siteCode: '' })
    scope.run(() => useInventoryScopeDefaults(filters))
    expect(filters.siteCode).toBe('SITE-002')
    state.sites!.value = []
    expect(filters.siteCode).toBe('')
  })

  it('多工厂时等待选择，仅继承合法深链，并按主体记住显式选择', () => {
    state.sites!.value = [{ code: 'SITE-002' }, { code: 'ST001' }]
    const route = reactive({ siteCode: 'UNAUTHORIZED' })
    const filters = reactive({ siteCode: '' })
    const other = reactive({ siteCode: '' })
    const scopeWithLink = scope.run(() => useInventoryScopeDefaults(filters, () => route.siteCode))!
    scope.run(() => useInventoryScopeDefaults(other))
    expect(filters.siteCode).toBe('')
    route.siteCode = 'ST001'
    expect(filters.siteCode).toBe('ST001')
    expect(other.siteCode).toBe('')
    scopeWithLink.siteSelection.value = 'SITE-002'
    expect(other.siteCode).toBe('SITE-002')
    context.subject = 'other'
    expect(other.siteCode).toBe('')
    expect(filters.siteCode).toBe('ST001')
  })

  it('单位跟随所选物料的基本单位，原材料不会被当成计件物料', async () => {
    const filters = reactive({ skuCode: '', uomCode: '', siteCode: '' })
    scope.run(() => useInventoryScopeDefaults(filters))

    expect(filters.uomCode).toBe('')

    filters.skuCode = 'RM-BAR-45-01'
    await nextTick()
    expect(filters.uomCode).toBe('kg')

    filters.skuCode = 'SKU-SHOCK-FR-01'
    await nextTick()
    expect(filters.uomCode).toBe('pcs')

    // 目录里查不到的编码（深链带进来的）不猜单位：宁可让查询等主档，也不用假单位查出空结果。
    filters.skuCode = 'SKU-UNKNOWN'
    await nextTick()
    expect(filters.uomCode).toBe('')

    filters.skuCode = ''
    await nextTick()
    expect(filters.uomCode).toBe('')
  })

  it('全厂效期概览只要工厂就启用，并按响应给出过期与近效期计数', async () => {
    const site = reactive({ code: '' })
    const overview = scope.run(() => useInventorySiteExpiryOverview(() => site.code))!

    expect(state.enabledWhen?.()).toBe(false)
    expect(state.expiryPageSize?.value).toBe(5)

    site.code = 'SITE-002'
    await nextTick()
    expect(state.enabledWhen?.()).toBe(true)
    expect(state.expiryFilters?.siteCode).toBe('SITE-002')

    state.expiryResponse!.value = {
      items: [],
      totalCount: 27,
      expiredCount: 9,
      nearExpiryCount: 18,
      skuCount: 6,
    }
    await nextTick()
    expect(overview.overviewTotalCount.value).toBe(27)
    expect(overview.overviewExpiredCount.value).toBe(9)
    expect(overview.overviewNearExpiryCount.value).toBe(18)
    expect(overview.overviewSkuCount.value).toBe(6)
  })
})
