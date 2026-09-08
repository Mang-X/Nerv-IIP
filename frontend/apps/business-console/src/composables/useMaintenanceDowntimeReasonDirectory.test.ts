import { configureApiClient } from '@nerv-iip/api-client'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, reactive } from 'vue'
import { useMaintenanceDowntimeReasonDirectory } from './useMaintenanceDowntimeReasonDirectory'
import { useMaintenanceWorkOrders } from './useBusinessMaintenance'
import { useBusinessContextStore } from '@/stores/businessContext'

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  confirmBusinessConsoleOperation: vi.fn(async (response) => response),
}))

// PublicContract / DomainInvariant: #3253 验收 1–3，#2964 v2 原值及 scope 合同。
describe('maintenance downtime reason directory', () => {
  afterEach(() => configureApiClient())

  function harness() {
    const requests: Array<{ url: URL; resolve: (response: Response) => void }> = []
    configureApiClient({
      baseUrl: 'http://maintenance.local',
      fetch: ((request: Request) =>
        new Promise<Response>((resolve) => {
          requests.push({ url: new URL(request.url), resolve })
        })) as typeof fetch,
    })
    const scope = reactive({ organizationId: 'org-a', environmentId: 'env-a' })
    let directory!: ReturnType<typeof useMaintenanceDowntimeReasonDirectory>
    const wrapper = mount(
      defineComponent({
        setup() {
          directory = useMaintenanceDowntimeReasonDirectory(scope)
          return () => h('div', directory.message.value)
        },
      }),
      { global: { plugins: [createPinia(), PiniaColada] } },
    )
    return { scope, directory, requests, wrapper }
  }

  it('queries the current scope and searches beyond the first page without rewriting codes', async () => {
    const { scope, directory, requests, wrapper } = harness()
    await flushPromises()
    expect(directory.state.value).toBe('loading')
    expect(requests[0]!.url.pathname).toContain('/directories/downtime-reason')
    expect(requests[0]!.url.searchParams.get('organizationId')).toBe('org-a')
    requests[0]!.resolve(
      Response.json({
        success: true,
        data: {
          items: [{ code: 'Line-A.Spindle', displayName: '主轴检修' }],
          total: 101,
        },
      }),
    )
    await flushPromises()
    expect(directory.options.value).toEqual([{ value: 'Line-A.Spindle', label: '主轴检修' }])
    expect(directory.total.value).toBe(101)
    directory.keyword.value = '液压'
    await flushPromises()
    expect(requests[1]!.url.searchParams.get('keyword')).toBe('液压')
    requests[1]!.resolve(Response.json({ success: true, data: { items: [], total: 0 } }))
    await flushPromises()
    expect(directory.state.value).toBe('empty')
    scope.environmentId = 'env-b'
    expect(directory.options.value).toEqual([])
    await flushPromises()
    expect(requests[2]!.url.searchParams.get('environmentId')).toBe('env-b')
    requests[2]!.resolve(Response.json({ success: true, data: { items: [], total: 0 } }))
    await flushPromises()
    wrapper.unmount()
  })

  it.each([403, 500, 200])(
    'distinguishes a %s failed read from an empty directory',
    async (status) => {
      const { directory, requests, wrapper } = harness()
      await flushPromises()
      requests[0]!.resolve(Response.json({ success: false }, { status }))
      await flushPromises()
      expect(directory.state.value).toBe(status === 403 ? 'forbidden' : 'failed')
      expect(directory.options.value).toEqual([])
      expect(directory.message.value).toContain(status === 403 ? '权限' : '读取失败')
      wrapper.unmount()
    },
  )

  it.each([
    { status: 401, message: 'Unauthorized', expected: '登录已过期，请重新登录。' },
    {
      status: 400,
      message: '当前工厂停机原因目录已停用，请联系设备主管',
      expected: '当前工厂停机原因目录已停用，请联系设备主管',
    },
    {
      status: 200,
      message: '当前工厂停机原因目录已停用，请联系设备主管',
      expected: '当前工厂停机原因目录已停用，请联系设备主管',
    },
  ])(
    'preserves the actionable reason from a $status rejected directory read',
    async ({ status, message, expected }) => {
      const { directory, requests, wrapper } = harness()
      await flushPromises()
      requests[0]!.resolve(Response.json({ success: false, message }, { status }))
      await flushPromises()
      expect(directory.state.value).toBe('failed')
      expect(directory.message.value).toBe(expected)
      expect(wrapper.text()).toBe(expected)
      expect(directory.options.value).toEqual([])
      wrapper.unmount()
    },
  )

  it('sends the real SDK mutation to v2 with a raw code or explicit null and refreshes the list', async () => {
    const posts: Array<{ url: string; body: Record<string, unknown> }> = []
    let reads = 0
    configureApiClient({
      baseUrl: 'http://maintenance.local',
      fetch: (async (request: Request) => {
        if (request.method === 'POST') {
          posts.push({ url: new URL(request.url).pathname, body: await request.json() })
          return Response.json({ success: true, data: { workOrderId: 'wo-created' } })
        }
        reads++
        return Response.json({ success: true, data: { items: [], total: 0 } })
      }) as typeof fetch,
    })
    const pinia = createPinia()
    useBusinessContextStore(pinia).patchContext({ organizationId: 'org-a', environmentId: 'env-a' })
    let orders!: ReturnType<typeof useMaintenanceWorkOrders>
    const wrapper = mount(
      defineComponent({
        setup() {
          orders = useMaintenanceWorkOrders()
          return () => h('div')
        },
      }),
      { global: { plugins: [pinia, PiniaColada] } },
    )
    await flushPromises()
    const initialReads = reads
    const body = {
      organizationId: 'org-a',
      environmentId: 'env-a',
      deviceAssetId: 'PRESS-01',
      priority: 'medium',
      openedBy: '张工',
      assetUnavailableReasonCode: 'Line-A.Spindle',
    }
    await orders.createWorkOrder(body)
    await orders.createWorkOrder({ ...body, assetUnavailableReasonCode: null })
    await flushPromises()
    expect(posts.map((post) => post.url)).toEqual([
      '/api/business-console/v2/maintenance/work-orders',
      '/api/business-console/v2/maintenance/work-orders',
    ])
    expect(posts.map((post) => post.body.assetUnavailableReasonCode)).toEqual([
      'Line-A.Spindle',
      null,
    ])
    expect(posts.every((post) => !('assetUnavailableReason' in post.body))).toBe(true)
    expect(reads).toBeGreaterThan(initialReads)
    wrapper.unmount()
  })
})
