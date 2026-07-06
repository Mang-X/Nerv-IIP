import { onScopeDispose, type Ref, ref, shallowRef } from 'vue'

export interface UseScreenDataOptions<T> {
  /** 轮询间隔（毫秒），默认 15s */
  intervalMs?: number
  /** 是否挂载即取一次，默认 true */
  immediate?: boolean
  /** 初始占位数据（后端/数据未就绪时也有内容显示） */
  initialData?: T
}

export interface UseScreenDataReturn<T> {
  data: Ref<T | undefined>
  error: Ref<unknown>
  loading: Ref<boolean>
  /** 最近一次成功取数的时间戳（ms） */
  lastUpdated: Ref<number | undefined>
  /** 取数失败但保留了上次数据时为 true */
  isStale: Ref<boolean>
  refresh: () => Promise<void>
  start: () => void
  stop: () => void
}

/**
 * 大屏取数：轮询 + 页面隐藏时暂停 + 失败保活。
 *
 * 设计要点（大屏挂墙长时间运行）：
 * - 取数失败不抛错、不清空旧数据，仅标记 error/isStale，墙上画面不闪空。
 * - 页面隐藏（切到别的标签/熄屏）时跳过取数，恢复可见时立即补一次。
 * - 组件卸载（scope dispose）自动停止并清理监听，避免泄漏。
 */
export function useScreenData<T>(
  fetcher: () => Promise<T>,
  options: UseScreenDataOptions<T> = {},
): UseScreenDataReturn<T> {
  const { intervalMs = 15_000, immediate = true, initialData } = options

  const data = shallowRef<T | undefined>(initialData)
  const error = shallowRef<unknown>(undefined)
  const loading = ref(false)
  const lastUpdated = ref<number | undefined>(undefined)
  const isStale = ref(false)

  let timer: ReturnType<typeof setTimeout> | undefined
  let active = false
  let inFlight = false

  function clearTimer() {
    if (timer !== undefined) {
      clearTimeout(timer)
      timer = undefined
    }
  }

  async function refresh(): Promise<void> {
    if (inFlight) return
    inFlight = true
    loading.value = true
    try {
      const result = await fetcher()
      data.value = result
      error.value = undefined
      lastUpdated.value = Date.now()
      isStale.value = false
    } catch (err) {
      // 失败保活：保留上次 data，只标记 error / stale，不抛出
      error.value = err
      isStale.value = data.value !== undefined
    } finally {
      loading.value = false
      inFlight = false
    }
  }

  function schedule() {
    clearTimer()
    if (!active) return
    timer = setTimeout(() => {
      void tick()
    }, intervalMs)
  }

  async function tick() {
    if (active && (typeof document === 'undefined' || !document.hidden)) {
      await refresh()
    }
    schedule()
  }

  function handleVisibility() {
    if (active && typeof document !== 'undefined' && !document.hidden) {
      void refresh()
    }
  }

  function start() {
    if (active) return
    active = true
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', handleVisibility)
    }
    if (immediate) void refresh()
    schedule()
  }

  function stop() {
    active = false
    clearTimer()
    if (typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', handleVisibility)
    }
  }

  onScopeDispose(stop)
  start()

  return { data, error, loading, lastUpdated, isStale, refresh, start, stop }
}
