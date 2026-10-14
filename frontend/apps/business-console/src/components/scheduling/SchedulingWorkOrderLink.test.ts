import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import SchedulingWorkOrderLink from './SchedulingWorkOrderLink.vue'

async function setup(props: { workOrderId?: string; operationTaskId?: string }, canRead = true) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'planner',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'planner',
      permissionCodes: canRead ? ['business.mes.work-orders.read'] : [],
    },
  })
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/:pathMatch(.*)*', component: { template: '<div />' } }],
  })
  await router.push('/scheduling')
  return {
    router,
    auth,
    wrapper: mount(SchedulingWorkOrderLink, {
      props,
      global: { plugins: [pinia, router] },
    }),
  }
}

// DomainInvariant: #4419 的无来源、工单级来源和目标权限边界。
describe('排产工单接续', () => {
  it('工单级入口只传工单，不猜测来源工序', async () => {
    const { wrapper, router } = await setup({ workOrderId: 'WO-260014' })
    await wrapper.get('a').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/mes/work-orders/WO-260014')
    expect(router.currentRoute.value.query).toEqual({})
    wrapper.unmount()
  })

  it('无工单来源时不生成链接，即使有工序标识', async () => {
    const { wrapper } = await setup({ operationTaskId: 'OP-20' })
    expect(wrapper.find('a').exists()).toBe(false)
    wrapper.unmount()
  })

  it('无目标读权限保持编号文本，权限恢复后可进入同 WO/OP', async () => {
    const { wrapper, auth, router } = await setup(
      {
        workOrderId: 'WO-260014',
        operationTaskId: 'WO-260014-OP-20',
      },
      false,
    )
    expect(wrapper.text()).toBe('WO-260014')
    expect(wrapper.find('a').exists()).toBe(false)
    auth.principal = { ...auth.principal!, permissionCodes: ['business.mes.work-orders.read'] }
    await flushPromises()
    await wrapper.get('a').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.path).toBe('/mes/work-orders/WO-260014')
    expect(router.currentRoute.value.query).toEqual({ operationTaskId: 'WO-260014-OP-20' })
    wrapper.unmount()
  })
})
