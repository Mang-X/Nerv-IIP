/**
 * unovis 的 `Tooltip` 在构造时把两个方法包成 500 ms throttle
 * （`@unovis/ts/components/tooltip/index.js:12-13`，底层是 `throttle-debounce`）：
 * 图表每次更新容器/重绑事件都会调用它们，窗口内的第二次调用会把一个 trailing 回调
 * 排进宏任务队列。`Tooltip.destroy()` 只清了 hide/show 两个延时，**不取消这两个
 * throttle**，所以 vitest 跑完一个测试文件、拆掉 jsdom 环境之后，这个回调仍挂在 Node
 * 事件循环上；触发时 `document` / `getComputedStyle` 已不存在，抛出的
 * `ReferenceError: document is not defined` 不归属任何用例，直接把整包判红
 * （用例全绿但 `Errors 1`，退出码 1）。红绿只取决于定时器落点与环境拆除时刻的先后，
 * 因此表现为偶发假红（#2011）。
 *
 * 收口方式：在测试环境 setup 里把这两个 throttle 包装换成**直接同步调用**。
 * 语义不变（jsdom 里本就不需要限频，两个方法都幂等：一个把容器 position 置为
 * relative，一个用 d3 重绑同名事件），但从此不再产生任何跨环境存活的宏任务——
 * 这是消除定时器本身，而不是把未捕获异常吞掉（后者会连真实错误一起放过）。
 *
 * 落点在 setup 而不是逐个测试文件桩掉图表组件：只要还有别的页面挂
 * `NvAreaChart` / `NvLineChart` / `NvBarChart` / `NvDonutChart`，逐个加桩就是打地鼠。
 *
 * 实现本身住在 `@nerv-iip/ui/test-support`（#2014）而不是某个 app 内部：暴露面不止
 * business-console —— `packages/ui` 自己的组件测试就直接挂真实图表，其它 app 一旦挂图
 * 也会复现。各包各写一份等于把「打地鼠」从文件级搬到包级，所以这里只留一份实现，
 * 由各包的 setup 引用。这个子路径是 test-only 的：它不进 `src/index.ts` 主桶，
 * 组件库的运行时消费者取不到它，也不受 NvUI 命名契约约束。
 *
 * 两个槽位都收口，但当前 jsdom 下只观测到 `_setContainerPositionThrottled` 真的排出过
 * 定时器（`mount()` 默认不挂 document → `hasContainer()` 恒假 → 每次重绘都重新
 * `setContainer`）；`_setUpEventsThrottled` 走的是同一个泄漏机制，一并处理以免以后
 * 换条渲染路径又冒出来，回归用例只钉得住前者。
 */
const THROTTLED_TO_DIRECT = {
  _setContainerPositionThrottled: '_setContainerPosition',
  _setUpEventsThrottled: '_setUpEvents',
} as const

type TooltipInternals = Record<string, unknown>

/**
 * 把 `Tooltip` 原型上的两个 throttle 槽位改成访问器：构造函数写入 throttle 包装时
 * 丢弃它，改在实例上装一个直接调用原方法的同名函数（实例自有属性会遮蔽原型访问器，
 * 后续读取不再经过这里）。必须在任何 `Tooltip` 实例创建之前调用。
 *
 * 这里用**动态** import 取 `Tooltip`：`@unovis/ts` 的 `utils/resize-observer.js` 在模块求值
 * 那一刻就把 `globalThis.ResizeObserver || @juggle/resize-observer` 定死；若在 setup 装好
 * ResizeObserver 桩之前静态导入 unovis，图表就会改用 juggle 的 polyfill，卸载时
 * `disconnect()` 在 jsdom 下抛 `Cannot read properties of undefined (reading 'observationTargets')`。
 * 静态 import 会被提升到桩之前，import 排序规则还会把它排到相对导入前面，所以只能延迟到调用时。
 */
export async function disableUnovisTooltipThrottle(): Promise<void> {
  const { Tooltip } = await import('@unovis/ts')
  const prototype: object = Tooltip.prototype
  for (const [throttledKey, directKey] of Object.entries(THROTTLED_TO_DIRECT)) {
    Object.defineProperty(prototype, throttledKey, {
      configurable: true,
      get() {
        return undefined
      },
      set(this: TooltipInternals) {
        Object.defineProperty(this, throttledKey, {
          configurable: true,
          writable: true,
          value: (...args: unknown[]) =>
            (this[directKey] as (...a: unknown[]) => unknown).apply(this, args),
        })
      },
    })
  }
}
