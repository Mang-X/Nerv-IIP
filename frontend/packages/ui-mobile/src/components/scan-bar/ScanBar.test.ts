import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ScanBar from './ScanBar.vue'

describe('ScanBar', () => {
  it('emits scan with the buffered value on Enter and clears the input', async () => {
    const wrapper = mount(ScanBar)
    const input = wrapper.get('input')
    await input.setValue('SKU-12345')
    await input.trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('scan')).toBeTruthy()
    expect(wrapper.emitted('scan')![0]).toEqual(['SKU-12345'])
    expect((input.element as HTMLInputElement).value).toBe('')
  })

  it('ignores Enter on an empty buffer', async () => {
    const wrapper = mount(ScanBar)
    await wrapper.get('input').trigger('keydown', { key: 'Enter' })
    expect(wrapper.emitted('scan')).toBeFalsy()
  })

  it('renders the provided placeholder', () => {
    const wrapper = mount(ScanBar, { props: { placeholder: '扫描库位或物料' } })
    expect(wrapper.get('input').attributes('placeholder')).toBe('扫描库位或物料')
  })
})
