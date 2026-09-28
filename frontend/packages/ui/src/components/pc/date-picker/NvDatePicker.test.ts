import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvDatePicker from './NvDatePicker.vue'

describe('NvDatePicker', () => {
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

  // 宽度只由 class 给：组件自带一个 `sm:w-48` 就会和外层字段壳的宽度抢同一个
  // 属性，胜负交给 Tailwind 生成顺序（#3735）。
  it('触发按钮不自带任何宽度类', () => {
    const wrapper = mount(NvDatePicker)

    expect(wrapper.get('button').classes()).not.toContain('sm:w-48')
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
