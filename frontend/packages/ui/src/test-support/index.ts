/**
 * `@nerv-iip/ui/test-support`：只给**测试环境 setup** 用的支撑件（#2014）。
 *
 * 刻意不从 `src/index.ts` 主桶导出——这些东西会改 unovis 原型、装全局桩，
 * 不属于组件库的运行时公共边界。消费方只在 vitest 的 `setupFiles` 里引用。
 */
export { disableUnovisTooltipThrottle } from './unovisTooltipTimers'
