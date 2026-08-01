import { flushPromises, mount } from '@vue/test-utils'
import { computed, defineComponent } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const permissions = vi.hoisted(() => ({ maintenanceRead: true, masterDataRead: true }))

vi.mock('@/composables/useWorkbenchHome', () => ({
  HOME_PERMISSIONS: {
    mesOperations: 'business.mes.operations.read',
    quality: 'business.quality.inspection-records.read',
    wmsReceipts: 'business.wms.receipts.read',
    wmsShipments: 'business.wms.shipments.read',
    wmsCounts: 'business.wms.counts.read',
    maintenanceWorkOrders: 'business.maintenance.work-orders.read',
    masterDataResources: 'business.masterdata.resources.read',
  },
  usePdaIdentity: () => ({
    permissionCodes: computed(
      () =>
        new Set(
          [
            permissions.maintenanceRead ? 'business.maintenance.work-orders.read' : undefined,
            permissions.masterDataRead ? 'business.masterdata.resources.read' : undefined,
          ].filter((code): code is string => Boolean(code)),
        ),
    ),
    can: (permission: string) =>
      permission.includes('quality') ||
      permission.includes('mes') ||
      (permissions.maintenanceRead && permission.includes('maintenance')) ||
      (permissions.masterDataRead && permission.includes('masterdata')),
  }),
  useMyDispatchTasks: () => {
    throw new Error('the task hub must not consume client-filtered personal dispatch facts')
  },
}))

import TasksPage from './tasks.vue'

describe('PDA tasks page', () => {
  beforeEach(() => {
    permissions.maintenanceRead = true
    permissions.masterDataRead = true
  })

  it('uses truthful scoped task entrances without claiming client-filtered MES rows are personal', async () => {
    const target = defineComponent({ template: '<div>target</div>' })
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/tasks', component: TasksPage },
        { path: '/mes/operation', component: target },
        { path: '/quality/tasks', component: target },
        { path: '/equipment/work-orders', component: target },
      ],
    })
    await router.push('/tasks')
    await router.isReady()
    const wrapper = mount(TasksPage, { global: { plugins: [router] } })

    expect(wrapper.text()).toContain('生产作业')
    expect(wrapper.text()).toContain('我的质检任务')
    expect(wrapper.text()).toContain('维修工单')
    expect(wrapper.text()).toContain('查看分派给当前维修人员的工单与设备位置')
    expect(wrapper.text()).not.toContain('Self')
    expect(wrapper.text()).not.toContain('服务端')
    expect(wrapper.text()).not.toContain('我的维修工单')
    expect(wrapper.text()).not.toContain('我的生产任务')
    expect(wrapper.text()).not.toContain('暂无派给我的生产任务')
    expect(wrapper.text()).not.toContain('仓储任务')

    const production = wrapper.get('a[href="/mes/operation"]')
    expect(production.text()).toContain('生产作业')
    expect(production.attributes('aria-label')).toBe('生产作业，查看当前账号可执行的生产作业')

    const maintenance = wrapper.get('a[href="/equipment/work-orders"]')
    expect(maintenance.text()).toContain('维修工单')

    await production.trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/operation')
  })

  it('hides the maintenance self queue without the maintenance read permission', async () => {
    permissions.maintenanceRead = false
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/tasks', component: TasksPage }],
    })
    await router.push('/tasks')
    await router.isReady()

    const wrapper = mount(TasksPage, { global: { plugins: [router] } })

    expect(wrapper.find('a[href="/equipment/work-orders"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('维修任务')
  })

  it('hides the maintenance self queue when device location read permission is unavailable', async () => {
    permissions.masterDataRead = false
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/tasks', component: TasksPage }],
    })
    await router.push('/tasks')
    await router.isReady()

    const wrapper = mount(TasksPage, { global: { plugins: [router] } })

    expect(wrapper.find('a[href="/equipment/work-orders"]').exists()).toBe(false)
  })
})
