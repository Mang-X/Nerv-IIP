import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter, RouterLink } from 'vue-router'
import AppShell from './AppShell.vue'

describe('AppShell', () => {
  it('renders brand and navigation items with RouterLink route locations', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { component: { template: '<div />' }, name: 'home', path: '/' },
        {
          component: { template: '<div />' },
          name: 'operation-detail',
          path: '/operations/:operationTaskId',
        },
      ],
    })

    router.push('/')
    await router.isReady()

    const operationTo = {
      name: 'operation-detail',
      params: { operationTaskId: 'task-42' },
    }

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
      props: {
        navItems: [{ label: 'Operation detail', to: operationTo }],
        title: 'Nerv-IIP',
      },
    })

    const links = wrapper.findAllComponents(RouterLink)
    expect(links).toHaveLength(2)
    expect(links[0].props('to')).toEqual({ path: '/' })
    expect(links[1].props('to')).toEqual(operationTo)

    await links[1].find('a').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/operations/task-42')
  })
})
