import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvEntityPicker from './NvEntityPicker.vue'

const options = [
  { value: 'SKU-FG-100', label: '前减振器总成', hint: '成品' },
  { value: 'RM-200', label: '活塞杆毛坯', hint: '原材料' },
]

describe('NvEntityPicker', () => {
  it('shows the placeholder when nothing is selected, and name + code when selected', () => {
    const empty = mount(NvEntityPicker, {
      props: { options, title: '选择物料', placeholder: '请选择物料' },
    })
    expect(empty.text()).toContain('请选择物料')

    const selected = mount(NvEntityPicker, {
      props: { options, title: '选择物料', modelValue: 'RM-200' },
    })
    expect(selected.text()).toContain('活塞杆毛坯')
    expect(selected.text()).toContain('RM-200')
  })

  it('is a selection-only dialog trigger (no free text input on the trigger)', () => {
    const wrapper = mount(NvEntityPicker, { props: { options, title: '选择物料' } })
    const trigger = wrapper.get('button[type="button"]')
    expect(trigger.attributes('aria-haspopup')).toBe('dialog')
    expect(wrapper.find('input').exists()).toBe(false)
  })

  it('opens a dialog with search + entity list and picks by click', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料', sourceText: '数据来自物料主数据' },
      attachTo: document.body,
    })
    await wrapper.get('button[type="button"]').trigger('click')
    await flushPromises()

    const search = document.body.querySelector<HTMLInputElement>('input[role="combobox"]')
    expect(search).not.toBeNull()
    const list = document.body.querySelector('[role="listbox"]')
    expect(list?.textContent).toContain('前减振器总成')
    expect(document.body.textContent).toContain('数据来自物料主数据')

    const optionButtons = [...document.body.querySelectorAll<HTMLButtonElement>('[role="option"]')]
    optionButtons.find((b) => b.textContent?.includes('RM-200'))?.click()
    await flushPromises()
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['RM-200'])
    wrapper.unmount()
  })

  it('clears the selection via the clear affordance when clearable', async () => {
    const wrapper = mount(NvEntityPicker, {
      props: { options, title: '选择物料', modelValue: 'RM-200', clearable: true, ariaLabel: '物料' },
    })
    await wrapper.get('button[aria-label="清除物料"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([''])
  })
})
