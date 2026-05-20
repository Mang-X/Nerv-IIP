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

  it('renders grouped navigation children with RouterLink route locations', async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { component: { template: '<div />' }, name: '/', path: '/' },
        { component: { template: '<div />' }, name: '/iam/users/', path: '/iam/users' },
        { component: { template: '<div />' }, name: '/iam/roles/', path: '/iam/roles' },
      ],
    })

    router.push('/')
    await router.isReady()

    const wrapper = mount(AppShell, {
      global: {
        plugins: [router],
      },
      props: {
        navItems: [
          {
            label: 'IAM',
            children: [
              { label: 'Users', to: { name: '/iam/users/' } },
              { label: 'Roles', to: { name: '/iam/roles/' } },
            ],
          },
        ],
        title: 'Nerv-IIP',
      },
    })

    expect(wrapper.text()).toContain('IAM')
    expect(wrapper.findAllComponents(RouterLink).map((link) => link.props('to'))).toContainEqual({
      name: '/iam/users/',
    })
  })
})
