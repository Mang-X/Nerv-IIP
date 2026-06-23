import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import DateRangePicker from './DateRangePicker.vue'

describe('DateRangePicker', () => {
  it('renders a range calendar with the current selection', async () => {
    const modelValue = { from: '2026-05-01', to: '2026-05-07' }
    const wrapper = mount(DateRangePicker, {
      attachTo: document.body,
      props: {
        modelValue,
      },
    })

    await wrapper.get('button').trigger('click')
    await flushPromises()

    expect(document.body.querySelector('[data-slot="range-calendar"]')).not.toBeNull()

    wrapper.unmount()
    document.body.innerHTML = ''
  })

  it('clears the selected range and emits null', async () => {
    const wrapper = mount(DateRangePicker, {
      attachTo: document.body,
      props: {
        modelValue: { from: '2026-05-01', to: '2026-05-07' },
      },
    })

    await wrapper.get('button').trigger('click')
    await flushPromises()

    const clearButton = Array.from(document.body.querySelectorAll('button'))
      .find(button => button.textContent?.includes('清除'))
    expect(clearButton).toBeDefined()

    clearButton!.click()
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([null])
    expect(wrapper.emitted('clear')).toHaveLength(1)
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull()

    wrapper.unmount()
    document.body.innerHTML = ''
  })
})
