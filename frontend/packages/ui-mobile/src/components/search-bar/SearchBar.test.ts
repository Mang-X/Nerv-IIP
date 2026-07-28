import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SearchBar from './SearchBar.vue'

describe('SearchBar', () => {
  it('keeps search, clear and cancel hit boxes at least 48px by contract', async () => {
    const wrapper = mount(SearchBar, {
      props: {
        cancelable: true,
        modelValue: '轴承',
      },
    })

    const input = wrapper.get('input')
    const clear = wrapper.get('button[aria-label="清除"]')
    const cancel = wrapper.get('button:not([aria-label])')

    expect(input.classes()).toContain('min-h-touch')
    expect(clear.classes()).toEqual(expect.arrayContaining(['min-h-touch', 'min-w-12']))
    expect(cancel.classes()).toEqual(expect.arrayContaining(['min-h-touch', 'min-w-12']))
  })

  it('retains Enter search, clear and cancel behavior', async () => {
    const wrapper = mount(SearchBar, {
      props: {
        cancelable: true,
        modelValue: '轴承',
        'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      },
    })

    await wrapper.get('input').trigger('keydown.enter')
    expect(wrapper.emitted('search')).toEqual([['轴承']])

    await wrapper.get('button[aria-label="清除"]').trigger('click')
    expect(wrapper.props('modelValue')).toBe('')

    await wrapper.setProps({ modelValue: '机床' })
    await wrapper.get('input').trigger('focus')
    await wrapper.get('button:not([aria-label])').trigger('click')
    expect(wrapper.props('modelValue')).toBe('')
    expect(wrapper.emitted('cancel')).toEqual([[]])
  })
})
