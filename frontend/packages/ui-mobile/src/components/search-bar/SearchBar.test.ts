import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SearchBar from './SearchBar.vue'

describe('SearchBar', () => {
  it('forwards an explicit accessible name to the native search input', () => {
    const wrapper = mount(SearchBar, {
      props: { ariaLabel: '维修工单关键字' },
    })

    expect(wrapper.get('input[type="search"]').attributes('aria-label')).toBe('维修工单关键字')
  })

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

  it('reports the emptied keyword through @search so list-only consumers can restore the full list', async () => {
    const wrapper = mount(SearchBar, {
      props: {
        modelValue: '轴承',
        'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      },
    })

    await wrapper.get('button[aria-label="清除"]').trigger('click')

    expect(wrapper.props('modelValue')).toBe('')
    expect(wrapper.emitted('search')).toEqual([['']])
  })

  it('reports the emptied keyword when the field is deleted down to empty without Enter', async () => {
    const wrapper = mount(SearchBar, {
      props: {
        modelValue: '轴承',
        'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      },
    })

    const input = wrapper.get('input[type="search"]')
    ;(input.element as HTMLInputElement).value = ''
    await input.trigger('input')

    expect(wrapper.props('modelValue')).toBe('')
    expect(wrapper.emitted('search')).toEqual([['']])
  })

  it('keeps cancel as "leave search", not as an implicit empty search', async () => {
    const wrapper = mount(SearchBar, {
      props: {
        cancelable: true,
        modelValue: '轴承',
        'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      },
    })

    await wrapper.get('input').trigger('focus')
    await wrapper.get('button:not([aria-label])').trigger('click')

    expect(wrapper.props('modelValue')).toBe('')
    expect(wrapper.emitted('cancel')).toEqual([[]])
    expect(wrapper.emitted('search')).toBeUndefined()
  })

  it('does not decide for the consumer that a whitespace-only keyword counts as cleared', async () => {
    const wrapper = mount(SearchBar, {
      props: {
        modelValue: '轴承',
        'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
      },
    })

    const input = wrapper.get('input[type="search"]')
    ;(input.element as HTMLInputElement).value = '   '
    await input.trigger('input')

    expect(wrapper.props('modelValue')).toBe('   ')
    expect(wrapper.emitted('search')).toBeUndefined()
  })

  it('stays silent when the consumer empties the binding itself', async () => {
    const wrapper = mount(SearchBar, { props: { modelValue: '轴承' } })

    await wrapper.setProps({ modelValue: '' })

    expect(wrapper.emitted('search')).toBeUndefined()
  })

  /**
   * 结构性护栏：不点名具体按钮，而是枚举当前渲染出的每个可点击控件。将来给搜索栏加了新的
   * 「清空」入口，只要它把关键词清空却不通知消费方（`search('')` 或 `cancel`），这里就红。
   */
  it('leaves no affordance that empties the keyword without notifying the consumer', async () => {
    const probe = mount(SearchBar, { props: { cancelable: true, modelValue: '轴承' } })
    const affordanceCount = probe.findAll('button').length
    expect(affordanceCount).toBeGreaterThanOrEqual(2)

    for (let index = 0; index < affordanceCount; index += 1) {
      const wrapper = mount(SearchBar, {
        props: {
          cancelable: true,
          modelValue: '轴承',
          'onUpdate:modelValue': (value: string) => wrapper.setProps({ modelValue: value }),
        },
      })
      const affordance = wrapper.findAll('button')[index]!
      const label = affordance.attributes('aria-label') ?? affordance.text() ?? `#${index}`

      await affordance.trigger('click')
      if (wrapper.props('modelValue') !== '') continue

      const announced =
        wrapper.emitted('search')?.some(([value]) => value === '') === true ||
        wrapper.emitted('cancel') !== undefined
      expect(announced, `「${label}」清空了关键词却没有通知消费方`).toBe(true)
    }
  })
})
