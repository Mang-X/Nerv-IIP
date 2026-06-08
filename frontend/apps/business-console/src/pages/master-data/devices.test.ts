import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DevicesPage from './devices.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn(),
}))

function stubResource(resourceType: string) {
  const rows = resourceType === 'device-asset'
    ? [{ resourceType: 'device-asset', code: 'EQ-01', displayName: '焊接机器人', active: true, snapshotVersion: '1' }]
    : []
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

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('master-data devices page', () => {
  it('renders the title, sample row and create button', async () => {
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('设备台账')
    expect(wrapper.text()).toContain('焊接机器人')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建设备'))).toBe(true)
  })
})
