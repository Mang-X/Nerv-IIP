import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import OrganizationPage from './organization.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn(),
}))

function stubResource(resourceType: string) {
  const labelByType: Record<string, { code: string, name: string }> = {
    'department': { code: 'DEPT-A', name: '总装部' },
    'team': { code: 'TEAM-A', name: '白班班组' },
    'shift': { code: 'SHIFT-A', name: '白班' },
    'work-calendar': { code: 'CAL-A', name: '标准日历' },
  }
  const entry = labelByType[resourceType]
  const rows = entry ? [{ resourceType, code: entry.code, displayName: entry.name, active: true, snapshotVersion: '1' }] : []
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

function stubReadonlyResource(resourceType: string) {
  const rows = resourceType === 'personnel-skill'
    ? [{ resourceType, code: 'SKILL-A', displayName: '焊接技能', active: true, snapshotVersion: '1' }]
    : []
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    resources: computed(() => rows),
    resourcesTotal: computed(() => rows.length),
    resourcesError: shallowRef(undefined),
    resourcesPending: shallowRef(false),
    refreshResources: vi.fn(),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useBusinessMasterDataResources: (resourceType: string) => stubReadonlyResource(resourceType),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('master-data organization page', () => {
  it('renders the title, five tabs, sample row and create button', async () => {
    const wrapper = mount(OrganizationPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('组织与人员')
    const tabs = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabs.some((t) => t.includes('部门'))).toBe(true)
    expect(tabs.some((t) => t.includes('班组'))).toBe(true)
    expect(tabs.some((t) => t.includes('班次'))).toBe(true)
    expect(tabs.some((t) => t.includes('工作日历'))).toBe(true)
    expect(tabs.some((t) => t.includes('人员技能'))).toBe(true)

    expect(wrapper.text()).toContain('总装部')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建部门'))).toBe(true)
  })
})
