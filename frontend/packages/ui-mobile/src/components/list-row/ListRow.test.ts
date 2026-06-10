import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ListRow from './ListRow.vue'

describe('ListRow', () => {
  it('renders title and subtitle', () => {
    const wrapper = mount(ListRow, { props: { title: 'RO-2026-001', subtitle: '待收货 · 3 行' } })
    expect(wrapper.text()).toContain('RO-2026-001')
    expect(wrapper.text()).toContain('待收货 · 3 行')
  })

  it('emits select when activated and meets the touch height baseline', async () => {
    const wrapper = mount(ListRow, { props: { title: 'X' } })
    expect(wrapper.get('[data-row]').classes()).toContain('min-h-row')
    await wrapper.get('[data-row]').trigger('click')
    expect(wrapper.emitted('select')).toBeTruthy()
  })

  it('shows the chevron only when interactive', () => {
    const plain = mount(ListRow, { props: { title: 'X', interactive: false } })
    expect(plain.find('[data-chevron]').exists()).toBe(false)
  })
})
