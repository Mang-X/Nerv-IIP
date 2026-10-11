import { flushPromises, mount } from '@vue/test-utils'
import { computed, defineComponent, h, ref } from 'vue'
import { afterEach, describe, expect, it } from 'vitest'
import Sidebar from './NvSidebar.vue'
import { provideSidebarContext } from '../../ui/sidebar/utils'

afterEach(() => {
  document.body.innerHTML = ''
})

describe('移动侧栏中文读屏', () => {
  it('暴露中文标题、描述和可操作的关闭按钮', async () => {
    const openMobile = ref(true)
    const wrapper = mount(
      defineComponent({
        setup() {
          provideSidebarContext({
            state: computed(() => 'expanded'),
            open: ref(true),
            setOpen: () => {},
            isMobile: ref(true),
            openMobile,
            setOpenMobile: (value) => {
              openMobile.value = value
            },
            toggleSidebar: () => {},
          })
          return () =>
            h(Sidebar, {}, { default: () => h('a', { href: '/mes/work-orders' }, '工单与派工') })
        },
      }),
      { attachTo: document.body },
    )
    await flushPromises()
    const dialog = document.querySelector('[role="dialog"]')!
    expect(document.getElementById(dialog.getAttribute('aria-labelledby')!)?.textContent).toBe(
      '导航侧栏',
    )
    expect(document.getElementById(dialog.getAttribute('aria-describedby')!)?.textContent).toBe(
      '显示移动端导航菜单。',
    )
    const close = dialog.querySelector<HTMLButtonElement>('button[aria-label="关闭"]')!
    expect(close).not.toBeNull()
    close.click()
    await flushPromises()
    expect(openMobile.value).toBe(false)
    wrapper.unmount()
  })
})
