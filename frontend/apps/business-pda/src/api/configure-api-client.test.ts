import { afterEach, describe, expect, it, vi } from 'vitest'

const configureApiClient = vi.fn()
vi.mock('@nerv-iip/api-client', () => ({ configureApiClient }))

const { configurePdaApiClient } = await import('./configure-api-client')
const { notifyUnauthorized, setUnauthorizedHandler } = await import('./unauthorized')

afterEach(() => {
  configureApiClient.mockClear()
  setUnauthorizedHandler(undefined)
})

describe('configurePdaApiClient', () => {
  /**
   * 这一格守的是**接线**，不是文案。
   *
   * tus 的 `HEAD` / `PATCH` 进不了 OpenAPI 契约，只能手搓 fetch；但手搓传输不等于手搓一整套
   * 认证处置。走 api-client 的请求 401 时会命中 `onUnauthorized`（清会话 + 跳登录），手搓那两跳
   * 必须**接回同一个函数**而不是自己弹提示了事——否则用户会卡在一个已失效的会话里反复重试。
   */
  it('hands the SAME onUnauthorized to the hand-rolled byte path', () => {
    const onUnauthorized = vi.fn()
    configurePdaApiClient({ accessTokenProvider: () => 't', onUnauthorized })

    // 手搓支路拿到的必须就是这一个函数，不是另建的一套。
    notifyUnauthorized()
    expect(onUnauthorized).toHaveBeenCalledTimes(1)
  })

  it('still injects the base URL and the global timeout fetch into api-client', () => {
    const onUnauthorized = vi.fn()
    configurePdaApiClient({ accessTokenProvider: () => 't', onUnauthorized })

    expect(configureApiClient).toHaveBeenCalledTimes(1)
    const passed = configureApiClient.mock.calls[0][0]
    expect(passed.onUnauthorized).toBe(onUnauthorized)
    expect(typeof passed.fetch).toBe('function')
    expect('baseUrl' in passed).toBe(true)
  })

  it('re-registering replaces the previous handler instead of stacking them', () => {
    const first = vi.fn()
    const second = vi.fn()
    configurePdaApiClient({ accessTokenProvider: () => 't', onUnauthorized: first })
    configurePdaApiClient({ accessTokenProvider: () => 't', onUnauthorized: second })

    notifyUnauthorized()
    expect(first).not.toHaveBeenCalled()
    expect(second).toHaveBeenCalledTimes(1)
  })
})

describe('notifyUnauthorized without a host', () => {
  it('is inert rather than throwing (单测直接挂组件时没有 main.ts 接线)', () => {
    setUnauthorizedHandler(undefined)
    expect(() => notifyUnauthorized()).not.toThrow()
  })
})
