import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NvInput from './NvInput.vue'

describe('NvInput', () => {
  it('uses the utility-layer destructive border only when invalid', () => {
    const invalidInput = mount(NvInput, { props: { invalid: true } })
    const normalInput = mount(NvInput)

    expect(invalidInput.attributes('data-invalid')).toBe('true')
    expect(invalidInput.classes()).toContain('data-[invalid=true]:border-destructive')
    expect(normalInput.classes()).toContain('border-input')
    expect(normalInput.attributes('data-invalid')).toBeUndefined()
  })
})
