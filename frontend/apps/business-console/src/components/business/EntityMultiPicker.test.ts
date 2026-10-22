import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { EntityPickerOption } from '@nerv-iip/ui'

import EntityMultiPicker from './EntityMultiPicker.vue'

const stubs = {
  NvEntityPicker: {
    props: ['options', 'search', 'serverSearch', 'totalCount', 'searchPlaceholder'],
    emits: ['update:modelValue', 'update:search'],
    template: `
      <div data-testid="picker"
        :data-search="search"
        :data-server-search="serverSearch || undefined"
        :data-total-count="totalCount"
        :data-search-placeholder="searchPlaceholder">
        <button v-for="option in options" :key="option.value" type="button"
          @click="$emit('update:modelValue', option.value)">{{ option.label }}</button>
        <input aria-label="目录搜索" :value="search"
          @input="$emit('update:search', $event.target.value)" />
      </div>`,
  },
  NvBadge: { template: '<span><slot /></span>' },
  XIcon: { template: '<i />' },
}

function mountPicker(props: {
  options: EntityPickerOption[]
  title: string
  modelValue?: string
  search?: string
  serverSearch?: boolean
  totalCount?: number
  searchPlaceholder?: string
}) {
  return mount(EntityMultiPicker, { props, global: { stubs } })
}

describe('EntityMultiPicker', () => {
  it('默认使用真实实体选择器的本地搜索过滤候选', async () => {
    const wrapper = mount(EntityMultiPicker, {
      props: {
        options: [
          { value: 'SUP-A', label: '华东钢材' },
          { value: 'SUP-B', label: '江南紧固件' },
        ],
        title: '选择供应商',
      },
      attachTo: document.body,
    })

    await wrapper.get('button[aria-haspopup="listbox"]').trigger('click')
    await flushPromises()
    const search = document.body.querySelector<HTMLInputElement>('input[role="combobox"]')
    expect(search).not.toBeNull()
    await search!.setRangeText('江南')
    search!.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const listboxText = document.body.querySelector('[role="listbox"]')?.textContent ?? ''
    expect(listboxText).not.toContain('华东钢材')
    expect(listboxText).toContain('江南紧固件')

    wrapper.unmount()
  })

  it('保持既有本地目录消费者的逗号字符串增删语义', async () => {
    const wrapper = mountPicker({
      modelValue: 'SUP-A',
      options: [
        { value: 'SUP-A', label: '华东钢材' },
        { value: 'SUP-B', label: '江南紧固件' },
      ],
      title: '选择供应商',
    })

    await wrapper.get('[data-testid="picker"] button').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['SUP-A,SUP-B'])
    await wrapper.setProps({ modelValue: 'SUP-A,SUP-B' })
    await wrapper.get('button[aria-label="移除 华东钢材"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['SUP-B'])
  })

  it('转发服务端搜索契约，并让跨搜索选项保留人读标签且可移除', async () => {
    const wrapper = mountPicker({
      modelValue: 'WC-PRESS',
      options: [{ value: 'WC-PRESS', label: '冲压工作中心' }],
      title: '选择工作中心',
      search: '冲压',
      serverSearch: true,
      totalCount: 203,
      searchPlaceholder: '搜索工作中心名称 / 编码',
    })

    const picker = wrapper.get('[data-testid="picker"]')
    expect(picker.attributes()).toMatchObject({
      'data-search': '冲压',
      'data-server-search': 'true',
      'data-total-count': '203',
      'data-search-placeholder': '搜索工作中心名称 / 编码',
    })
    await picker.get('input').setValue('精加工')
    expect(wrapper.emitted('update:search')?.at(-1)).toEqual(['精加工'])

    await wrapper.setProps({
      search: '精加工',
      options: [{ value: 'WC-MACHINING-201', label: '精加工工作中心' }],
      modelValue: 'WC-PRESS,WC-MACHINING-201',
      totalCount: 1,
    })
    expect(wrapper.text()).toContain('冲压工作中心')
    expect(wrapper.text()).toContain('精加工工作中心')

    await wrapper.get('button[aria-label="移除 冲压工作中心"]').trigger('click')
    expect(wrapper.emitted('update:modelValue')?.at(-1)).toEqual(['WC-MACHINING-201'])
  })
})
