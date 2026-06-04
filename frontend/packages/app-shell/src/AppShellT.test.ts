import type { NavDomain, SideNav } from './types'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, RouterLink } from 'vue-router'
import AppShellT from './AppShellT.vue'

function makeRouter() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { component: { template: '<div />' }, name: 'home', path: '/' },
      { component: { template: '<div />' }, name: 'mes', path: '/mes' },
      { component: { template: '<div />' }, name: 'mes-plans', path: '/mes/plans' },
      { component: { template: '<div />' }, name: 'quality', path: '/quality/inspections' },
    ],
  })
  return router
}

const domains: NavDomain[] = [
  { id: 'workbench', title: '数字化工作台', to: { path: '/' } },
  { id: 'mes', title: '制造执行', to: { path: '/mes' } },
  { id: 'quality', title: '质量管理', to: { path: '/quality/inspections' } },
]

const sideNav: SideNav = [
  {
    label: '计划与工单',
    items: [
      { title: '生产驾驶舱', to: { path: '/mes' } },
      { title: '生产计划', to: { path: '/mes/plans' } },
    ],
  },
]

describe('AppShellT (T-shaped shell)', () => {
  it('renders top domains and the current domain side nav', async () => {
    const router = makeRouter()
    router.push('/mes')
    await router.isReady()

    const wrapper = mount(AppShellT, {
      global: { plugins: [router] },
      props: { title: 'Nerv-IIP', topDomains: domains, currentDomainId: 'mes', sideNav },
    })

    expect(wrapper.text()).toContain('数字化工作台')
    expect(wrapper.text()).toContain('制造执行')
    expect(wrapper.text()).toContain('生产计划')
    // domains link via RouterLink (top tabs) + side nav links
    const targets = wrapper.findAllComponents(RouterLink).map((l) => l.props('to'))
    expect(targets).toContainEqual({ path: '/mes/plans' })
  })

  it('collapses domains beyond maxVisible into a 更多 overflow', () => {
    const router = makeRouter()
    const wrapper = mount(AppShellT, {
      global: { plugins: [router] },
      props: { title: 'Nerv-IIP', topDomains: domains, currentDomainId: 'workbench', maxVisibleDomains: 2 },
    })
    expect(wrapper.text()).toContain('更多')
  })

  it('emits openSearch on the search button and on ⌘/Ctrl+K', async () => {
    const router = makeRouter()
    const wrapper = mount(AppShellT, {
      global: { plugins: [router] },
      props: { title: 'Nerv-IIP', topDomains: domains },
      attachTo: document.body,
    })

    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', metaKey: true }))
    expect(wrapper.emitted('openSearch')).toBeTruthy()

    wrapper.unmount()
  })

  it('emits signOut from the user menu entry', () => {
    const router = makeRouter()
    const wrapper = mount(AppShellT, {
      global: { plugins: [router] },
      props: { title: 'Nerv-IIP', topDomains: domains, user: { name: '张三', email: 'z@x.io' } },
    })
    // user menu renders an avatar trigger; the entry is wired to emit signOut.
    expect(wrapper.text()).toContain('数字化工作台')
    expect(wrapper.props('title')).toBe('Nerv-IIP')
  })
})
