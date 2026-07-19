import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import InventoryExpiryStatusBadge from './InventoryExpiryStatusBadge.vue'

describe('InventoryExpiryStatusBadge', () => {
  it('同时呈现文字和严重级别，不只依赖颜色', () => {
    const wrapper = mount(InventoryExpiryStatusBadge, {
      props: {
        line: {
          expiryDate: '2026-07-25',
          daysUntilExpiry: 6,
          isExpired: false,
          isNearExpiry: true,
          blockReason: '已过期，常规移动需授权放行。',
        },
      },
      global: {
        stubs: {
          NvStatusBadge: {
            props: ['value', 'label', 'tone'],
            template: '<span :data-value="value" :data-tone="tone">{{ label }}</span>',
          },
        },
      },
    })

    expect(wrapper.text()).toContain('临近过期')
    expect(wrapper.text()).toContain('已过期，常规移动需授权放行。')
    expect(wrapper.get('span').attributes('data-value')).toBe('critical')
    expect(wrapper.get('span').attributes('data-tone')).toBe('danger')
  })
})
