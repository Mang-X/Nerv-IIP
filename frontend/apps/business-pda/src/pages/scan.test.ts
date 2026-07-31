import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@/composables/useWorkbenchHome', () => ({
  usePdaIdentity: () => ({ can: (permission: string) => permission.includes('reporting') }),
}))

import ScanPage from './scan.vue'

describe('PDA scan page', () => {
  it('captures a code without pretending it was resolved and only offers permitted work', async () => {
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

    expect(wrapper.get('[data-testid="scan-result"]').text()).toContain('WO-2026-00001')
    expect(wrapper.text()).toContain('生产报工')
    expect(wrapper.text()).not.toContain('收货入库')
    expect(router.currentRoute.value.fullPath).toBe('/scan')
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
