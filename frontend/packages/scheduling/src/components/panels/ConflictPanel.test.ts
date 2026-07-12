import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ConflictPanel from './ConflictPanel.vue'

describe('ConflictPanel', () => {
  it('renders business-language reason labels and emits select with taskId', async () => {
    const wrapper = mount(ConflictPanel, {
      props: {
        conflicts: [
          { id: 'c1', reason: 'capacity', severity: 'warning', orderId: 'WO-001', message: '产能不足', taskId: 'a2' },
        ],
      },
    })
    expect(wrapper.text()).toContain('产能不足')
    // 不暴露工程语言
    expect(wrapper.text()).not.toMatch(/capacity|operationId|conflictId|reasonCode/)
    await wrapper.find('[data-conflict-id="c1"]').trigger('click')
    expect(wrapper.emitted('select')?.[0]).toEqual(['a2'])
  })

  it('shows an empty state when there are no conflicts', () => {
    const wrapper = mount(ConflictPanel, { props: { conflicts: [] } })
    expect(wrapper.text()).toContain('无冲突')
  })
})
