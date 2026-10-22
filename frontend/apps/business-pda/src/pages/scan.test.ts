import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { computed } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const resolveBarcode = vi.hoisted(() => vi.fn())
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  resolveBusinessConsoleBarcode: resolveBarcode,
}))

vi.mock('@/composables/useWorkbenchHome', () => ({
  usePdaIdentity: () => ({
    organizationId: computed(() => 'org-1'),
    environmentId: computed(() => 'env-1'),
    can: (permission: string) => permission.includes('reporting'),
  }),
}))

import ScanPage from './scan.vue'

describe('PDA scan page', () => {
  it('shares strong-ID resolution and only offers permitted work', async () => {
    resolveBarcode.mockResolvedValue({
      data: {
        success: true,
        data: {
          status: 'resolved',
          candidates: [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } }],
        },
      },
    })
    const target = defineComponent({ template: '<div>target</div>' })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/scan', component: ScanPage },
        { path: '/mes/report', component: target },
      ],
    })
    await router.push('/scan')
    await router.isReady()
    const wrapper = mount(ScanPage, { global: { plugins: [router] } })
    const input = wrapper.get('input[placeholder^="扫描"]')

    await input.setValue('WO-2026-00001')
    await input.trigger('keydown.enter')

    await flushPromises()
    expect(wrapper.text()).toContain('生产报工')
    expect(wrapper.text()).not.toContain('收货入库')
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-1')
  })

  it('renders a permitted work entrance as a real, named link', async () => {
    const target = defineComponent({ template: '<div>target</div>' })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/scan', component: ScanPage },
        { path: '/mes/report', component: target },
      ],
    })
    await router.push('/scan')
    await router.isReady()
    const wrapper = mount(ScanPage, { global: { plugins: [router] } })
    const productionReport = wrapper.get('a[href="/mes/report"]')

    expect(productionReport.attributes('aria-label')).toBe('生产报工')
    await productionReport.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/report')
  })
})
