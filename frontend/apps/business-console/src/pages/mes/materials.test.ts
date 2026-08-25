import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

const refreshLineSideInventory = vi.fn(async () => {})
const lineSideInventoryPending = ref(false)
const lineSideInventoryError = ref<unknown>(null)
const lineSideInventoryReady = ref(true)
const lineSideInventoryBalances = ref([
  {
    siteCode: 'SITE-SH',
    locationCode: 'LINE-A01',
    skuCode: 'SKU-DAMPER-001',
    uomCode: 'pcs',
    onHandQuantity: 120,
    reservedQuantity: 20,
    availableQuantity: 100,
    lotCount: 3,
    oldestProductionDate: '2026-08-20',
    ageDays: 6,
    ageCompleteness: 'complete' as const,
  },
  {
    siteCode: 'SITE-SH',
    locationCode: 'LINE-A02',
    skuCode: 'SKU-SEAL-008',
    uomCode: 'pcs',
    onHandQuantity: 45,
    reservedQuantity: 5,
    availableQuantity: 40,
    lotCount: 2,
    oldestProductionDate: '2026-08-22',
    ageDays: 4,
    ageCompleteness: 'partial' as const,
  },
  {
    siteCode: 'SITE-SH',
    locationCode: 'LINE-A03',
    skuCode: 'SKU-OIL-012',
    uomCode: 'l',
    onHandQuantity: 18,
    reservedQuantity: 0,
    availableQuantity: 18,
    lotCount: 1,
    oldestProductionDate: null,
    ageDays: null,
    ageCompleteness: 'unavailable' as const,
  },
])
const initialLineSideInventoryBalances = lineSideInventoryBalances.value

vi.mock('@/composables/useBusinessMes', () => ({
  useMesMaterialIssueRequests: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    materialIssueRequests: ref([]),
    materialIssueRequestsError: ref(null),
    materialIssueRequestsPending: ref(false),
    materialIssueRequestsTotal: ref(0),
    refreshMaterialIssueRequests: vi.fn(async () => {}),
  }),
  useMesLineSideInventoryBalances: () => ({
    lineSideInventoryBalances: computed(() => lineSideInventoryBalances.value),
    lineSideInventoryTotal: computed(() => lineSideInventoryBalances.value.length),
    lineSideInventoryPending,
    lineSideInventoryError,
    lineSideInventoryReady,
    refreshLineSideInventory,
  }),
}))

vi.mock('@/composables/mes/useMesReferenceLabels', () => ({
  mesMaterialIssueStatusOptions: [{ label: '全部状态', value: 'all' }],
  useMesReferenceLabels: () => ({ statusLabel: (value?: string) => value ?? '未知状态' }),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({ resolveSku: (value?: string) => value }),
}))

vi.mock('@/layouts/BusinessLayout.vue', () => ({
  default: { template: '<main><slot /></main>' },
}))

vi.mock('vue-router', () => ({
  RouterLink: { template: '<a><slot /></a>' },
  useRouter: () => ({ push: vi.fn(async () => {}) }),
}))

import MaterialsPage from './materials.vue'

describe('Console MES materials page line-side inventory', () => {
  beforeEach(() => {
    refreshLineSideInventory.mockClear()
    lineSideInventoryPending.value = false
    lineSideInventoryError.value = null
    lineSideInventoryReady.value = true
    lineSideInventoryBalances.value = initialLineSideInventoryBalances
  })

  it('shows authoritative balances and distinguishes complete, partial, and unknown ages', async () => {
    const wrapper = mount(MaterialsPage)
    await flushPromises()

    expect(wrapper.text()).toContain('线边库存余额与账龄')
    expect(wrapper.text()).toContain('SKU-DAMPER-001')
    expect(wrapper.text()).toContain('在手 120 pcs')
    expect(wrapper.text()).toContain('可用 100 pcs')
    expect(wrapper.text()).toContain('6 天')
    expect(wrapper.text()).toContain('账龄完整')
    expect(wrapper.text()).toContain('4 天（部分批次缺少生产日期）')
    expect(wrapper.text()).toContain('账龄部分可知')
    expect(wrapper.text()).toContain('账龄未知（批次缺少生产日期）')
    expect(wrapper.text()).not.toContain('0 天')
  })

  it('distinguishes loading, error, empty, and refresh behavior', async () => {
    lineSideInventoryBalances.value = []
    lineSideInventoryPending.value = true
    lineSideInventoryReady.value = false
    const wrapper = mount(MaterialsPage)
    await flushPromises()
    expect(wrapper.text()).toContain('正在加载线边库存余额与账龄')

    lineSideInventoryPending.value = false
    lineSideInventoryError.value = new Error('网络暂不可用')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('线边库存加载失败')
    expect(wrapper.text()).not.toContain('暂无线边库存余额')

    lineSideInventoryError.value = null
    lineSideInventoryReady.value = true
    await flushPromises()
    expect(wrapper.text()).toContain('当前组织/环境范围暂无线边库存余额')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('刷新库存'))!
      .trigger('click')
    expect(refreshLineSideInventory).toHaveBeenCalledTimes(1)
  })
})
