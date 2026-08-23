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
})
