/**
 * `@nerv-iip/ui/test-support`：只给**测试环境 setup** 用的支撑件（#2014）。
 *
 * 刻意不从 `src/index.ts` 主桶导出——这些东西会改 unovis 原型、装全局桩，
 * 不属于组件库的运行时公共边界，也不受 NvUI 命名契约约束。消费方只在 vitest 的
 * `setupFiles` 里引用；子入口边界见 `frontend/DESIGN/governance.md` 的「包子入口边界」。
 *
 * 接入方式（`apps/console` / `apps/screen` / `apps/business-pda` 目前未挂 unovis 图表，
 * 因此暂未接入；哪天挂了就照 `apps/business-console/src/test/setup.ts` 加一行）：
 *
 * 1. 在该 app 的 `src/test/setup.ts` 里 `await disableUnovisTooltipThrottle()`，且必须排在
 *    ResizeObserver 桩**之后**（unovis 在模块求值时就把 ResizeObserver 定死，顺序换了会在
 *    卸载时抛 `observationTargets`）；导入面的门禁只放行 `src/test/setup.ts` 这一个文件。
 * 2. 给该 app 的 vite 配置补 `@nerv-iip/ui/test-support` 别名，并排在裸 `@nerv-iip/ui`
 *    **之前**——对象别名按声明序做前缀匹配，否则会被拼成 `.../src/index.ts/test-support`。
 */
export { disableUnovisTooltipThrottle } from './unovisTooltipTimers'
