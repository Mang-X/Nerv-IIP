import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppShellMobile from './AppShellMobile.vue'

describe('AppShellMobile', () => {
  it('renders header / content / footer slots', () => {
    const wrapper = mount(AppShellMobile, {
      slots: {
        header: '<div>标题栏</div>',
        default: '<div>内容</div>',
        footer: '<nav>底部导航</nav>',
      },
    })
    expect(wrapper.text()).toContain('标题栏')
    expect(wrapper.text()).toContain('内容')
    expect(wrapper.text()).toContain('底部导航')
  })

  it('applies top safe-area on header and bottom safe-area on footer', () => {
    const wrapper = mount(AppShellMobile, {
      slots: { header: '<div>H</div>', footer: '<div>F</div>' },
    })
    expect(wrapper.get('[data-shell="header"]').classes()).toContain('pt-safe')
    expect(wrapper.get('[data-shell="footer"]').classes()).toContain('pb-safe')
  })

  it('omits footer region when no footer slot is provided', () => {
    const wrapper = mount(AppShellMobile, { slots: { default: '<div>X</div>' } })
    expect(wrapper.find('[data-shell="footer"]').exists()).toBe(false)
  })
})
