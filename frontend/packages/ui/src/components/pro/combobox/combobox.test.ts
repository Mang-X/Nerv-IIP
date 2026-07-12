import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvCombobox from './NvCombobox.vue'
import NvSearchSelect from './NvSearchSelect.vue'

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

  it('exposes listbox popup semantics on the trigger (a11y)', () => {
    const wrapper = mount(NvSearchSelect, { props: { options } })
    const trigger = wrapper.get('button[type="button"]')
    expect(trigger.attributes('aria-haspopup')).toBe('listbox')
    expect(trigger.attributes('aria-expanded')).toBe('false')
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

  it('is a combobox-role input with listbox-autocomplete a11y wiring', () => {
    const wrapper = mount(NvCombobox, { props: { suggestions } })
    const input = wrapper.get('input')
    expect(input.attributes('role')).toBe('combobox')
    expect(input.attributes('aria-autocomplete')).toBe('list')
    expect(input.attributes('aria-controls')).toBeTruthy()
  })

  // 回归：旧实现按 props.modelValue（落后一拍）过滤，输入切到无匹配后弹层不关。
  // 现按本次输入的 query 过滤，开关判断与当前输入一致。
  it('filters by the current typed value — closes when the new value matches nothing', async () => {
    const wrapper = mount(NvCombobox, { props: { modelValue: '轴', suggestions } })
    const input = wrapper.get('input')

    // 从匹配（轴 → 轴承温度）切到无匹配（xyz）：弹层应收起。
    await input.setValue('xyz')
    expect(input.attributes('aria-expanded')).toBe('false')

    // 从无匹配切回匹配（振 → 振动）：弹层应展开。
    await input.setValue('振')
    expect(input.attributes('aria-expanded')).toBe('true')
  })

  it('keeps filtering in sync when modelValue is updated externally', async () => {
    const wrapper = mount(NvCombobox, { props: { modelValue: '', suggestions } })
    const input = wrapper.get('input')
    await wrapper.setProps({ modelValue: '轴承温度' })
    expect((input.element as HTMLInputElement).value).toBe('轴承温度')
  })
})
