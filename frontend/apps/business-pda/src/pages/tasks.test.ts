import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@/composables/useWorkbenchHome', () => ({
  HOME_PERMISSIONS: {
    mesOperations: 'business.mes.operations.read',
    quality: 'business.quality.inspection-records.read',
    wmsReceipts: 'business.wms.receipts.read',
    wmsShipments: 'business.wms.shipments.read',
    wmsCounts: 'business.wms.counts.read',
  },
  usePdaIdentity: () => ({
    can: (permission: string) => permission.includes('quality') || permission.includes('mes'),
  }),
  useMyDispatchTasks: () => {
    throw new Error('the task hub must not consume client-filtered personal dispatch facts')
  },
}))

import TasksPage from './tasks.vue'

describe('PDA tasks page', () => {
  it('uses truthful scoped task entrances without claiming client-filtered MES rows are personal', async () => {
    const target = defineComponent({ template: '<div>target</div>' })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/tasks', component: TasksPage },
        { path: '/mes/operation', component: target },
        { path: '/quality/tasks', component: target },
      ],
    })
    await router.push('/tasks')
    await router.isReady()
    const wrapper = mount(TasksPage, { global: { plugins: [router] } })

    expect(wrapper.text()).toContain('生产作业')
    expect(wrapper.text()).toContain('我的质检任务')
    expect(wrapper.text()).not.toContain('我的生产任务')
    expect(wrapper.text()).not.toContain('暂无派给我的生产任务')
    expect(wrapper.text()).not.toContain('仓储任务')

    const production = wrapper.get('a[href="/mes/operation"]')
    expect(production.text()).toContain('生产作业')
    expect(production.attributes('aria-label')).toBe('生产作业，服务端按当前主体与授权作业范围过滤')

    await production.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/operation')
  })
})
