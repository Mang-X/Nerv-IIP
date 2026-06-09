import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ReferenceDataPage from './reference-data.vue'

const stub = vi.hoisted(() => ({
  createCode: vi.fn(),
}))

// 列表桩数据带 codeSet 分组字段。
const codeRows = [
  { resourceType: 'reference-data', codeSet: 'material-type', code: 'raw-material', displayName: '原材料', active: true },
  { resourceType: 'reference-data', codeSet: 'material-type', code: 'finished-goods', displayName: '成品', active: false },
]

function stubCodes() {
  return {
    codes: computed(() => codeRows),
    codesError: shallowRef(undefined),
    codesPending: shallowRef(false),
    codesTotal: computed(() => codeRows.length),
    createCode: stub.createCode,
    createCodeError: shallowRef(undefined),
    createCodePending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'reference-data', skip: 0, take: 10, codeSet: 'material-type' }),
    refreshCodes: vi.fn(),
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
  useReferenceDataCodes: () => stubCodes(),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('master-data reference-data page', () => {
  it('renders the title and a CodeSet master list', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('数据字典')
    const nav = wrapper.find('nav[aria-label="字典分组"]')
    expect(nav.exists()).toBe(true)
    expect(nav.text()).toContain('物料类型')
    expect(nav.text()).toContain('技能等级')
    expect(nav.text()).toContain('仓储条件')
  })

  it('renders the selected CodeSet entries on the right', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('原材料')
    expect(wrapper.text()).toContain('成品')
  })

  it('switches the selected CodeSet when a left item is clicked', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const button = wrapper.find('nav[aria-label="字典分组"]').findAll('button').find((b) => b.text().includes('仓储条件'))!
    await button.trigger('click')
    await flushPromises()

    expect(button.attributes('aria-pressed')).toBe('true')
  })

  it('keeps system-maintained CodeSets read-only for creation', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const createButton = wrapper.findAll('button').find((b) => b.text().includes('新建字典条目'))!
    expect(createButton.attributes('disabled')).toBeDefined()
  })

  it('opens the create dialog for maintainable CodeSets', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const button = wrapper.find('nav[aria-label="字典分组"]').findAll('button').find((b) => b.text().includes('仓储条件'))!
    await button.trigger('click')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建字典条目'))!.trigger('click')
    await flushPromises()

    const body = document.body.textContent ?? ''
    expect(body).toContain('新建字典条目')
    expect(body).toContain('所属字典')
  })

  it('exposes per-row actions', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })
})
