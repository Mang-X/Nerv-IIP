import { configureApiClient } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
import { deviceIdBatches } from '@/data/real/deviceBatches'
import HallMetric from './HallMetric.vue'

const session = { organizationId: 'o', environmentId: 'e' }
afterEach(() => {
  configureApiClient({})
  vi.useRealTimers()
})

it('按当前组织读取活动报警总数，过期保留最后样本，无权清空，重试接受零', async () => {
  vi.useFakeTimers()
  let status = 200
  let total = 31
  const requests: URL[] = []
  configureApiClient({
    baseUrl: 'http://fixture',
    fetch: async (input) => {
      requests.push(new URL(new Request(input).url))
      return Response.json({ success: status === 200, data: { items: [{}], total } }, { status })
    },
  })
  const page = mount(HallMetric, { props: { label: '未恢复报警', metric: 'alarms', session } })
  try {
    expect(page.text()).toContain('读取中')
    await flushPromises()
    expect(page.get('strong').text()).toBe('31')
    expect(Object.fromEntries(requests[0].searchParams)).toEqual({
      ...session,
      status: 'active',
      take: '1',
      skip: '0',
    })
    const stamp = page.get('small').text().split('最后更新 ')[1]
    status = 503
    await vi.advanceTimersByTimeAsync(15000)
    expect(page.text()).toContain('数据滞留')
    expect(page.text()).toContain(stamp)
    status = 403
    await page.get('button').trigger('click')
    await flushPromises()
    expect(page.text()).toContain('无权查看')
    expect(page.get('strong').text()).toBe('—')
    total = 0
    status = 200
    await page.get('button').trigger('click')
    await flushPromises()
    expect(page.get('strong').text()).toBe('0')
    expect(page.find('button').exists()).toBe(false)
  } finally {
    page.unmount()
  }
})

function deviceScopeResponse(url: URL) {
  if (!url.pathname.endsWith('/me/work-context')) return
  const q = url.searchParams
  if (
    q.has('scopeKind') ||
    q.has('scopeId') ||
    !['business.masterdata.resources.read', 'business.iiot.telemetry.read'].includes(
      q.get('permissionCode') ?? '',
    )
  )
    return new Response(null, { status: 403 })
  return Response.json({
    success: true,
    data: { authorizedScopes: [{ kind: 'organization', id: 'o' }] },
  })
}

it('真实 UUID 的 28 台请求拆批，同时执行数量上限', () => {
  const ids = Array.from(
    { length: 28 },
    (_, i) => `00000000-0000-0000-0000-${String(i + 1).padStart(12, '0')}`,
  )
  expect(deviceIdBatches(ids).map((csv) => csv.split(',').length)).toEqual([27, 1])
  expect(
    deviceIdBatches(Array.from({ length: 51 }, (_, i) => String(i))).map(
      (csv) => csv.split(',').length,
    ),
  ).toEqual([50, 1])
})

it('台账跨页时不能把首批设备当作组织总数', async () => {
  let requests = 0
  configureApiClient({
    baseUrl: 'http://fixture',
    fetch: async (input) => {
      const url = new URL(new Request(input).url)
      const scope = deviceScopeResponse(url)
      if (scope) return scope
      requests++
      if (url.pathname.endsWith('/overview')) {
        expect(url.searchParams.get('deviceAssetIds')).toBe('a')
        return Response.json({success:true,data:{devices:[{deviceAssetId:'a',currentState:'running',isSourceFresh:true,activeAlarmCount:0}]}})
      }
      return Response.json({
        success: true,
        data: { resources: [{ deviceAssetId: 'a' }], total: 51 },
      })
    },
  })
  const page = mount(HallMetric, { props: { label: '运行设备', metric: 'running', session } })
  try {
    await flushPromises()
    expect(page.get('strong').text()).toBe('1/1')
    expect(page.text()).toContain('在册 51 台 · 已覆盖 1 台 · 未知 50 台')
    expect(requests).toBe(2)
  } finally {
    page.unmount()
  }
})

it('运行占比按台账关联，断线设备不计运行，响应顺序不影响结果', async () => {
  const urls: URL[] = []
  configureApiClient({
    baseUrl: 'http://fixture',
    fetch: async (input) => {
      const url = new URL(new Request(input).url)
      const scope = deviceScopeResponse(url)
      if (scope) return scope
      urls.push(url)
      const data = url.pathname.endsWith('/device-assets')
        ? { resources: [{ deviceAssetId: 'a' }, { deviceAssetId: 'b' }], total: 2 }
        : {
            devices: [
              {
                deviceAssetId: 'b',
                currentState: 'running',
                activeAlarmCount: 0,
                isSourceFresh: false,
              },
              {
                deviceAssetId: 'a',
                currentState: 'running',
                activeAlarmCount: 0,
                isSourceFresh: true,
              },
            ],
          }
      return Response.json({ success: true, data })
    },
  })
  const page = mount(HallMetric, { props: { label: '运行设备', metric: 'running', session } })
  try {
    await flushPromises()
    expect(page.get('strong').text()).toBe('1/2')
    expect(page.text()).toContain('b · 断线')
    expect(urls[1].searchParams.get('deviceAssetIds')).toBe('a,b')
    expect(
      urls.every(
        (url) =>
          url.searchParams.get('organizationId') === 'o' &&
          url.searchParams.get('environmentId') === 'e',
      ),
    ).toBe(true)
  } finally {
    page.unmount()
  }
})

it('仓储按建单日计算分母，首页已经触及昨日时无需翻历史页', async () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-11-21T10:30:00'))
  const fetch = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(new Request(input).url)
    if (url.pathname === '/api/business-console/v1/wms/work-scopes/shipments')
      return Response.json({
        success: true,
        data: {
          actorPrincipalId: 'foreman',
          items: [{ scopeKind: 'site', scopeId: 'S01', displayName: '一号站点' }],
        },
      })
    if (
      url.pathname !== '/api/business-console/v1/wms/outbound-orders' ||
      url.searchParams.get('scopeKind') !== 'site' ||
      url.searchParams.get('scopeId') !== 'S01'
    )
      return new Response(null, { status: 403 })
    return Response.json({
      success: true,
      data: {
        total: 150,
        items: [
          { createdAtUtc: new Date('2026-11-21T09:00:00').toISOString(), status: 'Completed' },
          { createdAtUtc: new Date('2026-11-21T08:00:00').toISOString(), status: 'Open' },
          { createdAtUtc: new Date('2026-11-20T23:50:00').toISOString(), status: 'Completed' },
        ],
      },
    })
  })
  configureApiClient({ baseUrl: 'http://fixture', fetch })
  const page = mount(HallMetric, { props: { label: '今日出库', metric: 'outbound', session } })
  try {
    await flushPromises()
    expect(page.get('strong').text()).toBe('1/2')
    expect(page.text()).toContain('一号站点')
    expect(fetch).toHaveBeenCalledTimes(2)
  } finally {
    page.unmount()
  }
})
