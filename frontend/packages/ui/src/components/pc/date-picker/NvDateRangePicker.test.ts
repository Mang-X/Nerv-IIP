import type { DateRange } from './NvDateRangePicker.vue'
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import NvDateRangePicker from './NvDateRangePicker.vue'

describe('NvDateRangePicker', () => {
  // 双日期标签（`2026-06-10 ~ 2026-06-18`，约 20 字符）远宽于组件自带的 64px
  // 默认档，不截断会换行把按钮从 h-9 顶高。属于呈现行为，走真渲染断言。
  it('双日期标签在窄触发器上被截断而不是换行', () => {
    const wrapper = mount(NvDateRangePicker, {
      props: { modelValue: { start: '2026-06-10', end: '2026-06-18' } },
    })

    const label = wrapper.get('span.truncate')
    expect(label.text()).toContain('2026-06-10')
    expect(label.text()).toContain('2026-06-18')
  })

  // 宽度只由调用方的 `class` 给；组件自带任何断点宽度都会和字段壳 / 调用方
  // 抢同一个属性。`class` 是声明 prop，不在模板里显式绑就是**确定性丢弃**，
  // 调用方传什么都不生效且不报错（#3735）。
  it('触发按钮不自带任何断点宽度类', () => {
    const breakpointWidths = (element: Element) =>
      Array.from(element.classList).filter((name) => /^(sm|md|lg|xl|2xl):w-/.test(name))
    // 组件根是 Popover 渲染的注释节点，包裹 div 得按 data-slot 找。
    const wrapperOf = (element: Element) => {
      const found = element.querySelector('[data-slot="nv-date-range-picker"]')
      if (!found) throw new Error('未找到 NvDateRangePicker 的宽度包裹层')
      return found
    }

    const bare = mount(NvDateRangePicker)
    expect(breakpointWidths(bare.get('button').element)).toEqual([])
    expect(breakpointWidths(wrapperOf(bare.element))).toEqual([])

    // 调用方给的断点宽度落到包裹 div 上（组件自带的 `w-full` 与调用方同名类在
    // twMerge 里合流，断点那一档才是新出现的）。
    const sized = mount(NvDateRangePicker, { props: { class: 'w-full sm:w-64' } })
    expect(breakpointWidths(wrapperOf(sized.element))).toEqual(['sm:w-64'])
  })

  it('选完区间回传出规范排序的两端', async () => {
    const wrapper = mount(NvDateRangePicker, { attachTo: document.body })

    await wrapper.get('button').trigger('click')
    // 日历浮层经 `PopoverPortal` 渲染到 `document.body`，不在 wrapper 树里。
    // 顺带取当月：默认游标是「今天」所在月，写死 6/10、6/18 会在别的月份跑挂。
    const today = new Date()
    const daysInMonth = new Date(today.getFullYear(), today.getMonth() + 1, 0).getDate()
    const late = Math.min(18, daysInMonth)
    const early = 1

    const click = async (day: number) => {
      const cell = Array.from(document.querySelectorAll('button')).find(
        (button) =>
          !button.hasAttribute('aria-label') && button.textContent?.trim() === String(day),
      )
      if (!cell) throw new Error(`浮层里找不到 ${day} 号`)
      cell.click()
      await nextTick()
    }

    // 先点靠后的、再点靠前的：两端应被自动排序（票面口径）。
    await click(late)
    await click(early)

    const last = wrapper.emitted('update:modelValue')?.at(-1)?.[0] as DateRange | undefined
    const pad = (n: number) => String(n).padStart(2, '0')
    const month = `${today.getFullYear()}-${pad(today.getMonth() + 1)}`
    expect(last).toEqual({ start: `${month}-${pad(early)}`, end: `${month}-${pad(late)}` })

    wrapper.unmount()
  })
})
