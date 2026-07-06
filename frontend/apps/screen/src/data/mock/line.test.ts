import { describe, expect, it } from 'vitest'
import { buildLineBoard, buildLineCards, composeLineState, shiftNow } from './line'

describe('composeLineState（归并规则）', () => {
  it('任一报警 → 红；停机/待机 → 黄；全运行 → 绿；断线不改灯', () => {
    expect(composeLineState([{ state: 'run' }, { state: 'alarm' }])).toBe('alarm')
    expect(composeLineState([{ state: 'run' }, { state: 'down' }])).toBe('attention')
    expect(composeLineState([{ state: 'run' }, { state: 'idle' }])).toBe('attention')
    expect(composeLineState([{ state: 'run' }, { state: 'offline' }])).toBe('run')
    expect(composeLineState([{ state: 'run' }, { state: 'run' }])).toBe('run')
  })
})

describe('shiftNow（真实时钟班次）', () => {
  it('早班/夜班边界与剩余推算', () => {
    const at = (h: number, m = 0) => shiftNow(new Date(2026, 6, 6, h, m))
    expect(at(8).name).toBe('早班')
    expect(at(8).elapsedMin).toBe(0)
    expect(at(19, 59).remainingMin).toBe(1)
    expect(at(20).name).toBe('夜班')
    expect(at(20).elapsedMin).toBe(0)
    expect(at(3).name).toBe('夜班')
    expect(at(3).elapsedMin).toBe(7 * 60)
    for (const s of [at(0), at(9, 30), at(23, 59)]) {
      expect(s.remainingMin).toBeGreaterThan(0)
      expect(s.remainingMin).toBeLessThanOrEqual(720)
      expect(s.elapsedMin + s.remainingMin).toBe(720)
    }
  })
})

describe('buildLineCards（选择器 · 与设备屏同源）', () => {
  it('F01：电芯线红（卷绕机报警同源）、涂装黄、焊装二线失联角标、红线置顶', () => {
    const cards = buildLineCards('F01')
    expect(cards.length).toBe(9)
    const bat = cards.find((c) => c.name === '电芯线')
    expect(bat?.state).toBe('alarm')
    expect(bat?.alert).toContain('卷绕机 1#')
    expect(cards.find((c) => c.name === '涂装线')?.state).toBe('attention')
    expect(cards.find((c) => c.name === '总装一线')?.state).toBe('attention')
    const weld2 = cards.find((c) => c.name === '焊装二线')
    expect(weld2?.offlineDevices).toBeGreaterThanOrEqual(1)
    // 红线置顶
    expect(cards[0].state).toBe('alarm')
    const rank = { alarm: 0, attention: 1, run: 2 }
    const seqRank = cards.map((c) => rank[c.state])
    expect([...seqRank].sort((a, b) => a - b)).toEqual(seqRank)
  })

  it('scope 收窄（workshop-lead）：只见电池车间两条线', () => {
    const cards = buildLineCards('F01', ['WS-BATTERY'])
    expect(cards.map((c) => c.name).sort()).toEqual(['PACK 线', '电芯线'])
  })

  it('卡片信息密度：设备点排与设备数一致、产量/迷你趋势齐备', () => {
    const cards = buildLineCards('F01')
    for (const c of cards) {
      expect(c.deviceDots.length).toBeGreaterThan(0)
      expect(c.output.plan).toBeGreaterThan(0)
      expect(c.output.good).toBeLessThanOrEqual(c.output.plan)
      expect(c.hourly).toHaveLength(12)
    }
    expect(cards.find((c) => c.name === '电芯线')?.deviceDots).toHaveLength(6)
  })
})

describe('buildLineBoard（单线大屏）', () => {
  it('报警线（电芯线）：红灯 + 横幅 + 达成掉 + 节拍落后 + 产量勾稽', () => {
    const b = buildLineBoard('LN-BAT-1')
    expect(b).not.toBeNull()
    expect(b!.state).toBe('alarm')
    expect(b!.banner?.level).toBe('alarm')
    expect(b!.banner?.text).toContain('卷绕机 1#')
    // 产量勾稽：good+scrap+rework = 完工数 = plan×达成率
    const total = b!.output.good + b!.output.scrap + b!.output.rework
    expect(total).toBeLessThanOrEqual(b!.output.plan)
    expect(b!.output.achievement).toBe(Math.round((total / b!.output.plan) * 100))
    // 节拍落后为正 → 红
    expect(b!.takt.deviationPct).toBeGreaterThan(0)
    expect(b!.takt.actualSec).toBeGreaterThan(b!.takt.standardSec)
    expect(b!.hourly).toHaveLength(12)
    // 工序状态机：恰一个 doing，done 全在 doing 之前
    const doing = b!.wo!.steps.filter((s) => s.state === 'doing')
    expect(doing).toHaveLength(1)
    const doingIdx = b!.wo!.steps.findIndex((s) => s.state === 'doing')
    for (const [i, s] of b!.wo!.steps.entries()) {
      if (i < doingIdx) expect(s.state).toBe('done')
      if (i > doingIdx) expect(s.state).toBe('todo')
    }
    expect(b!.wo!.dueInMin).toBeGreaterThan(0)
    expect(b!.shift.remainingMin).toBeGreaterThan(0)
    // FPY 勾稽 = 良品/完工；报警线停机统计 ≥1 次；安灯有响应中记录
    expect(b!.fpy).toBe(Math.round((b!.output.good / total) * 1000) / 10)
    expect(b!.downtime.count).toBeGreaterThanOrEqual(1)
    expect(b!.downtime.totalMin).toBeGreaterThan(0)
    expect(b!.andon.length).toBeGreaterThanOrEqual(1)
    expect(b!.andon[0].state).toBe('响应中')
    // 趋势标签/节拍产能参考
    expect(b!.hourLabels).toHaveLength(12)
    for (const l of b!.hourLabels) expect(l).toMatch(/^\d{2}:00$/)
    expect(b!.planPerHour).toBeGreaterThan(0)
    expect(b!.crew.leader).toBeTruthy()
    // 设备带带首个关键参数（非断线设备）
    expect(b!.devices.some((d) => d.param)).toBe(true)
  })

  it('正常线（冲压一线）：绿灯、无横幅、无安灯记录、无停机（异常是例外）', () => {
    const b = buildLineBoard('LN-STAMP-1')
    expect(b!.state).toBe('run')
    expect(b!.banner).toBeUndefined()
    expect(b!.takt.deviationPct).toBeLessThanOrEqual(6)
    expect(b!.andon).toHaveLength(0)
    expect(b!.downtime.count).toBe(0)
  })

  it('scope 外的线返回 null（越权防护）；未知线 null', () => {
    expect(buildLineBoard('LN-STAMP-1', 'F01', ['WS-BATTERY'])).toBeNull()
    expect(buildLineBoard('LN-NOPE')).toBeNull()
  })
})
