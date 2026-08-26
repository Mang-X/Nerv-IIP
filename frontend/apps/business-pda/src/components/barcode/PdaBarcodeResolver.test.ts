import { flushPromises, mount } from '@vue/test-utils'
import { computed } from 'vue'
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

async function scan(wrapper: Awaited<ReturnType<typeof setup>>['wrapper'], value: string) {
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
})
