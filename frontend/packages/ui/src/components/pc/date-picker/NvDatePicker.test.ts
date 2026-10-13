import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import NvDatePicker from './NvDatePicker.vue'

describe('NvDatePicker', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  // PublicContract：#4385 与 #3905 获批 spec r1，locale 只影响日历日期展示。
  it.each([
    ['zh-CN', '2026/1/1'],
    ['en-US', '1/1/2026'],
  ])('按 %s 显示固定日历日期，保持 ISO 模型', (locale, expected) => {
    const wrapper = mount(NvDatePicker, { props: { modelValue: '2026-01-01', locale } })
    expect(wrapper.get('button').text()).toBe(expected)
    expect(wrapper.props('modelValue')).toBe('2026-01-01')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    wrapper.unmount()
  })

  it('响应式 locale 切换只更新展示；未传 locale 保留 ISO，空值保留占位', async () => {
    const wrapper = mount(NvDatePicker, {
      props: { modelValue: '2026-06-18', locale: 'zh-CN', placeholder: '业务日期' },
    })
    expect(wrapper.get('button').text()).toBe('2026/6/18')
    await wrapper.setProps({ locale: 'en-US' })
    expect(wrapper.get('button').text()).toBe('6/18/2026')
    expect(wrapper.props('modelValue')).toBe('2026-06-18')
    expect(wrapper.emitted('update:modelValue')).toBeUndefined()
    await wrapper.setProps({ locale: undefined })
    expect(wrapper.get('button').text()).toBe('2026-06-18')
    await wrapper.setProps({ modelValue: '', locale: 'zh-CN' })
    expect(wrapper.get('button').text()).toBe('业务日期')
    wrapper.unmount()
  })

  it('本地化展示下真实日历选择回传 ISO，清除回传空串', async () => {
    const wrapper = mount(NvDatePicker, {
      attachTo: document.body,
      props: {
        modelValue: '2026-06-18',
        locale: 'en-US',
        clearable: true,
        'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      },
    })
    await wrapper.get('button').trigger('click')
    await flushPromises()
    const day = Array.from(document.querySelectorAll<HTMLButtonElement>('.nv-dp-cell')).find(
      (cell) => cell.textContent?.trim() === '19',
    )!
    day.click()
    await flushPromises()
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual(['2026-06-19'])
    expect(wrapper.get('button').text()).toBe('6/19/2026')
    await wrapper.get('[aria-label="清除所选日期"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.[1]).toEqual([''])
    expect(wrapper.get('button').text()).toBe('选择日期')
    wrapper.unmount()
  })

  it('本地化展示保留禁用和标签、校验关联', () => {
    const wrapper = mount(NvDatePicker, {
      props: {
        modelValue: '2026-06-18',
        locale: 'zh-CN',
        disabled: true,
        clearable: true,
        id: 'business-date',
        ariaInvalid: true,
        ariaDescribedby: 'date-error',
      },
    })
    const trigger = wrapper.get('button')
    expect(trigger.attributes('disabled')).toBeDefined()
    expect(trigger.attributes('id')).toBe('business-date')
    expect(trigger.attributes('aria-invalid')).toBe('true')
    expect(trigger.attributes('aria-describedby')).toBe('date-error')
    expect(wrapper.find('[aria-label="清除所选日期"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('forwards validation semantics to the focusable trigger', () => {
    const wrapper = mount(NvDatePicker, {
      props: {
        id: 'plan-date',
        ariaInvalid: true,
        ariaDescribedby: 'plan-date-error',
      },
    })
    const trigger = wrapper.get('button')

    expect(trigger.attributes('id')).toBe('plan-date')
    expect(trigger.attributes('aria-invalid')).toBe('true')
    expect(trigger.attributes('aria-describedby')).toBe('plan-date-error')
  })

  // 宽度只由 class 给：组件自带任何 `w-*` 都会和外层字段壳 / 调用方给的
  // `sm:w-*` 抢同一个属性，胜负交给 Tailwind 生成顺序（#3735）。只断言「不含
  // `sm:w-48`」是假防线——复核实测把它换成 `sm:w-56`，按钮溢出包裹层 80px 而
  // 全套断言照绿。
  it('触发按钮不自带任何宽度类', () => {
    // `w-full` 是组件自己的（按钮占满包裹 div，是布局的一部分）；要禁的是
    // **断点宽度**——`sm:w-*` 才是和调用方 / 字段壳抢同一个属性的那个。
    const breakpointWidths = (element: Element) =>
      Array.from(element.classList).filter((name) => /^(sm|md|lg|xl|2xl):w-/.test(name))
    // 组件根是 Popover 渲染的注释节点，包裹 div 得按 data-slot 找。
    const wrapperOf = (element: Element) => {
      const found = element.querySelector('[data-slot="nv-date-picker"]')
      if (!found) throw new Error('未找到 NvDatePicker 的宽度包裹层')
      return found
    }

    const bare = mount(NvDatePicker)
    expect(breakpointWidths(bare.get('button').element)).toEqual([])
    // 包裹 div 同理：只允许占满壳给的宽度。
    expect(breakpointWidths(wrapperOf(bare.element))).toEqual([])
    // 调用方没给宽度时，包裹 div 就是 `w-full`，按钮占满壳给的宽度。
    expect(wrapperOf(bare.element).classList.contains('w-full')).toBe(true)
    expect(bare.get('button').classes()).toContain('w-full')

    // 调用方给的断点宽度落到包裹 div 上（它自带的 `w-full` 与调用方的同名类在
    // twMerge 里合流，断点那一档才是新出现的）。
    const sized = mount(NvDatePicker, { props: { class: 'w-full sm:w-48' } })
    expect(breakpointWidths(wrapperOf(sized.element))).toEqual(['sm:w-48'])
  })

  it('clearable 打开且已选中时给出清除入口，清除回传空串', async () => {
    const wrapper = mount(NvDatePicker, {
      props: { modelValue: '2026-06-18', clearable: true },
    })

    await wrapper.get('[aria-label="清除所选日期"]').trigger('click')

    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([''])
  })

  it('没有 clearable 就不给清除入口', () => {
    const wrapper = mount(NvDatePicker, { props: { modelValue: '2026-06-18' } })

    expect(wrapper.find('[aria-label="清除所选日期"]').exists()).toBe(false)
  })
})
