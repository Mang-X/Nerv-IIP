import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import Cell from './Cell.vue'

describe('NvCell keyboard activation', () => {
  it.each(['Enter', ' '])('emits one click for the %s key when interactive', async (key) => {
    const wrapper = mount(Cell, { props: { title: '生产作业', arrow: true } })

    await wrapper.get('[role="button"]').trigger('keydown', { key })

    expect(wrapper.emitted('click')).toHaveLength(1)
  })

  it('does not make an informational cell keyboard interactive', async () => {
    const wrapper = mount(Cell, { props: { title: '工号', value: 'EMP-049' } })

    await wrapper.get('[data-slot="cell"]').trigger('keydown', { key: 'Enter' })

    expect(wrapper.emitted('click')).toBeUndefined()
  })
})
