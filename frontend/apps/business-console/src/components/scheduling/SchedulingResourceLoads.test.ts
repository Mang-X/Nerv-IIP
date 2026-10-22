import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SchedulingResourceLoads from './SchedulingResourceLoads.vue'
import { formatDateTime } from '@/utils/format'

// Regression / #4463：同一资源的多个零负荷窗口不可混为一行或静默消失。
describe('资源负荷窗口', () => {
  it('groups resources and preserves active, zero and unknown loads with explicit windows', async () => {
    const loads = [
      {
        resourceId: 'WC-01',
        windowStartUtc: '2026-10-17T00:00:00Z',
        windowEndUtc: '2026-10-17T08:00:00Z',
        assignedMinutes: 120,
        availableMinutes: 480,
        utilization: 0.25,
      },
      {
        resourceId: 'WC-01',
        windowStartUtc: '2026-10-18T00:00:00Z',
        windowEndUtc: '2026-10-18T08:00:00Z',
        assignedMinutes: 0,
        availableMinutes: 480,
        utilization: 0,
      },
      {
        resourceId: 'WC-01',
        windowStartUtc: '2026-10-19T00:00:00Z',
        windowEndUtc: '2026-10-19T08:00:00Z',
        assignedMinutes: 0,
        availableMinutes: 0,
        utilization: 0,
      },
      {
        resourceId: 'WC-02',
        windowStartUtc: '2026-10-17T00:00:00Z',
        windowEndUtc: '2026-10-17T08:00:00Z',
      },
    ]
    const wrapper = mount(SchedulingResourceLoads, { props: { loads } })
    expect(wrapper.findAll('article')).toHaveLength(2)
    const first = wrapper.findAll('article')[0]!
    expect(first.get('h4').text()).toBe('WC-01')
    expect(first.text()).toContain('利用率 25%')
    expect(first.get('summary').text()).toContain('2 个零负荷窗口')
    expect(first.get('details').attributes('open')).toBeUndefined()
    await first.get('summary').trigger('click')
    for (const load of loads) {
      expect(wrapper.text()).toContain(
        `${formatDateTime(load.windowStartUtc)} 至 ${formatDateTime(load.windowEndUtc)}`,
      )
    }
    expect(first.get('details').text()).toContain('已排 0 / 可用 480')
    expect(first.get('details').text()).toContain('已排 0 / 可用 0')
    const second = wrapper.findAll('article')[1]!
    expect(second.text()).toContain('已排 — / 可用 — 分钟 · 利用率 —')
    expect(second.find('details').exists()).toBe(false)
  })

  it('retains an all-zero resource as an expandable group', () => {
    const wrapper = mount(SchedulingResourceLoads, {
      props: {
        loads: [
          { resourceId: 'WC-IDLE', assignedMinutes: 0, availableMinutes: 480, utilization: 0 },
        ],
      },
    })
    expect(wrapper.get('h4').text()).toBe('WC-IDLE')
    expect(wrapper.get('summary').text()).toContain('1 个零负荷窗口')
    expect(wrapper.text()).not.toContain('暂无资源负荷')
  })
})
