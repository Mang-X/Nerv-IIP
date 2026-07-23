import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import UrgencyDisplayModeSelect from './UrgencyDisplayModeSelect.vue'

const selectStubs = {
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  // Trigger/content render as fragments so the option stubs stay direct <select>
  // children — otherwise native setValue cannot resolve the option by value.
  NvSelectTrigger: { template: '<slot />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

describe('UrgencyDisplayModeSelect', () => {
  it('offers all seven display modes', () => {
    const wrapper = mount(UrgencyDisplayModeSelect, {
      props: { modelValue: 'level' },
      global: { stubs: selectStubs },
    })

    const options = wrapper.findAll('option')
    expect(options).toHaveLength(7)
    expect(options.map((o) => o.attributes('value'))).toEqual([
      'level',
      'businessPriority',
      'dynamicUrgency',
      'executionRisk',
      'criticalRatio',
      'slack',
      'expectedDelay',
    ])
    expect(wrapper.text()).toContain('Critical Ratio')
    expect(wrapper.text()).toContain('预计延迟')
  })

  it('emits the selected mode via v-model', async () => {
    const wrapper = mount(UrgencyDisplayModeSelect, {
      props: { modelValue: 'level' },
      global: { stubs: selectStubs },
    })

    await wrapper.get('select').setValue('criticalRatio')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['criticalRatio'])
  })
})
