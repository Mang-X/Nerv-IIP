// scope 一致性守卫测试（MAN-467 review）：切换 scope 时旧 tick 不覆盖新 board。
import { describe, expect, it } from 'vitest'
import { scopedOverride } from './scope'

describe('scopedOverride', () => {
  it('scope 一致 → 用 ops 覆盖（返回其 data）', () => {
    const ops = { scopeKey: 'F01::planner', data: { pick: 3 } }
    const board = { scopeKey: 'F01::planner', data: { pick: 1 } }
    expect(scopedOverride(ops, board)).toEqual({ pick: 3 })
  })

  it('scope 不一致（切换瞬间旧 ops）→ undefined，页面回退 board', () => {
    const oldOps = { scopeKey: 'F01::planner', data: { pick: 3 } }
    const newBoard = { scopeKey: 'F02::planner', data: { pick: 9 } }
    expect(scopedOverride(oldOps, newBoard)).toBeUndefined()
  })

  it('persona 切换也纳入 scope key', () => {
    const ops = { scopeKey: 'F01::planner', data: { pick: 3 } }
    const board = { scopeKey: 'F01::supervisor', data: { pick: 1 } }
    expect(scopedOverride(ops, board)).toBeUndefined()
  })

  it('任一为空 → undefined（首刷未就绪）', () => {
    const scoped = { scopeKey: 'F01::planner', data: { pick: 3 } }
    expect(scopedOverride(undefined, scoped)).toBeUndefined()
    expect(scopedOverride(scoped, undefined)).toBeUndefined()
  })
})
