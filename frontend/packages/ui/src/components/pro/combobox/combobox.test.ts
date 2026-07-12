import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvCombobox from './ComboboxPro.vue'
import NvSearchSelect from './SearchSelectPro.vue'

describe('NvSearchSelect', () => {
  const options = [
    { value: 'user-1', label: '张工', hint: 'E001' },
    { value: 'user-2', label: '李工', hint: 'E002' },
  ]

  it('shows the placeholder when nothing is selected and the label when it is', () => {
    const empty = mount(NvSearchSelect, { props: { options, placeholder: '未指派' } })
    expect(empty.text()).toContain('未指派')

    const selected = mount(NvSearchSelect, { props: { options, modelValue: 'user-2' } })
    expect(selected.text()).toContain('李工')
  })

  it('renders a button trigger (selection-only, no free text input on the trigger)', () => {
    const wrapper = mount(NvSearchSelect, { props: { options } })
    const trigger = wrapper.find('button[type="button"]')
    expect(trigger.exists()).toBe(true)
    // the trigger itself is not a text input
    expect(wrapper.find('button input').exists()).toBe(false)
  })
})

describe('NvCombobox', () => {
  const suggestions = [{ value: '轴承温度' }, { value: '振动' }]

  it('reflects modelValue in the input and emits the raw typed text (free entry)', async () => {
    const wrapper = mount(NvCombobox, { props: { modelValue: '轴承温度', suggestions } })
    const input = wrapper.get('input')
    expect((input.element as HTMLInputElement).value).toBe('轴承温度')

    await input.setValue('自定义特性') // not in suggestions — still accepted
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['自定义特性'])
  })

  it('is a combobox-role input', () => {
    const wrapper = mount(NvCombobox, { props: { suggestions } })
    expect(wrapper.get('input').attributes('role')).toBe('combobox')
  })
})
