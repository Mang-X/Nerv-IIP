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
  })
})
