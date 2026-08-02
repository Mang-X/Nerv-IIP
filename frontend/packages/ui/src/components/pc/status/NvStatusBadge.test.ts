import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import NvStatusBadge from './NvStatusBadge.vue'

/**
 * 漏词告警的触发边界。
 *
 * 告警要拦的是「裸码值上了屏」。调用方自己传了 `label` 时，屏上是它的词、
 * 不是词表的回吐值——漏词没有可见后果，照报只会把开发期频道刷成噪声。
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

  it('没传 label 时，词表回吐值会上屏——漏词必须报', () => {
    expect(warnsFor({ value: '__漏词甲__' })).toBe(true)
  })

  it('传了 label 时，词表结果不上屏——不该报', () => {
    expect(warnsFor({ value: '__漏词乙__', label: '通过' })).toBe(false)
  })
})
