import { describe, expect, it } from 'vitest'
import { tooltipHtml } from './DhtmlxEngine'
import type { ScheduleTask } from '../../model/types'

/**
 * #1399 M6 tooltip 里的语义 chip 必须走 token,不许出现裸色值。
 *
 * 背景:「插单」chip 曾硬编码 `oklch(0.7 0.17 60)`,与 `--nv-scheduling-rush` 的
 * `oklch(0.68 0.18 45)` 不是同一个颜色——同一个语义在图例、工序详情、tooltip 三处
 * 呈现出两种橙,而且裸值进 innerHTML 后暗色主题不跟随。
 *
 * 这类问题在浏览器里极难发现(得 hover 到恰好是插单的那一条),所以按字符串断言。
 */

function task(patch: Partial<ScheduleTask> = {}): ScheduleTask {
  return {
    id: 't1',
    orderId: 'WO-2026-03008',
    operationId: 'OP-10',
    operationSequence: 10,
    type: 'operation',
    text: '下料',
    startUtc: '2026-08-01T00:00:00.000Z',
    endUtc: '2026-08-01T03:00:00.000Z',
    locked: false,
    hasConflict: false,
    ...patch,
  } as ScheduleTask
}

describe('tooltipHtml 颜色事实源', () => {
  it('插单 chip 用 --nv-scheduling-rush,而不是硬编码的橙', () => {
    const html = tooltipHtml(task({ isRush: true }))
    expect(html).toContain('插单')
    expect(html).toContain('var(--nv-scheduling-rush)')
    // 曾经的裸值,不许回来
    expect(html).not.toContain('oklch(0.7 0.17 60)')
  })

  it('所有 chip 的颜色都是 var(--…),tooltip 里不出现任何裸色字面量', () => {
    const html = tooltipHtml(
      task({
        isRush: true,
        locked: true,
        hasConflict: true,
        priority: 'high',
        materialRisk: { message: '缺 2 项' },
        equipmentRisk: { message: '状态未知' },
      } as Partial<ScheduleTask>),
    )
    // color:/background: 后面若直接跟 oklch(/#/rgb( 就是绕过了 token
    const rawColor = /(?:color|background)\s*:\s*(?:oklch\(|#[0-9a-fA-F]{3}|rgba?\()/
    expect(rawColor.test(html)).toBe(false)
  })
})
