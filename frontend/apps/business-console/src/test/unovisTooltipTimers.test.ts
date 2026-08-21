import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Tooltip } from '@unovis/ts'
import { NvAreaChart } from '@nerv-iip/ui'

/**
 * #2011 的阳性对照：挂载**真实**的 `NvAreaChart`（`NvMetricStrip` 迷你图与
 * 设备/质量/计划各页图表都走这条路径），走一次「挂载 + 数据更新 + 卸载」，
 * 断言卸载后事件循环上不再有 unovis 排下的待触发宏任务。
 *
 * 为什么要更新一次数据：`throttle-debounce` 的第一次调用走 leading 同步执行、
 * 不排定时器，只有 500 ms 窗口内的第二次调用才会排 trailing 回调；而 jsdom 里
 * `mount()` 默认不挂到 document，`Tooltip.hasContainer()` 因 `isConnected` 为 false
 * 永远为假，于是每次重绘都会重新 `setContainer` —— 这正是 CI 上偶发假红的成因。
 * 去掉 `src/test/setup.ts` 里的收口后，本用例会看到残留定时器（实测 1~2 个，随重绘次数浮动）而变红。
 */
const chartData = [
  { label: '第 1 周', value: 1052 },
  { label: '第 2 周', value: 1184 },
  { label: '第 3 周', value: 1284 },
]

const realSetTimeout = globalThis.setTimeout
const realClearTimeout = globalThis.clearTimeout

/** 记录窗口内排下、且未被取消也未触发的定时器。 */
function trackPendingTimers() {
  const pending = new Map<unknown, number>()

  vi.spyOn(globalThis, 'setTimeout').mockImplementation(((
    handler: TimerHandler,
    timeout?: number,
    ...args: unknown[]
  ) => {
    let id: unknown
    id = realSetTimeout(
      (...called: unknown[]) => {
        pending.delete(id)
        if (typeof handler === 'function') (handler as (...a: unknown[]) => void)(...called)
      },
      timeout,
      ...args,
    )
    pending.set(id, timeout ?? 0)
    return id
  }) as typeof globalThis.setTimeout)

  vi.spyOn(globalThis, 'clearTimeout').mockImplementation(((id: unknown) => {
    pending.delete(id)
    return realClearTimeout(id as Parameters<typeof globalThis.clearTimeout>[0])
  }) as typeof globalThis.clearTimeout)

  return pending
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('#2011 unovis tooltip 定时器收口', () => {
  it('挂载并更新真实 NvAreaChart 后，卸载时没有残留定时器', async () => {
    const pending = trackPendingTimers()

    const wrapper = mount(NvAreaChart, { props: { data: chartData, crosshair: true } })
    await nextTick()
    await nextTick()
    await wrapper.setProps({ data: chartData.map((d) => ({ ...d, value: d.value + 60 })) })
    await nextTick()
    await nextTick()

    // 图表确实挂起来了（否则「无残留定时器」是空断言）
    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.html()).toContain('data-vis-tooltip')

    wrapper.unmount()
    await nextTick()

    expect([...pending.values()]).toEqual([])
  })

  it('收口后 tooltip 的容器定位仍然执行，只是从定时器改为同步调用', async () => {
    // `_setContainerPosition` 正是 CI 崩栈里那一帧（拆环境后读 document 而抛）。
    // 断言它照常被调用，才能区分「定时器被消除」与「tooltip 被整体关掉」。
    const internals = Tooltip.prototype as unknown as Record<string, () => void>
    const setContainerPosition = vi.spyOn(internals, '_setContainerPosition')

    const wrapper = mount(NvAreaChart, { props: { data: chartData, crosshair: true } })
    await nextTick()
    await nextTick()

    expect(setContainerPosition).toHaveBeenCalled()

    wrapper.unmount()
  })
})
