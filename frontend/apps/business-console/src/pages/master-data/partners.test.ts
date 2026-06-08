import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import PartnersPage from './partners.vue'

const stub = vi.hoisted(() => ({
  createPartner: vi.fn(),
}))

// 列表桩数据带真实 typed 角色字段（partnerType 主角色 + partnerRoles 附加角色）。
const partnerRows = [
  { resourceType: 'business-partner', code: 'P-001', displayName: '广汽集团', active: true, partnerType: 'customer', partnerRoles: ['carrier'], taxId: '91440000MA5R' },
  { resourceType: 'business-partner', code: 'P-002', displayName: '中石化润滑油', active: true, partnerType: 'supplier' },
]

function stubPartners() {
  return {
    createPartner: stub.createPartner,
    createPartnerError: shallowRef(undefined),
    createPartnerPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'business-partner', skip: 0, take: 10 }),
    partners: computed(() => partnerRows),
    partnersError: shallowRef(undefined),
    partnersPending: shallowRef(false),
    partnersTotal: computed(() => partnerRows.length),
    refreshPartners: vi.fn(),
  }
}

function stubActions() {
  return {
    update: vi.fn(),
    disable: vi.fn(),
    enable: vi.fn(),
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessPartners: () => stubPartners(),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('master-data partners page', () => {
  it('renders the title, real role labels (not code-guessed) and counts', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('业务伙伴')
    expect(wrapper.text()).toContain('广汽集团')
    // 主角色 customer -> 客户，附加角色 carrier -> 承运商；中石化为供应商。
    expect(wrapper.text()).toContain('客户')
    expect(wrapper.text()).toContain('承运商')
    expect(wrapper.text()).toContain('供应商')
  })

  it('exposes a role filter select and a create-partner button', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.find('[aria-label="伙伴角色"]').exists()).toBe(true)
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建伙伴'))).toBe(true)
  })

  it('opens the create dialog with an explicit primary-role select and extra-role checkboxes', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建伙伴'))!.trigger('click')
    await flushPromises()

    const body = document.body.textContent ?? ''
    expect(body).toContain('新建业务伙伴')
    expect(body).toContain('主角色')
    expect(body).toContain('附加角色')
  })

  it('exposes per-row actions', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })
})
