import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import ScanBar from './ScanBar.vue'

describe('ScanBar', () => {
  it('emits scan with the buffered value on Enter and clears the input', async () => {
    const wrapper = mount(ScanBar)
    const input = wrapper.get('input')
    await input.setValue('SKU-12345')
    await input.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('scan')).toBeTruthy()
    expect(wrapper.emitted('scan')![0]).toEqual(['SKU-12345'])
    expect((input.element as HTMLInputElement).value).toBe('')
    wrapper.unmount()
  })

  it('ignores Enter on an empty buffer', async () => {
    const wrapper = mount(ScanBar)
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('scan')).toBeFalsy()
    wrapper.unmount()
  })

  it('renders the provided placeholder', () => {
    const wrapper = mount(ScanBar, { props: { placeholder: '扫描库位或物料' } })
    expect(wrapper.get('input').attributes('placeholder')).toBe('扫描库位或物料')
    wrapper.unmount()
  })

  it('refocuses the input on blur when active (default)', async () => {
    const wrapper = mount(ScanBar, { attachTo: document.body })
    const input = wrapper.get('input').element as HTMLInputElement
    input.blur()
    await wrapper.get('input').trigger('blur')
    await new Promise((r) => requestAnimationFrame(() => r(null)))
    expect(document.activeElement).toBe(input)
    wrapper.unmount()
  })

  it('does not refocus on blur when active is false', async () => {
    const wrapper = mount(ScanBar, { props: { active: false }, attachTo: document.body })
    const input = wrapper.get('input').element as HTMLInputElement
    input.blur()
    await wrapper.get('input').trigger('blur')
    await new Promise((r) => requestAnimationFrame(() => r(null)))
    expect(document.activeElement).not.toBe(input)
    wrapper.unmount()
  })

  // --- 焦点竞态契约（受控 RAF 时序）------------------------------------------
  describe('focus race with controlled requestAnimationFrame', () => {
    let rafCallbacks: Map<number, FrameRequestCallback>
    let nextRafId: number

    function flushRaf() {
      const pending = [...rafCallbacks.values()]
      rafCallbacks.clear()
      for (const cb of pending) cb(0)
    }

    beforeEach(() => {
      rafCallbacks = new Map()
      nextRafId = 1
      vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
        const id = nextRafId++
        rafCallbacks.set(id, cb)
        return id
      })
      vi.stubGlobal('cancelAnimationFrame', (id: number) => {
        rafCallbacks.delete(id)
      })
    })

    afterEach(() => {
      vi.unstubAllGlobals()
    })

    it('cancels a pending blur-refocus when active turns false before the RAF fires (S3)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input').element as HTMLInputElement
      flushRaf() // 消费 onMounted 的初始 refocus
      input.blur()
      await wrapper.get('input').trigger('blur') // 回焦已排入 RAF
      await wrapper.setProps({ active: false }) // 浮层此刻才打开
      flushRaf()
      expect(document.activeElement).not.toBe(input)
      wrapper.unmount()
    })

    it('does not steal focus if active turns false and the queued RAF still fires', async () => {
      // 双保险中的回调内复查：即使取消失效（异步边缘），回调也不得抢焦。
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input').element as HTMLInputElement
      flushRaf()
      input.blur()
      await wrapper.get('input').trigger('blur')
      const queued = [...rafCallbacks.values()]
      await wrapper.setProps({ active: false })
      for (const cb of queued) cb(0) // 强制执行已被取消的回调
      expect(document.activeElement).not.toBe(input)
      wrapper.unmount()
    })

    it('re-arms focus when active flips back from false to true', async () => {
      const wrapper = mount(ScanBar, { props: { active: false }, attachTo: document.body })
      const input = wrapper.get('input').element as HTMLInputElement
      expect(document.activeElement).not.toBe(input)
      await wrapper.setProps({ active: true }) // 浮层关闭，恢复常驻焦点
      flushRaf()
      expect(document.activeElement).toBe(input)
      wrapper.unmount()
    })

    // --- document 级扫码缓冲（S2 确定性 jsdom 版）-----------------------------
    function typeOnDocument(key: string) {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
      document.body.dispatchEvent(event)
      return event
    }

    it('buffers a scan burst arriving while the input is unfocused and emits scan on Enter (S2)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input').element as HTMLInputElement
      // 不 flushRaf：停留在 blur→回焦之间的竞态窗口，input 未聚焦
      expect(document.activeElement).not.toBe(input)

      for (const ch of 'SKU-77') expect(typeOnDocument(ch).defaultPrevented).toBe(true)
      await nextTick()
      expect(input.value).toBe('SKU-77')

      expect(typeOnDocument('Enter').defaultPrevented).toBe(true)
      expect(wrapper.emitted('scan')).toBeTruthy()
      expect(wrapper.emitted('scan')![0]).toEqual(['SKU-77'])
      await nextTick()
      expect(input.value).toBe('')
      wrapper.unmount()
    })

    it('lets Enter pass through when the buffer is empty', () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      expect(typeOnDocument('Enter').defaultPrevented).toBe(false)
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })

    it('does not capture keystrokes while another editable element is focused', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const other = document.createElement('input')
      document.body.appendChild(other)
      other.focus()

      const event = new KeyboardEvent('keydown', { key: 'X', bubbles: true, cancelable: true })
      other.dispatchEvent(event)
      expect(event.defaultPrevented).toBe(false)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')

      other.remove()
      wrapper.unmount()
    })

    it('does not capture keystrokes when active is false (S3)', async () => {
      const wrapper = mount(ScanBar, { props: { active: false }, attachTo: document.body })
      expect(typeOnDocument('A').defaultPrevented).toBe(false)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
      expect(rafCallbacks.size).toBe(0) // 也没有排入任何回焦
      wrapper.unmount()
    })

    it('stops capturing after unmount', () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      wrapper.unmount()
      expect(typeOnDocument('A').defaultPrevented).toBe(false)
    })

    it('ignores modifier chords in the document buffer', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const event = new KeyboardEvent('keydown', {
        key: 'c',
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
      })
      document.body.dispatchEvent(event)
      expect(event.defaultPrevented).toBe(false)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
      wrapper.unmount()
    })

    it('lets Space pass through untouched (button activation must not be swallowed)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      expect(typeOnDocument(' ').defaultPrevented).toBe(false)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
      wrapper.unmount()
    })

    it('lets keys pass through during IME composition', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const event = new KeyboardEvent('keydown', {
        key: 'a',
        isComposing: true,
        bubbles: true,
        cancelable: true,
      })
      document.body.dispatchEvent(event)
      expect(event.defaultPrevented).toBe(false)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
      wrapper.unmount()
    })

    it('lets Enter pass through when the buffer holds manually typed (non-captured) content', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      // 手工内容走 input 路径（v-model），document 捕获路径从未写入过。
      await wrapper.get('input').setValue('DRAFT')
      expect(document.activeElement).not.toBe(wrapper.get('input').element)
      expect(typeOnDocument('Enter').defaultPrevented).toBe(false)
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })
  })

  // --- document 缓冲时序判别（假定时器）--------------------------------------
  describe('document buffer timing with fake timers', () => {
    beforeEach(() => {
      vi.useFakeTimers()
      // RAF 置为 no-op：保持 input 不被回焦，停留在 document 捕获路径。
      vi.stubGlobal('requestAnimationFrame', () => 0)
      vi.stubGlobal('cancelAnimationFrame', () => {})
    })

    afterEach(() => {
      vi.useRealTimers()
      vi.unstubAllGlobals()
    })

    function typeOnDocument(key: string) {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
      document.body.dispatchEvent(event)
      return event
    }

    it('resets the buffer to the incoming char when the burst gap is exceeded', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      typeOnDocument('A')
      typeOnDocument('B')
      // 超过突发间隔（100ms）：AB 是陈旧残片，被新突发 CDE 取代
      vi.advanceTimersByTime(150)
      for (const ch of 'CDE') typeOnDocument(ch)
      typeOnDocument('Enter')
      expect(wrapper.emitted('scan')).toBeTruthy()
      expect(wrapper.emitted('scan')![0]).toEqual(['CDE'])
      wrapper.unmount()
    })

    it('lets Enter pass through when the captured buffer is stale (freshness exceeded)', () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      typeOnDocument('A')
      typeOnDocument('B')
      // 只推进系统时钟不触发定时器：模拟空闲清理尚未执行、但缓冲已过新鲜度
      vi.setSystemTime(Date.now() + 400)
      expect(typeOnDocument('Enter').defaultPrevented).toBe(false)
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })

    it('clears a captured fragment after the idle timeout without an Enter', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      typeOnDocument('A')
      typeOnDocument('B')
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('AB')
      vi.advanceTimersByTime(300)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })

    it('clears an untouched captured fragment on idle timeout even after the input regained focus', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input').element as HTMLInputElement
      typeOnDocument('A')
      // RAF 回焦后 300ms 内用户没碰残片：这就是误按残片，聚焦与否都要清。
      // 否则残片永久滞留输入框，之后的原生 Enter 会把它当扫码提交（P0-1）。
      input.focus()
      expect(document.activeElement).toBe(input)
      vi.advanceTimersByTime(300)
      await nextTick()
      expect(input.value).toBe('')
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })

    it('emits the full code when a captured prefix is continued via the native input path (mixed path)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input')
      const inputEl = input.element as HTMLInputElement
      // 未聚焦窗口内 document 捕获前缀两个字符
      typeOnDocument('S')
      typeOnDocument('K')
      await nextTick()
      expect(inputEl.value).toBe('SK')
      // 模拟 RAF 回焦，之后扫码流走原生 input 路径续写余下字符：setValue 触发
      // 原生 input 事件 → 所有权转移（清捕获标记），缓冲从此归用户/原生路径。
      inputEl.focus()
      await input.setValue('SKU-12345')
      // 原生 Enter 提交：拼装后的完整码 emit，不再受 document 捕获路径约束
      // （P0 守卫只拦「未接管的纯捕获短残片」，接管后不生效）。
      await input.trigger('keydown', { key: 'Enter' })
      expect(wrapper.emitted('scan')).toBeTruthy()
      expect(wrapper.emitted('scan')![0]).toEqual(['SKU-12345'])
      expect(inputEl.value).toBe('')
      wrapper.unmount()
    })

    it('discards a pure captured short fragment on native Enter after refocus instead of emitting (P0 guard)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input')
      const inputEl = input.element as HTMLInputElement
      // 未聚焦窗口内 document 捕获 1-2 字符误按残片
      typeOnDocument('A')
      typeOnDocument('B')
      await nextTick()
      expect(inputEl.value).toBe('AB')
      // 模拟 RAF 回焦后立即原生 Enter：缓冲仍是未被接管的纯捕获短残片，
      // 不得旁路 MIN_SCAN_CHARS 捕获规则——不 emit，且残片被清空。
      inputEl.focus()
      await input.trigger('keydown', { key: 'Enter' })
      expect(wrapper.emitted('scan')).toBeFalsy()
      expect(inputEl.value).toBe('')
      wrapper.unmount()
    })

    it('transfers buffer ownership on a native input event: idle timeout keeps the content', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input')
      typeOnDocument('A')
      typeOnDocument('B')
      await nextTick()
      // 用户接管：原生 input 事件一旦发生即转移所有权——即使值恰好仍等于捕获
      // 快照（编辑后变回快照的场景），空闲超时也不得再按「纯捕获残片」清空。
      ;(input.element as HTMLInputElement).focus()
      await input.setValue('AB')
      vi.advanceTimersByTime(300)
      await nextTick()
      expect((input.element as HTMLInputElement).value).toBe('AB')
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })

    it('transfers buffer ownership on a native input event: active=false keeps the content', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input')
      typeOnDocument('A')
      typeOnDocument('B')
      await nextTick()
      // 同上：接管后浮层打开（active=false）也不得清空用户已接管的内容。
      ;(input.element as HTMLInputElement).focus()
      await input.setValue('AB')
      await wrapper.setProps({ active: false })
      expect((input.element as HTMLInputElement).value).toBe('AB')
      wrapper.unmount()
    })

    it('clears a captured fragment via the active=false guard even after the input regained focus', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      const input = wrapper.get('input').element as HTMLInputElement
      typeOnDocument('A')
      await nextTick()
      input.focus()
      // 残片尚未超时（捕获标记还在）：active=false 守卫必须仍认得并清掉
      await wrapper.setProps({ active: false })
      expect(input.value).toBe('')
      wrapper.unmount()
    })

    it('lets a document-path Enter pass through when the burst is shorter than MIN_SCAN_CHARS', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      typeOnDocument('A')
      typeOnDocument('B')
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('AB')
      // 单/双字符是误按特征：Enter 放行（原生交互不受影响），不当扫码消费
      expect(typeOnDocument('Enter').defaultPrevented).toBe(false)
      expect(wrapper.emitted('scan')).toBeFalsy()
      wrapper.unmount()
    })

    it('arbitrates via defaultPrevented so only one of two mounted ScanBars consumes a burst', async () => {
      const first = mount(ScanBar, { attachTo: document.body })
      const second = mount(ScanBar, { attachTo: document.body })
      for (const ch of 'SKU-99') typeOnDocument(ch)
      typeOnDocument('Enter')
      await nextTick()
      // 先注册者 preventDefault 抢占，后注册者让位：恰好一个 emit、值完整、无双写
      const firstScans = first.emitted('scan') ?? []
      const secondScans = second.emitted('scan') ?? []
      expect(firstScans.length + secondScans.length).toBe(1)
      expect([...firstScans, ...secondScans][0]).toEqual(['SKU-99'])
      expect((first.get('input').element as HTMLInputElement).value).toBe('')
      expect((second.get('input').element as HTMLInputElement).value).toBe('')
      second.unmount()
      first.unmount()
    })

    it('does not clear the buffer on idle timeout when the input path modified it since capture', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      typeOnDocument('A')
      // 捕获后缓冲被 input 路径改动：input 事件即所有权转移（新语义），用户内容不动；
      // 快照失配判断仍作兜底保留同一结论。
      await wrapper.get('input').setValue('A-EDITED')
      vi.advanceTimersByTime(300)
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('A-EDITED')
      wrapper.unmount()
    })

    it('clears an unconsumed captured fragment when active turns false (P1-1)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      typeOnDocument('A')
      typeOnDocument('B')
      await nextTick()
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('AB')
      await wrapper.setProps({ active: false }) // 浮层打开：半次扫码残片必须丢弃
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('')
      // 恢复后下一枪不与残片拼接
      await wrapper.setProps({ active: true })
      for (const ch of 'CDE') typeOnDocument(ch)
      typeOnDocument('Enter')
      expect(wrapper.emitted('scan')).toBeTruthy()
      expect(wrapper.emitted('scan')![0]).toEqual(['CDE'])
      wrapper.unmount()
    })

    it('keeps manually typed content when active turns false (only captured fragments are dropped)', async () => {
      const wrapper = mount(ScanBar, { attachTo: document.body })
      await wrapper.get('input').setValue('DRAFT') // 手工内容，无未消费捕获
      await wrapper.setProps({ active: false })
      expect((wrapper.get('input').element as HTMLInputElement).value).toBe('DRAFT')
      wrapper.unmount()
    })
  })
})
