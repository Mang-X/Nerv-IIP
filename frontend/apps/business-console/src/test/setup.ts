import { enableAutoUnmount } from '@vue/test-utils'
import { afterEach } from 'vitest'
import { disableUnovisTooltipThrottle } from './unovisTooltipTimers'

enableAutoUnmount(afterEach)

/**
 * jsdom 不实现 ResizeObserver，而 unovis（`NvAreaChart` 等图表底座）挂载与卸载
 * 时都要用它。缺了它的后果不是「图表画不出来」，而是**整个页面挂载直接抛异常**——
 * 任何一张 KPI 卡带上迷你图，这一页的用例会一起变红，报错还指向 `wrapper.text()`
 * 这种与图表无关的地方。同一段桩在 `@nerv-iip/ui` 的 `Metric.test.ts` /
 * `Carousel.test.ts` 里已有先例，这里提到 app 级 setup，免得每张带图的页面各写一遍。
 */
if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
}

if (!globalThis.localStorage) {
  const storage = new Map<string, string>()

  Object.defineProperty(globalThis, 'localStorage', {
    configurable: true,
    value: {
      clear: () => storage.clear(),
      getItem: (key: string) => storage.get(key) ?? null,
      key: (index: number) => [...storage.keys()][index] ?? null,
      removeItem: (key: string) => storage.delete(key),
      setItem: (key: string, value: string) => storage.set(key, value),
      get length() {
        return storage.size
      },
    },
  })
}

/**
 * unovis tooltip 的 throttle 定时器会活过 jsdom 环境拆除，导致整包偶发假红（#2011）。
 * 必须在任何图表挂载（即任何 `Tooltip` 实例创建）之前收口；同时必须排在上面的
 * ResizeObserver 桩**之后**——unovis 在模块求值时就把 ResizeObserver 定死了，详见
 * `unovisTooltipTimers.ts` 的注释。
 */
await disableUnovisTooltipThrottle()
