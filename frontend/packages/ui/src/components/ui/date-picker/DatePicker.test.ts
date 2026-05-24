import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import DatePicker from './DatePicker.vue'

describe('DatePicker', () => {
  it('closes the popover after applying or clearing a date', async () => {
    const wrapper = mount(DatePicker, {
      attachTo: document.body,
      props: {
        modelValue: '2026-05-01',
      },
    })

    await wrapper.get('button').trigger('click')
    await flushPromises()

    const input = document.body.querySelector<HTMLInputElement>('input[aria-label="Date"]')
    expect(input).not.toBeNull()

    input!.value = '2026-05-02'
    input!.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const applyButton = Array.from(document.body.querySelectorAll('button'))
      .find(button => button.textContent?.includes('Apply'))
    expect(applyButton).toBeDefined()

    applyButton!.click()
    await flushPromises()

    expect(wrapper.emitted('apply')?.[0]).toEqual(['2026-05-02'])
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull()

    await wrapper.get('button').trigger('click')
    await flushPromises()

    const clearButton = Array.from(document.body.querySelectorAll('button'))
      .find(button => button.textContent?.includes('Clear'))
    expect(clearButton).toBeDefined()

    clearButton!.click()
    await flushPromises()

    expect(wrapper.emitted('clear')).toHaveLength(1)
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull()

    wrapper.unmount()
    document.body.innerHTML = ''
  })
})
