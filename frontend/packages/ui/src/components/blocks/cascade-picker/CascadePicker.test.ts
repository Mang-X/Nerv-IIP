import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvCascadePicker from './NvCascadePicker.vue'

const levels = [
  {
    key: 'workshop',
    label: '车间',
    options: [{ value: 'WS-01', label: '冲压车间' }],
  },
  {
    key: 'line',
    label: '产线',
    options: [{ value: 'LINE-01', label: '冲压一线' }],
  },
  {
    key: 'device',
    label: '设备',
    options: [{ value: 'DEV-PRESS-01', label: '冲压机 01' }],
  },
]

describe('NvCascadePicker', () => {
  it('renders one labeled searchable select per level, defaulting to 全部', () => {
    const wrapper = mount(NvCascadePicker, {
      props: { levels, modelValue: { workshop: '', line: '', device: '' } },
    })
    expect(wrapper.text()).toContain('车间')
    expect(wrapper.text()).toContain('产线')
    expect(wrapper.text()).toContain('设备')
    const triggers = wrapper.findAll('button[aria-haspopup="listbox"]')
    expect(triggers).toHaveLength(3)
    for (const trigger of triggers) expect(trigger.text()).toContain('全部')
  })

  it('shows the selected labels from modelValue', () => {
    const wrapper = mount(NvCascadePicker, {
      props: { levels, modelValue: { workshop: 'WS-01', line: 'LINE-01', device: '' } },
    })
    expect(wrapper.text()).toContain('冲压车间')
    expect(wrapper.text()).toContain('冲压一线')
  })

  it('clears downstream levels when an upstream level changes', () => {
    const wrapper = mount(NvCascadePicker, {
      props: {
        levels,
        modelValue: { workshop: 'WS-01', line: 'LINE-01', device: 'DEV-PRESS-01' },
      },
    })
    const cascade = wrapper.findComponent(NvCascadePicker)
    const selects = cascade.findAllComponents({ name: 'NvSearchSelect' })
    expect(selects).toHaveLength(3)
    selects[0]!.vm.$emit('update:modelValue', '')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual([
      { workshop: '', line: '', device: '' },
    ])
  })
})
