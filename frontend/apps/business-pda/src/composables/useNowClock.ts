import { onScopeDispose, readonly, shallowRef, type Ref } from 'vue'

/**
 * 受控的响应式当前时间（毫秒）。用于「超期」等**依赖时间流逝**的派生状态：
 * 停留在页面时跨过 `dueAtUtc`，超期标记与排序会随时钟自动重算，而不是等其它状态变化才更新
 * （`Date.now()` 不是响应式依赖，做不到这点）。定时器在作用域销毁时自动清理。
 *
 * @param intervalMs 刷新间隔，默认 30s——超期是分钟级语义，无需秒级刷新。
 */
export function useNowClock(intervalMs = 30_000): Readonly<Ref<number>> {
  const now = shallowRef(Date.now())
  const timer = setInterval(() => {
    now.value = Date.now()
  }, intervalMs)
  onScopeDispose(() => clearInterval(timer))
  return readonly(now)
}
