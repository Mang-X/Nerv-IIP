import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AppShellInset from './AppShellInset.vue'

const passthrough = { template: '<div><slot /></div>' }

describe('NvAppShellInset', () => {
  it('allows wide workbench content to shrink inside the sidebar layout', () => {
    const wrapper = mount(AppShellInset, {
      global: {
        stubs: {
          Sidebar: passthrough,
          SidebarContent: passthrough,
          SidebarFooter: passthrough,
          SidebarHeader: passthrough,
          SidebarInset: {
            props: ['class'],
            template: '<main data-test="sidebar-inset" :class="$props.class"><slot /></main>',
          },
          SidebarProvider: passthrough,
          SidebarRail: true,
          SidebarTrigger: true,
          Separator: true,
        },
      },
    })

    expect(wrapper.get('[data-test="sidebar-inset"]').classes()).toContain('min-w-0')
  })
})
