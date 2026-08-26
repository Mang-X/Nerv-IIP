import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { computed, defineComponent, nextTick, shallowRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
  resolve: vi.fn(),
  search: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  resolveBusinessConsoleBarcode: api.resolve,
  searchBusinessConsoleObjects: api.search,
}))

vi.mock('@/composables/useWorkbenchHome', () => ({
  usePdaIdentity: () => ({
    organizationId: computed(() => 'org-1'),
    environmentId: computed(() => 'env-1'),
  }),
}))

import PdaBarcodeResolver from './PdaBarcodeResolver.vue'

function resolved(status: string, candidates: unknown[] = []) {
  return Promise.resolve({ data: { success: true, data: { status, candidates } } })
}

async function setup() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/mes/report', component: { template: '<div>report</div>' } },
      { path: '/mes/operation', component: { template: '<div>operation</div>' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const wrapper = mount(PdaBarcodeResolver, { global: { plugins: [router] } })
  return { wrapper, router }
}

async function setupLifecycleHost() {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { template: '<div />' } },
      { path: '/mes/report', component: { template: '<div>report</div>' } },
    ],
  })
  await router.push('/')
  await router.isReady()
  const visible = shallowRef(true)
  const LifecycleHost = defineComponent({
    components: { PdaBarcodeResolver },
    setup: () => ({ visible }),
    template: '<PdaBarcodeResolver v-if="visible" />',
  })
  const wrapper = mount(LifecycleHost, { global: { plugins: [router] } })

  return {
    router,
    wrapper,
    async hideResolver() {
      visible.value = false
      await nextTick()
    },
  }
}

async function scan(wrapper: VueWrapper, value: string) {
  const input = wrapper.get('input[placeholder^="扫描"]')
  await input.setValue(value)
  await input.trigger('keydown.enter')
}

describe('PdaBarcodeResolver', () => {
  beforeEach(() => vi.clearAllMocks())

  it('shows pending and directly navigates a unique supported candidate', async () => {
    let settle!: (value: unknown) => void
    api.resolve.mockReturnValue(new Promise((resolve) => (settle = resolve)))
    const { wrapper, router } = await setup()

    await scan(wrapper, 'WO-CODE')
    expect(wrapper.get('[data-testid="barcode-status"]').text()).toContain('正在解析')

    settle({
      data: {
        success: true,
        data: {
          status: 'resolved',
          candidates: [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } }],
        },
      },
    })
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-1')
  })

  it('requires manual selection when the server reports ambiguity', async () => {
    api.resolve.mockReturnValue(
      resolved('ambiguous', [
        { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } },
        {
          objectType: 'mes-operation',
          strongIds: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
        },
      ]),
    )
    const { wrapper, router } = await setup()

    await scan(wrapper, 'AMB')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/')
    expect(wrapper.get('[data-testid="barcode-status"]').text()).toContain('多个候选')

    await wrapper.get('[data-testid="barcode-candidate-1"]').trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(
      '/mes/operation?workOrderId=WO-1&operationTaskId=OP-1',
    )
  })

  it('offers authorized server candidate search for unknown codes without navigating', async () => {
    api.resolve.mockReturnValue(resolved('unknown'))
    api.search.mockResolvedValue({
      data: {
        success: true,
        data: {
          results: [
            {
              objectType: 'mes-work-order',
              title: '工单 WO-9',
              objectNumber: 'WO-9',
              route: '/pc',
            },
          ],
        },
      },
    })
    const { wrapper, router } = await setup()

    await scan(wrapper, 'UNKNOWN-9')
    await flushPromises()
    expect(wrapper.get('[data-testid="barcode-status"]').text()).toContain('无法确认')
    await wrapper.get('[data-testid="barcode-search"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="barcode-search-results"]').text()).toContain('仅供核对的候选')
    expect(wrapper.text()).toContain('工单 WO-9')
    expect(wrapper.find('a[href="/pc"]').exists()).toBe(false)
    expect(router.currentRoute.value.fullPath).toBe('/')
  })

  it('distinguishes forbidden and unsupported outcomes', async () => {
    api.resolve.mockRejectedValueOnce({ response: { status: 403 } })
    const { wrapper } = await setup()
    await scan(wrapper, 'DENIED')
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('无权解析')

    api.resolve.mockReturnValueOnce(
      resolved('resolved', [
        { objectType: 'inventory-location', strongIds: { inventoryLocationId: 'LOC-1' } },
      ]),
    )
    await scan(wrapper, 'UNSUPPORTED')
    await flushPromises()
    expect(wrapper.get('[data-testid="barcode-status"]').text()).toContain('暂不支持直达')
  })

  it('does not navigate when a pending resolve completes after unmount', async () => {
    let settle!: (value: unknown) => void
    api.resolve.mockReturnValue(new Promise((resolve) => (settle = resolve)))
    const { hideResolver, wrapper, router } = await setupLifecycleHost()

    await scan(wrapper, 'WO-LATE')
    await hideResolver()
    settle({
      data: {
        success: true,
        data: {
          status: 'resolved',
          candidates: [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-LATE' } }],
        },
      },
    })
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/')
  })

  it('fails closed with a recoverable alert when navigation rejects', async () => {
    api.resolve.mockReturnValue(
      resolved('resolved', [
        { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-NAV-FAIL' } },
      ]),
    )
    const { wrapper, router } = await setup()
    vi.spyOn(router, 'push').mockRejectedValueOnce(new Error('目标页面懒加载失败'))

    await scan(wrapper, 'WO-NAV-FAIL')
    await flushPromises()

    expect(wrapper.get('[data-testid="barcode-status"]').attributes('role')).toBe('alert')
    expect(wrapper.get('[data-testid="barcode-status"]').text()).toContain('无法打开目标页面')
    expect(wrapper.get('input[placeholder^="扫描"]').attributes('disabled')).toBeUndefined()

    await scan(wrapper, 'WO-NAV-FAIL')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NAV-FAIL')
  })

  it('ignores an older navigation rejection after a newer scan navigates successfully', async () => {
    api.resolve
      .mockReturnValueOnce(
        resolved('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-OLD' } },
        ]),
      )
      .mockReturnValueOnce(
        resolved('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-NEW' } },
        ]),
      )
    const { wrapper, router } = await setup()
    const actualPush = router.push.bind(router)
    let rejectFirst!: (reason?: unknown) => void
    vi.spyOn(router, 'push')
      .mockImplementationOnce(
        () =>
          new Promise((_resolve, reject) => {
            rejectFirst = reject
          }),
      )
      .mockImplementation(actualPush)

    await scan(wrapper, 'WO-OLD')
    await flushPromises()
    await scan(wrapper, 'WO-NEW')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NEW')

    rejectFirst(new Error('旧导航懒加载失败'))
    await flushPromises()
    expect(wrapper.get('[data-testid="barcode-status"]').attributes('role')).toBe('status')
    expect(wrapper.get('[data-testid="barcode-status"]').text()).not.toContain('无法打开目标页面')
  })
})
