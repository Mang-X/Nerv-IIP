import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { useWmsInbound } from '.'
import { useWmsWorkScope } from './useWmsWorkScope'

const requestCatalog = vi.hoisted(() => vi.fn<() => Promise<unknown>>())
const requestOrders = vi.hoisted(() => vi.fn<(scopeId: string) => Promise<unknown>>())

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions: () => ({
    key: ['receiptWorkScopes', 'org-001', 'env-dev'],
    query: requestCatalog,
  }),
  listBusinessConsoleWmsInboundOrdersQueryOptions: ({ query }: { query: { scopeId: string } }) => ({
    key: ['receiptOrders', query.scopeId ?? ''],
    query: () => requestOrders(query.scopeId),
  }),
}))

function catalogResponse(employee: string) {
  return {
    success: true,
    data: {
      actorPrincipalId: employee,
      items: [
        { scopeKind: 'self', scopeId: employee },
        { scopeKind: 'work-pool', scopeId: 'SHARED', displayName: '共同收货池' },
      ],
    },
  }
}

it('换岗立即清空上岗范围，晚到目录无法覆盖新账号的默认选择', async () => {
  const pending: Array<(value: unknown) => void> = []
  requestCatalog.mockImplementation(() => new Promise((resolve) => pending.push(resolve)))
  const orderRequests: Array<{ scopeId: string; resolve: (value: unknown) => void }> = []
  requestOrders.mockImplementation(
    (scopeId) => new Promise((resolve) => orderRequests.push({ scopeId, resolve })),
  )
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.principal = {
    principalId: 'operator-a',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  } as never
  auth.sessionId = 'shift-a'
  let scope!: ReturnType<typeof useWmsWorkScope>
  let inbound!: ReturnType<typeof useWmsInbound>
  const wrapper = mount(
    defineComponent({
      setup() {
        scope = useWmsWorkScope('receipts')
        inbound = useWmsInbound()
        return () => h('div', scope.scopeKey.value ?? '')
      },
    }),
    { global: { plugins: [pinia, PiniaColada] } },
  )

  try {
    await flushPromises()
    expect(pending).toHaveLength(1)
    pending[0]!(catalogResponse('employee-a'))
    await flushPromises()
    scope.scopeKey.value = 'work-pool:SHARED'
    expect(scope.scopeId.value).toBe('SHARED')
    await flushPromises()
    const previousOrders = orderRequests.find((request) => request.scopeId === 'SHARED')
    expect(previousOrders).toBeDefined()

    void scope.refresh()
    await flushPromises()
    expect(pending).toHaveLength(2)

    auth.principal = {
      principalId: 'operator-b',
      organizationId: 'org-001',
      environmentId: 'env-dev',
    } as never
    auth.sessionId = 'shift-b'
    expect(scope.scopeKey.value).toBeUndefined()
    expect(scope.hasSelection.value).toBe(false)
    await flushPromises()
    expect(pending).toHaveLength(3)

    pending[1]!(catalogResponse('employee-a'))
    await flushPromises()
    expect(scope.scopeOptions.value).toEqual([])
    expect(scope.hasSelection.value).toBe(false)

    pending[2]!(catalogResponse('employee-b'))
    await flushPromises()
    expect(scope.scopeKey.value).toBe('self:employee-b')
    expect(scope.scopeId.value).toBe('employee-b')

    scope.scopeKey.value = 'work-pool:SHARED'
    await flushPromises()
    const sharedPoolRequests = orderRequests.filter((request) => request.scopeId === 'SHARED')
    expect(sharedPoolRequests).toHaveLength(2)
    previousOrders!.resolve({
      success: true,
      data: { items: [{ inboundOrderId: 'old-operator-order' }], total: 1 },
    })
    await flushPromises()
    expect(inbound.orders.value).toEqual([])
    sharedPoolRequests[1]!.resolve({
      success: true,
      data: { items: [{ inboundOrderId: 'current-operator-order' }], total: 1 },
    })
    await flushPromises()
    expect(inbound.orders.value.map((order) => order.inboundOrderId)).toEqual([
      'current-operator-order',
    ])
  } finally {
    wrapper.unmount()
  }
})
