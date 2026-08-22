import { disableUnovisTooltipThrottle } from '../test-support'

/**
 * `@nerv-iip/ui` 自己的组件测试也挂真实图表（`Metric.test.ts` → `NvMetricStrip`
 * → `NvMetricSparklinePart` → `NvAreaChart` → `VisTooltip`），走的是和
 * business-console 完全相同的那条 unovis 路径，所以同样会被 tooltip 的 throttle
 * 定时器判红（#2011 / #2014）。这个包此前没有统一的 setup 落点，ResizeObserver 桩
 * 是各测试文件自己 `beforeAll` 写的；这里把两件事都提上来。
 */

/**
 * jsdom 不实现 ResizeObserver，而 unovis 图表挂载与卸载都要用它。缺了它不是「图表
 * 画不出来」，而是整个组件挂载直接抛异常。
 */
if (!globalThis.ResizeObserver) {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver
}

/**
 * 必须排在 ResizeObserver 桩**之后**：`@unovis/ts` 在模块求值那一刻就把
 * `globalThis.ResizeObserver || @juggle/resize-observer` 定死，详见
 * `../test-support/unovisTooltipTimers.ts` 的注释（收口函数内部因此用动态 import）。
 */
await disableUnovisTooltipThrottle()
