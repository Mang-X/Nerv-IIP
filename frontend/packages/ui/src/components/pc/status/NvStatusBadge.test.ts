import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import NvStatusBadge from './NvStatusBadge.vue'

/**
 * 漏词告警的触发边界。
 *
 * 告警要发现的是「已知状态缺少可读标签」。调用方自己传了 `label` 时，屏上是它的词、
 * 不是词表的占位值——漏词没有可见后果，照报只会把开发期频道刷成噪声。
 * 实测踩到：审批决策记录明明传了 `:label="通过"`，控制台仍在报
 * 「词表缺失: approve」，把真正的漏词（履约时间线的「高风险」）淹在里面。
 */
describe('NvStatusBadge 漏词告警边界', () => {
  // warnMissingStatusLabel 用模块级 Set 按归一键去重（避免表格逐行刷屏），
  // 所以**两条用例必须用不同码值**——否则第二条被去重吞掉，无论修没修都"通过"。
  // 这个坑当场踩到：变异验证时把修复改回去，两条依然全绿。
  function warnsFor(props: Record<string, unknown>) {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    mount(NvStatusBadge, { props })
    const hit = warn.mock.calls.flat().join(' ').includes('词表缺失')
    warn.mockRestore()
    return hit
  }

  it('没传 label 时，词表缺失会让状态不可读——漏词必须报', () => {
    expect(warnsFor({ value: '__漏词甲__' })).toBe(true)
  })

  it('传了 label 时，词表结果不上屏——不该报', () => {
    expect(warnsFor({ value: '__漏词乙__', label: '通过' })).toBe(false)
  })
})

describe('NvStatusBadge 中文状态展示', () => {
  it.each([
    ['Registered', '已登记'],
    ['Matched', '已匹配'],
    ['Executed', '已执行'],
    ['Recorded', '已记录'],
    ['Converted', '已转换'],
    ['Authorized', '已授权'],
    ['WarehouseReceived', '仓库已收货'],
    ['CreditApproved', '贷项已批准'],
    ['CreditIssued', '贷项已开具'],
    ['CreditDenied', '贷项已拒绝'],
    ['PaymentHeld', '付款已冻结'],
    ['Voided', '已作废'],
    ['Applied', '已应用'],
    ['WaitingForParts', '待备件'],
    ['Verified', '已验证'],
    ['PendingApproval', '待审批'],
    ['returned', '已退回'],
    ['skipped', '已跳过'],
    ['withdrawn', '已撤回'],
    ['revoked', '已吊销'],
    ['split', '已拆分'],
    ['merged', '已合并'],
  ])('%s 显示已知中文状态 %s', (value, label) => {
    const wrapper = mount(NvStatusBadge, { props: { value } })
    expect(wrapper.text()).toBe(label)
    expect(wrapper.attributes('aria-label')).toBe(`状态：${label}`)
    wrapper.unmount()
  })

  it.each([null, '', 'new-producer-status'])('未知状态 %s 显示中性占位', (value) => {
    const wrapper = mount(NvStatusBadge, { props: { value } })
    expect(wrapper.text()).toBe('—')
    expect(wrapper.findComponent({ name: 'NvStatusDot' }).props('tone')).toBe('neutral')
    wrapper.unmount()
  })

  it('未知码仍使用调用方显式 label 与 tone', () => {
    const wrapper = mount(NvStatusBadge, {
      props: { value: 'local-status', label: '等待复核', tone: 'warning' },
    })
    expect(wrapper.text()).toBe('等待复核')
    expect(wrapper.findComponent({ name: 'NvStatusDot' }).props('tone')).toBe('warning')
    wrapper.unmount()
  })
})
