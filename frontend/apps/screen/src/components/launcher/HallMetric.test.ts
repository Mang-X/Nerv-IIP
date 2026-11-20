import { configureApiClient } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
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

it('台账跨页时不能把首批设备当作组织总数', async () => {
  let requests = 0
  configureApiClient({
    baseUrl: 'http://fixture',
    fetch: async () => {
      requests++
      return Response.json({
        success: true,
        data: { resources: [{ deviceAssetId: 'a' }], total: 51 },
      })
    },
  })
  const page = mount(HallMetric, { props: { label: '运行设备', metric: 'running', session } })
  try {
    await flushPromises()
    expect(page.text()).toContain('完整台账不可用')
    expect(page.text()).not.toContain('实时')
    expect(requests).toBe(1)
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
