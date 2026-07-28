import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

import ErpPage from './index.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const filters = reactive<{ status?: string; keyword?: string; skip: number; take: number }>({
  status: undefined,
  keyword: undefined,
  skip: 0,
  take: 10,
})

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef('10') }),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: { keyword: 'PR-001' } }),
}))

vi.mock('@/composables/useBusinessErp', () => ({
  useErpPurchaseRequisitions: () => ({
    filters,
    items: computed(() => [
      {
        purchaseRequisitionId: 'pr-id-001',
        requisitionNo: 'PR-001',
        requiredDate: '2026-07-03',
        quantity: 8,
        siteCode: 'SITE-01',
        skuCode: 'SKU-RM-001',
        status: 'Open',
        suggestionId: 'suggestion-001',
        uomCode: 'kg',
      },
    ]),
    total: computed(() => 1),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    convertToPurchaseOrder: vi.fn(),
    convertToPurchaseOrderError: shallowRef(undefined),
    convertToPurchaseOrderPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessPartners: () => ({
    filters: reactive({ includeDisabled: undefined }),
    partners: computed(() => []),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('ERP purchase requisition page', () => {
  it('initializes keyword from downstream route query and renders real requisition rows', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(filters.keyword).toBe('PR-001')
    expect(wrapper.text()).toContain('PR-001')
    expect(wrapper.text()).toContain('suggestion-001')
  })

  it('renders requisition status filter and semantic KPIs', async () => {
    const wrapper = mount(ErpPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    // 三卡合并为一张构成卡：主数值取筛选总数，分段是流转状态。
    expect(wrapper.text()).toContain('采购申请')
    expect(wrapper.text()).toContain('待处理')
    expect(wrapper.text()).toContain('已转单')
    expect(wrapper.text()).toContain('全部申请')
  })
})
