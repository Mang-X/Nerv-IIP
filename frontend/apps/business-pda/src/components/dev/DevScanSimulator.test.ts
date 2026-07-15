import { flushPromises, mount } from '@vue/test-utils'
import { NvScanBar } from '@nerv-iip/ui-mobile'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from '../../App.vue'
import DevScanSimulator from './DevScanSimulator.vue'
import { injectScanKeystrokes, SCAN_GUN_CHAR_INTERVAL_MS } from './dev-scan-injection'

interface RecordedKey {
  key: string
  at: number
  bubbles: boolean
  cancelable: boolean
  event: KeyboardEvent
}

/** Capture-phase recorder for every keydown the injector dispatches on document. */
function recordDocumentKeydowns(handler?: (event: KeyboardEvent) => void) {
  const events: RecordedKey[] = []
  const listener = (event: KeyboardEvent) => {
    handler?.(event)
    events.push({
      key: event.key,
      at: Date.now(),
      bubbles: event.bubbles,
      cancelable: event.cancelable,
      event,
    })
  }
  document.addEventListener('keydown', listener, true)
  return {
    events,
    stop: () => document.removeEventListener('keydown', listener, true),
  }
}

afterEach(() => {
  vi.useRealTimers()
})

describe('injectScanKeystrokes（扫码枪时序注码）', () => {
  it('按 15ms 间隔逐字符派发 keydown 并以 Enter 收尾（可冒泡、可取消）', async () => {
    vi.useFakeTimers()
    const recorder = recordDocumentKeydowns()

    const done = injectScanKeystrokes('WO-123')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * 6 + 5)
    await done
    recorder.stop()

    expect(recorder.events.map((e) => e.key)).toEqual(['W', 'O', '-', '1', '2', '3', 'Enter'])
    for (const e of recorder.events) {
      expect(e.bubbles).toBe(true)
      expect(e.cancelable).toBe(true)
    }
    // 突发时序契约：相邻字符间隔 = 15ms，远低于 ScanBar 的 100ms 突发阈值；
    // Enter 相对最后一个字符同样 15ms，满足 300ms 新鲜度约束。
    const gaps = recorder.events.slice(1).map((e, i) => e.at - recorder.events[i]!.at)
    expect(gaps).toEqual([15, 15, 15, 15, 15, 15])
  })

  it('派发的事件可被 preventDefault（ScanBar 捕获字符即阻断默认行为）', async () => {
    vi.useFakeTimers()
    const recorder = recordDocumentKeydowns((event) => event.preventDefault())

    const done = injectScanKeystrokes('ABC')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * 3 + 5)
    await done
    recorder.stop()

    expect(recorder.events).toHaveLength(4)
    for (const e of recorder.events) {
      expect(e.event.defaultPrevented).toBe(true)
    }
  })

  it('焦点在可编辑元素上时先 blur 再派发（保证 document 捕获路径接手）', async () => {
    vi.useFakeTimers()
    const holder = document.createElement('input')
    document.body.appendChild(holder)
    holder.focus()
    expect(document.activeElement).toBe(holder)

    const activeAtDispatch: (Element | null)[] = []
    const recorder = recordDocumentKeydowns(() => {
      activeAtDispatch.push(document.activeElement)
    })

    const done = injectScanKeystrokes('ABC')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * 3 + 5)
    await done
    recorder.stop()
    holder.remove()

    // 每次派发时可编辑元素都已被让位——ScanBar 的 capture 兜底才会消费字符。
    for (const active of activeAtDispatch) {
      expect(active).not.toBe(holder)
    }
  })
})

