import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))

vi.mock('@/composables/useWorkbenchHome', () => ({
  usePdaIdentity: () => ({ can: (permission: string) => permission.includes('reporting') }),
}))

import ScanPage from './scan.vue'

describe('PDA scan page', () => {
  it('captures a code without pretending it was resolved and only offers permitted work', async () => {
    const wrapper = mount(ScanPage)
    const input = wrapper.get('input[placeholder^="扫描"]')

    await input.setValue('WO-2026-00001')
    await input.trigger('keydown.enter')

    expect(wrapper.get('[data-testid="scan-result"]').text()).toContain('WO-2026-00001')
    expect(wrapper.text()).toContain('生产报工')
    expect(wrapper.text()).not.toContain('收货入库')
    expect(push).not.toHaveBeenCalled()
  })
})
