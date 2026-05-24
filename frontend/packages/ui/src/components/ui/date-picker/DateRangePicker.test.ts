import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import DateRangePicker from './DateRangePicker.vue'

describe('DateRangePicker', () => {
  it('resets the draft and closes the popover when cancelling', async () => {
    const modelValue = { from: '2026-05-01', to: '2026-05-07' }
    const wrapper = mount(DateRangePicker, {
      attachTo: document.body,
      props: {
        modelValue,
      },
    })

    await wrapper.get('button').trigger('click')
    await flushPromises()

    const startInput = document.body.querySelector<HTMLInputElement>('input[aria-label="Start date"]')
    expect(startInput).not.toBeNull()

    startInput!.value = '2026-05-03'
    startInput!.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const cancelButton = Array.from(document.body.querySelectorAll('button'))
      .find(button => button.textContent?.includes('Cancel'))
    expect(cancelButton).toBeDefined()

    cancelButton!.click()
    await flushPromises()

    expect(wrapper.emitted('cancel')).toHaveLength(1)
    expect(wrapper.emitted('update:modelValue')?.[0]).toEqual([modelValue])
    expect(document.body.querySelector('[data-slot="popover-content"]')).toBeNull()

    wrapper.unmount()
    document.body.innerHTML = ''
  })
})
