import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { effectScope } from 'vue'
import { useScreenData, type UseScreenDataReturn } from './useScreenData'

// flush 微任务 + 到期定时器
function flush() {
  return vi.advanceTimersByTimeAsync(0)
}

describe('useScreenData', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('immediate 时挂载立即取一次并写入 data', async () => {
    const fetcher = vi.fn().mockResolvedValue('A')
    const scope = effectScope()
    let api!: UseScreenDataReturn<string>
    scope.run(() => {
      api = useScreenData(fetcher, { intervalMs: 1000 })
    })
    expect(fetcher).toHaveBeenCalledTimes(1)
    await flush()
    expect(api.data.value).toBe('A')
    expect(api.isStale.value).toBe(false)
    expect(api.lastUpdated.value).toBeTypeOf('number')
    scope.stop()
  })

  it('按 intervalMs 周期性轮询', async () => {
    const fetcher = vi.fn().mockResolvedValue('A')
    const scope = effectScope()
    scope.run(() => useScreenData(fetcher, { intervalMs: 1000 }))
    await flush()
    expect(fetcher).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(1000)
    expect(fetcher).toHaveBeenCalledTimes(2)
    await vi.advanceTimersByTimeAsync(1000)
    expect(fetcher).toHaveBeenCalledTimes(3)
    scope.stop()
  })

  it('失败保活：保留上次数据并标记 stale，不抛错', async () => {
    const fetcher = vi
      .fn()
      .mockResolvedValueOnce('A')
      .mockRejectedValueOnce(new Error('boom'))
    const scope = effectScope()
    let api!: UseScreenDataReturn<string>
    scope.run(() => {
      api = useScreenData(fetcher, { intervalMs: 1000 })
    })
    await flush()
    expect(api.data.value).toBe('A')

    await vi.advanceTimersByTimeAsync(1000)
    expect(api.data.value).toBe('A') // 旧数据保留
    expect(api.isStale.value).toBe(true)
    expect(api.error.value).toBeInstanceOf(Error)
    scope.stop()
  })

  it('stop 后停止轮询', async () => {
    const fetcher = vi.fn().mockResolvedValue('A')
    const scope = effectScope()
    let api!: UseScreenDataReturn<string>
    scope.run(() => {
      api = useScreenData(fetcher, { intervalMs: 1000 })
    })
    await flush()
    api.stop()
    await vi.advanceTimersByTimeAsync(3000)
    expect(fetcher).toHaveBeenCalledTimes(1)
    scope.stop()
  })

  it('scope dispose 自动停止轮询', async () => {
    const fetcher = vi.fn().mockResolvedValue('A')
    const scope = effectScope()
    scope.run(() => useScreenData(fetcher, { intervalMs: 1000 }))
    await flush()
    scope.stop() // 触发 onScopeDispose -> stop
    await vi.advanceTimersByTimeAsync(3000)
    expect(fetcher).toHaveBeenCalledTimes(1)
  })

  it('immediate=false 时挂载不取数，start 后才取', async () => {
    const fetcher = vi.fn().mockResolvedValue('A')
    const scope = effectScope()
    let api!: UseScreenDataReturn<string>
    scope.run(() => {
      api = useScreenData(fetcher, { intervalMs: 1000, immediate: false })
    })
    expect(fetcher).toHaveBeenCalledTimes(0)
    await vi.advanceTimersByTimeAsync(1000)
    expect(fetcher).toHaveBeenCalledTimes(1)
    scope.stop()
  })
})
