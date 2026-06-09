import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import FacilitiesPage from './facilities.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn(),
  createWorkshop: vi.fn(),
}))

function stubResource(resourceType: string) {
  const rows = resourceType === 'site'
    ? [{ resourceType: 'site', code: 'PLANT-A', displayName: '宁波工厂', active: true, snapshotVersion: '1' }]
    : resourceType === 'production-line'
      ? [{ resourceType: 'production-line', code: 'LINE-A', displayName: '前桥线', active: true, snapshotVersion: '1' }]
      : [{ resourceType: 'work-center', code: 'WC-A', displayName: '焊接中心', active: true, snapshotVersion: '1' }]
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    items: computed(() => rows),
    total: computed(() => rows.length),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    create: stub.create,
    createError: shallowRef(undefined),
    createPending: shallowRef(false),
  }
}

function stubWorkshops() {
  const rows = [{ resourceType: 'workshop', code: 'WS-A', displayName: '总装车间', active: true, siteCode: 'PLANT-A' }]
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    workshops: computed(() => rows),
    workshopsTotal: computed(() => rows.length),
    workshopsError: shallowRef(undefined),
    workshopsPending: shallowRef(false),
    refreshWorkshops: vi.fn(),
    createWorkshop: stub.createWorkshop,
    createWorkshopError: shallowRef(undefined),
    createWorkshopPending: shallowRef(false),
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
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useBusinessWorkshops: () => stubWorkshops(),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('master-data facilities page', () => {
  it('renders the title, four tabs, sample rows and create buttons', async () => {
    const wrapper = mount(FacilitiesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('工厂与产线')
    expect(wrapper.text()).toContain('工厂 → 车间 → 产线 → 工作中心 → 设备')
    const tabs = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabs.some((t) => t.includes('工厂'))).toBe(true)
    expect(tabs.some((t) => t.includes('车间'))).toBe(true)
    expect(tabs.some((t) => t.includes('产线'))).toBe(true)
    expect(tabs.some((t) => t.includes('工作中心'))).toBe(true)

    expect(wrapper.text()).toContain('宁波工厂')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建工厂'))).toBe(true)
  })

  it('renders real workshop list with create button and per-row actions', async () => {
    const wrapper = mount(FacilitiesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const workshopTab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes('车间'))
    expect(workshopTab).toBeTruthy()
    await workshopTab!.trigger('focus')
    await workshopTab!.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('总装车间')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建车间'))).toBe(true)
    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('exposes per-row actions (detail / rename / disable)', async () => {
    const wrapper = mount(FacilitiesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })
})
