import { mount } from '@vue/test-utils'
import { SidebarProvider } from '@nerv-iip/ui'
import { describe, expect, it } from 'vitest'
import { h } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import NavSide from './NavSide.vue'
import type { SideNav } from './types'

const groups: SideNav = [
  {
    label: '销售',
    items: [
      { title: '销售机会', to: { path: '/erp/sales' } },
      { title: '销售报价', to: { path: '/erp/sales/quotations' } },
      { title: '销售订单', to: { path: '/erp/sales/orders' } },
    ],
  },
  {
    label: '生产',
    items: [{ title: '生产工单', to: { path: '/mes/work-orders' } }],
  },
]

async function mountAt(path: string) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', name: 'catch-all', component: { template: '<div />' } }],
  })
  await router.push(path)
  await router.isReady()

  // SidebarMenuButton injects the sidebar context, so NavSide must mount inside a provider.
  const Host = {
    render: () => h(SidebarProvider, null, { default: () => h(NavSide, { groups }) }),
  }

  return mount(Host, { global: { plugins: [router] } })
}

/** Titles of the nav entries currently rendered as active. */
function activeTitles(wrapper: Awaited<ReturnType<typeof mountAt>>): string[] {
  return wrapper
    .findAll('[data-active="true"]')
    .map((el) => el.text().trim())
    .filter((text) => text.length > 0)
}

describe('NavSide active matching', () => {
  it('highlights only the exact entry, not its section landing page', async () => {
    const wrapper = await mountAt('/erp/sales/orders')

    expect(activeTitles(wrapper)).toEqual(['销售订单'])
  })

  it('highlights the section landing page on its own route', async () => {
    const wrapper = await mountAt('/erp/sales')

    expect(activeTitles(wrapper)).toEqual(['销售机会'])
  })

  it('keeps the list entry highlighted on a detail route that has no nav entry', async () => {
    const wrapper = await mountAt('/mes/work-orders/WO-2026-0001')

    expect(activeTitles(wrapper)).toEqual(['生产工单'])
  })

  it('highlights nothing when no entry covers the route', async () => {
    const wrapper = await mountAt('/quality/ncrs')

    expect(activeTitles(wrapper)).toEqual([])
  })
})
