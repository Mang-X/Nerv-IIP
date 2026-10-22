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
  beforeEach(() => vi.resetAllMocks())

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

  it('fails closed when router resolves a prevented navigation', async () => {
    api.resolve.mockReturnValue(
      resolved('resolved', [
        { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-BLOCKED' } },
      ]),
    )
    const { wrapper, router } = await setup()
    const removeGuard = router.beforeEach(() => false)

    await scan(wrapper, 'WO-BLOCKED')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/')
    expect(wrapper.get('[data-testid="barcode-status"]').attributes('role')).toBe('alert')
    expect(wrapper.get('[data-testid="barcode-status"]').text()).toContain('无法打开目标页面')

    removeGuard()
    await scan(wrapper, 'WO-BLOCKED')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-BLOCKED')
  })

  it('blocks a newer scan while the previous navigation is still pending', async () => {
    api.resolve
      .mockReturnValueOnce(
        resolved('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-NAV-PENDING' } },
        ]),
      )
      .mockReturnValueOnce(
        resolved('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-MUST-NOT-START' } },
        ]),
      )
    const { wrapper, router } = await setup()
    let releaseNavigation!: (allow: boolean) => void
    router.beforeEach(
      () =>
        new Promise<boolean>((resolve) => {
          releaseNavigation = resolve
        }),
    )

    await scan(wrapper, 'WO-NAV-PENDING')
    await vi.waitFor(() => expect(releaseNavigation).toBeTypeOf('function'))

    const input = wrapper.get('input[placeholder^="扫描"]')
    await scan(wrapper, 'WO-MUST-NOT-START')
    expect({
      inputDisabled: (input.element as HTMLInputElement).matches(':disabled'),
      resolveCalls: api.resolve.mock.calls.length,
    }).toEqual({ inputDisabled: true, resolveCalls: 1 })

    releaseNavigation(true)
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NAV-PENDING')
  })

  it('lets a newer scan replace a pending resolve and ignores the older success', async () => {
    let settleOlder!: (value: unknown) => void
    api.resolve
      .mockReturnValueOnce(new Promise((resolve) => (settleOlder = resolve)))
      .mockReturnValueOnce(
        resolved('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-NEWER' } },
        ]),
      )
    const { wrapper, router } = await setup()

    await scan(wrapper, 'WO-OLDER')
    await scan(wrapper, 'WO-NEWER')
    await flushPromises()

    expect(api.resolve).toHaveBeenCalledTimes(2)
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NEWER')

    settleOlder({
      data: {
        success: true,
        data: {
          status: 'resolved',
          candidates: [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-OLDER' } }],
        },
      },
    })
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NEWER')
  })

  it('lets a newer scan replace a pending resolve and ignores the older rejection', async () => {
    let rejectOlder!: (reason?: unknown) => void
    api.resolve
      .mockReturnValueOnce(
        new Promise((_resolve, reject) => {
          rejectOlder = reject
        }),
      )
      .mockReturnValueOnce(
        resolved('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-NEWER' } },
        ]),
      )
    const { wrapper, router } = await setup()

    await scan(wrapper, 'WO-OLDER')
    await scan(wrapper, 'WO-NEWER')
    await flushPromises()

    expect(api.resolve).toHaveBeenCalledTimes(2)
    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NEWER')

    rejectOlder(new Error('旧解析请求失败'))
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/mes/report?workOrderId=WO-NEWER')
    expect(wrapper.get('[data-testid="barcode-status"]').attributes('role')).toBe('status')
    expect(wrapper.get('[data-testid="barcode-status"]').text()).not.toContain('解析服务暂不可用')
  })
})
