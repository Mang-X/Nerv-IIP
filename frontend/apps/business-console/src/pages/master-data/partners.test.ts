import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import PartnersPage from './partners.vue'

const stub = vi.hoisted(() => ({
  createPartner: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({
    name: '广汽集团',
    partnerType: 'customer',
    partnerRoles: ['carrier'],
    taxId: '91440000MA5R',
  }),
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
    update: actionStub.update,
    disable: vi.fn(),
    enable: vi.fn(),
    fetchDetail: actionStub.fetchDetail,
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

// 把 RowActions 的下拉换成同步渲染插槽的轻量桩，让「编辑」菜单项可直接点击。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}

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

  it('opens the partner dialog in edit mode (full-field) when a row 编辑 is triggered', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(PartnersPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 详情被拉取用于全字段回填（第一行编码 P-001）。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('P-001')
    // 对话框进入编辑态：标题含「编辑业务伙伴」，编码只读。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑业务伙伴')
    const codeInput = document.getElementById('partner-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('blocks create on empty required fields with a summary alert and no create call', async () => {
    stub.createPartner.mockClear()
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 打开「新建伙伴」对话框（重置后 code/name 为空 → 非法）。
    await wrapper.findAll('button').find((b) => b.text().includes('新建伙伴'))!.trigger('click')
    await flushPromises()

    // 对话框 teleport 到 body，从 body 取就地表单触发提交。
    const form = document.body.querySelector('form')
    expect(form).toBeTruthy()
    form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('请完整填写带 * 的必填项')
    expect(stub.createPartner).not.toHaveBeenCalled()
  })
})