describe('DevScanSimulator', () => {
  it('渲染悬浮按钮；点按开合面板（含输入框、注码按钮与预设码）', async () => {
    const wrapper = mount(DevScanSimulator)

    expect(wrapper.find('[data-testid=dev-scan-toggle]').exists()).toBe(true)
    expect(wrapper.find('[data-testid=dev-scan-panel]').exists()).toBe(false)

    await wrapper.find('[data-testid=dev-scan-toggle]').trigger('click')
    expect(wrapper.find('[data-testid=dev-scan-panel]').exists()).toBe(true)
    expect(wrapper.find('[data-testid=dev-scan-input]').exists()).toBe(true)
    expect(wrapper.find('[data-testid=dev-scan-inject]').exists()).toBe(true)
    expect(wrapper.findAll('[data-testid=dev-scan-preset]').length).toBeGreaterThanOrEqual(1)

    await wrapper.find('[data-testid=dev-scan-toggle]').trigger('click')
    expect(wrapper.find('[data-testid=dev-scan-panel]').exists()).toBe(false)
  })

  it('空输入时注码按钮禁用', async () => {
    const wrapper = mount(DevScanSimulator)
    await wrapper.find('[data-testid=dev-scan-toggle]').trigger('click')

    const inject = wrapper.find('[data-testid=dev-scan-inject]')
    expect(inject.attributes('disabled')).toBeDefined()

    await wrapper.find('[data-testid=dev-scan-input]').setValue('WO-1')
    expect(inject.attributes('disabled')).toBeUndefined()
  })

  it('点「注码」按扫码枪时序派发输入内容 + Enter 后缀', async () => {
    vi.useFakeTimers()
    const wrapper = mount(DevScanSimulator)
    await wrapper.find('[data-testid=dev-scan-toggle]').trigger('click')
    await wrapper.find('[data-testid=dev-scan-input]').setValue('LOT-42')

    const recorder = recordDocumentKeydowns()
    await wrapper.find('[data-testid=dev-scan-inject]').trigger('click')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * 6 + 5)
    recorder.stop()

    expect(recorder.events.map((e) => e.key)).toEqual(['L', 'O', 'T', '-', '4', '2', 'Enter'])
  })

  it('点预设码直接注码', async () => {
    vi.useFakeTimers()
    const wrapper = mount(DevScanSimulator)
    await wrapper.find('[data-testid=dev-scan-toggle]').trigger('click')

    const preset = wrapper.findAll('[data-testid=dev-scan-preset]')[0]!
    const presetCode = preset.text()

    const recorder = recordDocumentKeydowns()
    await preset.trigger('click')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * presetCode.length + 20)
    recorder.stop()

    expect(recorder.events.map((e) => e.key)).toEqual([...presetCode, 'Enter'])
  })

  it('与 ScanBar 集成冒烟：注码经 document 捕获缓冲触发 scan 事件', async () => {
    vi.useFakeTimers()
    // detached mount：ScanBar 的回焦 focus() 不生效 → activeElement 停留在 body，
    // 正是 document 捕获路径的前置条件（与真机失焦窗口同构）。
    const scanBar = mount(NvScanBar)
    const simulator = mount(DevScanSimulator)

    await simulator.find('[data-testid=dev-scan-toggle]').trigger('click')
    await simulator.find('[data-testid=dev-scan-input]').setValue('WO-2026-0001')
    await simulator.find('[data-testid=dev-scan-inject]').trigger('click')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * 12 + 20)

    expect(scanBar.emitted('scan')).toEqual([['WO-2026-0001']])
  })

  it('与 ScanBar 集成：焦点常驻（input 已聚焦）时注码仍生效（逐字符让位）', async () => {
    vi.useFakeTimers()
    const host = document.createElement('div')
    document.body.appendChild(host)
    const scanBar = mount(NvScanBar, { attachTo: host })
    // 触发挂载期回焦 RAF，让 input 真正持有焦点（fake timers 也接管了 RAF）。
    await vi.advanceTimersByTimeAsync(50)
    expect(document.activeElement).toBe(scanBar.find('input').element)

    const done = injectScanKeystrokes('SKU-777')
    await vi.advanceTimersByTimeAsync(SCAN_GUN_CHAR_INTERVAL_MS * 7 + 20)
    await done

    expect(scanBar.emitted('scan')).toEqual([['SKU-777']])
  })
})

describe('App 挂载点 DEV 门控', () => {
  it('DEV 构建下渲染悬浮模拟扫码按钮（生产树摇由 build 后 grep dist 验证）', async () => {
    // vitest 里 import.meta.env.DEV === true → 异步组件分支生效。
    expect(import.meta.env.DEV).toBe(true)
    const wrapper = mount(App, { global: { stubs: { RouterView: true } } })
    await flushPromises()
    await flushPromises()
    expect(wrapper.find('[data-testid=dev-scan-simulator]').exists()).toBe(true)
  })
})
