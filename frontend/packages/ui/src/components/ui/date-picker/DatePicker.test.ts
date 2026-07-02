import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import DatePicker from './DatePicker.vue'

describe('DatePicker', () => {
  it('selects a day from the calendar and emits a YYYY-MM-DD string', async () => {
    const wrapper = mount(DatePicker, {
      attachTo: document.body,
      props: {
        modelValue: '2026-05-01',
      },
    })

    await wrapper.get('button').trigger('click')
    await flushPromises()

    const calendar = document.body.querySelector('[data-slot="calendar"]')
    expect(calendar).not.toBeNull()

    const triggers = Array.from(
      document.body.querySelectorAll<HTMLElement>('[data-slot="calendar-cell-trigger"]'),
    )
    const target = triggers.find(el => el.textContent?.trim() === '2')
    expect(target).toBeDefined()

    target!.click()
    await flushPromises()

    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['2026-05-02'])
    expect(wrapper.emitted('apply')?.at(-1)).toEqual(['2026-05-02'])
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull()

    wrapper.unmount()
    document.body.innerHTML = ''
  })

  it('clears the selected date and emits null', async () => {
    const wrapper = mount(DatePicker, {
      attachTo: document.body,
      props: {
        modelValue: '2026-05-01',
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
