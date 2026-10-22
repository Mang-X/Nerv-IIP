// #3656：页面行为以真实组件与轮询验证；仅 Gateway fetcher 替身，不证明真栈。
import { flushPromises, mount } from '@vue/test-utils'
import { reactive } from 'vue'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import AndonScreen from './AndonScreen.vue'
import { fetchAndonQueue, fetchAndonScopes } from '@/data/fetchers/andon'

vi.mock('@/data/config', () => ({ IS_REAL_DATA: true }))
vi.mock('@/data/fetchers/andon', () => ({ fetchAndonQueue: vi.fn(), fetchAndonScopes: vi.fn() }))
const auth = reactive({
  sessionId: 'session-1',
  principal: { organizationId: 'org-1', environmentId: 'env-1' },
})
vi.mock('@/stores/realAuth', () => ({ useRealAuthStore: () => auth }))
const scope = { kind: 'workCenter', id: 'wc-1', displayName: '装配一线' }
const call = {
  id: 'call-1',
  category: 'quality' as const,
  status: 'open' as const,
  workCenterId: 'wc-1',
  workOrderId: 'WO-260920-01',
  raisedAtUtc: '2026-09-20T01:00:00Z',
}
let wrapper: ReturnType<typeof mount>

beforeEach(() => {
  vi.useFakeTimers()
  vi.resetAllMocks()
  auth.sessionId = 'session-1'
  vi.mocked(fetchAndonScopes).mockResolvedValue([scope])
  vi.mocked(fetchAndonQueue).mockResolvedValue({ items: [call], total: 1 })
})
afterEach(() => {
  wrapper?.unmount()
  vi.useRealTimers()
})
async function open() {
  wrapper = mount(AndonScreen)
  await flushPromises()
  await wrapper.get('[role="combobox"]').trigger('click')
  const option = document.body.querySelector('[role="option"]') as HTMLElement
  option.click()
  await flushPromises()
}

it('从待响应轮询为处理中并显示服务器升级，关闭后移出活动队列', async () => {
  await open()
  expect(wrapper.text()).toContain('待响应')
  expect(wrapper.text()).toContain('WO-260920-01')
  expect(wrapper.text()).not.toContain('已升级')
  vi.mocked(fetchAndonQueue).mockResolvedValue({
    items: [
      {
        ...call,
        status: 'claimed',
        responseDurationSeconds: 65,
        responderId: 'worker-2',
        escalatedAtUtc: '2026-09-20T01:01:00Z',
      },
    ],
    total: 1,
  })
  await vi.advanceTimersByTimeAsync(4000)
  expect(wrapper.text()).toContain('处理中')
  expect(wrapper.text()).toContain('已升级')
  expect(wrapper.text()).toContain('65 秒')
  vi.mocked(fetchAndonQueue).mockResolvedValue({ items: [], total: 0 })
  await vi.advanceTimersByTimeAsync(4000)
  expect(wrapper.text()).toContain('当前范围无活动呼叫')
  expect(wrapper.text()).not.toContain('WO-260920-01')
})

it.each([new TypeError('Failed to fetch'), new Error('未授权'), new Error('读取失败')])(
  '失败不能留下无呼叫或旧行',
  async (error) => {
    vi.mocked(fetchAndonQueue).mockResolvedValue({ items: [], total: 0 })
    await open()
    vi.mocked(fetchAndonQueue).mockRejectedValue(error)
    await vi.advanceTimersByTimeAsync(4000)
    expect(wrapper.text()).not.toContain('当前范围无活动呼叫')
    expect(wrapper.text()).toContain(error instanceof TypeError ? '失联' : error.message)
    expect(wrapper.text()).not.toContain('共 0 条活动呼叫')
  },
)

it('切换范围立即清除旧行且延迟到达的旧请求不能覆盖新范围', async () => {
  const next = { kind: 'workCenter', id: 'wc-2', displayName: '装配二线' }
  vi.mocked(fetchAndonScopes).mockResolvedValue([scope, next])
  let resolveOld!: (value: { items: (typeof call)[]; total: number }) => void
  vi.mocked(fetchAndonQueue).mockImplementation((_session, selected) =>
    selected.id === 'wc-1'
      ? new Promise((resolve) => {
          resolveOld = resolve
        })
      : Promise.resolve({ items: [{ ...call, id: 'call-2', workOrderId: 'WO-NEW' }], total: 1 }),
  )
  await open()
  await wrapper.get('[role="combobox"]').trigger('click')
  ;(document.body.querySelectorAll('[role="option"]')[1] as HTMLElement).click()
  await flushPromises()
  resolveOld({ items: [call], total: 1 })
  await flushPromises()
  expect(wrapper.text()).toContain('WO-NEW')
  expect(wrapper.text()).not.toContain('WO-260920-01')
})

it('轮询挂起超出新鲜度窗口后撤下空队列提示', async () => {
  vi.mocked(fetchAndonQueue)
    .mockResolvedValueOnce({ items: [], total: 0 })
    .mockReturnValue(new Promise(() => {}))
  await open()
  expect(wrapper.text()).toContain('当前范围无活动呼叫')
  await vi.advanceTimersByTimeAsync(13_000)
  expect(wrapper.text()).toContain('失联')
  expect(wrapper.text()).not.toContain('当前范围无活动呼叫')
})

it('没有授权范围时显示未配置而不读取队列', async () => {
  vi.mocked(fetchAndonScopes).mockResolvedValue([])
  wrapper = mount(AndonScreen)
  await flushPromises()
  expect(wrapper.text()).toContain('未配置真实范围')
  expect(fetchAndonQueue).not.toHaveBeenCalled()
})

it('切换会话后旧队列响应不能重新出现', async () => {
  let resolveOld!: (value: { items: (typeof call)[]; total: number }) => void
  vi.mocked(fetchAndonQueue).mockReturnValue(
    new Promise((resolve) => {
      resolveOld = resolve
    }),
  )
  await open()
  auth.sessionId = 'session-2'
  await flushPromises()
  resolveOld({ items: [call], total: 1 })
  await flushPromises()
  expect(wrapper.text()).not.toContain('WO-260920-01')
  expect(wrapper.text()).toContain('请选择真实作业范围')
})
