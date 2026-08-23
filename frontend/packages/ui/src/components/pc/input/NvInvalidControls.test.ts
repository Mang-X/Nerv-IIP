import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent } from 'vue'
import NvInput from './NvInput.vue'
import NvSelect from '../select/NvSelect.vue'
import NvSelectTrigger from '../select/NvSelectTrigger.vue'

describe('NvUI invalid controls', () => {
  it('gives NvInput an invalid utility that survives host cascade layers', () => {
    const wrapper = mount(NvInput, { props: { invalid: true } })
    const frame = wrapper.get('[data-slot="nv-input"]')

    expect(frame.attributes('data-invalid')).toBe('true')
    expect(frame.classes()).toContain('data-[invalid=true]:border-destructive')
  })

  it('gives NvSelectTrigger the same invalid utility contract', () => {
    const Host = defineComponent({
      components: { NvSelect, NvSelectTrigger },
      template: '<NvSelect><NvSelectTrigger invalid /></NvSelect>',
    })
    const wrapper = mount(Host)
    const trigger = wrapper.get('[data-slot="nv-select-trigger"]')

    expect(trigger.attributes('data-invalid')).toBe('true')
    expect(trigger.classes()).toContain('data-[invalid=true]:border-destructive')
  })
})
