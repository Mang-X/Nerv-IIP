import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import ShiftWorkingTime from './ShiftWorkingTime.vue'

describe('班次工作时段', () => {
  it('读取详情并显示跨日和实际计薪分钟数', async () => {
    const fetchDetail = vi.fn().mockResolvedValue({
      startsAt: '22:00:00',
      endsAt: '06:00:00',
      paidMinutes: 450,
    })
    const wrapper = mount(ShiftWorkingTime, { props: { code: 'NIGHT', fetchDetail } })
    expect(wrapper.get('[role="status"]').text()).toBe('正在读取工作时段…')
    await flushPromises()
    expect(fetchDetail).toHaveBeenCalledWith('NIGHT')
    expect(wrapper.text()).toContain('22:00 – 次日 06:00')
    expect(wrapper.text()).toContain('计薪 450 分钟')
  })

  it('区分未配置、读取失败和零计薪', async () => {
    const fetchDetail = vi
      .fn()
      .mockResolvedValueOnce({})
      .mockResolvedValueOnce(undefined)
      .mockResolvedValueOnce({ startsAt: '10:00:00', endsAt: '10:30:00', paidMinutes: 0 })
    const wrapper = mount(ShiftWorkingTime, { props: { code: 'DAY', fetchDetail } })
    await flushPromises()
    expect(wrapper.text()).toContain('未配置工作时段。')
    await wrapper.setProps({ code: 'MISSING' })
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toContain('工作时段读取失败')
    await wrapper.setProps({ code: 'BREAK' })
    await flushPromises()
    expect(wrapper.text()).toContain('不计薪')
  })
})
