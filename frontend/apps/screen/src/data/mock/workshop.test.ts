import { describe, expect, it } from 'vitest'
import { buildQualityBoard } from './quality'
import { buildWorkshopBoard, composeWorkshopState } from './workshop'

describe('composeWorkshopState（车间态归并）', () => {
  it('任一线红 → 红；任一线黄 → 黄；全绿 → 绿', () => {
    expect(composeWorkshopState([{ state: 'run' }, { state: 'alarm' }])).toBe('alarm')
    expect(composeWorkshopState([{ state: 'attention' }, { state: 'alarm' }])).toBe('alarm')
    expect(composeWorkshopState([{ state: 'run' }, { state: 'attention' }])).toBe('attention')
    expect(composeWorkshopState([{ state: 'run' }, { state: 'run' }])).toBe('run')
  })
})

describe('buildWorkshopBoard（报警车间 · 与产线/设备屏同源）', () => {
  it('WS-BATTERY：红灯 + 电芯线红卡置顶 + 卷绕机 1# 同源事件 + 交接遗留叙事', () => {
    const b = buildWorkshopBoard('WS-BATTERY')
    expect(b).not.toBeNull()
    expect(b!.workshopName).toBe('电池车间')
    expect(b!.managerName).toBe('孙立军')
    expect(b!.state).toBe('alarm')
    // 五条线（M2 扩容：电芯×2 + 模组 + PACK×2，多线滚动场景）；红线置顶（沿 buildLineCards 排序）
    expect(b!.lines).toHaveLength(5)
    expect(new Set(b!.lines.map((l) => l.name))).toEqual(
      new Set(['电芯线', '电芯二线', '模组线', 'PACK 线', 'PACK 二线']),
    )
    expect(b!.lines[0].name).toBe('电芯线')
    expect(b!.lines[0].state).toBe('alarm')
    // 事件流与设备屏画像同源：卷绕机 1# 急停 + 维修派工张建国（REPAIR_POOL 同人）
    const alarmEv = b!.events.find((e) => e.level === 'alarm')
    expect(alarmEv?.text).toContain('卷绕机 1#')
    expect(alarmEv?.lineName).toBe('电芯线')
    expect(alarmEv?.status).toContain('张建国')
    // 对应停机统计 ≥1 次；注液机预警（设备屏 ALARM_POOL 同叙事）也在流内
    expect(b!.downtime.count).toBeGreaterThanOrEqual(1)
    expect(b!.downtime.totalMin).toBeGreaterThan(0)
    expect(b!.events.some((e) => e.level === 'warn' && e.text.includes('注液机'))).toBe(true)
    // 班组：电池一班 + 夜班交接遗留（卷绕机异响 → 当班急停，叙事闭环）
    expect(b!.crew.teamName).toBe('电池一班')
    expect(b!.crew.leader).toBeTruthy()
    expect(b!.crew.handoverIssues).toBe(1)
    expect(b!.crew.handoverNote).toContain('卷绕机')
    // 临期预警引用电芯线当前工单（与产线屏同号）
    const bat = b!.lines.find((l) => l.id === 'LN-BAT-1')!
    expect(b!.woAlerts.some((w) => w.kind === 'dueSoon' && w.code === bat.currentWo)).toBe(true)
    // NCR 与质量屏严格同一批（单号/缺陷从 buildQualityBoard 过滤本车间，不另编）
    const qCodes = new Set(buildQualityBoard('F01').ncrs.map((r) => r.code))
    expect(b!.quality.ncr.length).toBeGreaterThanOrEqual(2)
    for (const n of b!.quality.ncr) expect(qCodes.has(n.code)).toBe(true)
    expect(b!.quality.ncr.some((n) => n.code === 'NCR-26-041' && n.text === '极片对齐度超差')).toBe(true)
    // 事件流含已恢复历史（当班全貌），且历史全部沉底、活跃异常在前
    const firstResolved = b!.events.findIndex((e) => e.resolved)
    expect(firstResolved).toBeGreaterThan(0)
    expect(b!.events.slice(firstResolved).every((e) => e.resolved)).toBe(true)
    expect(b!.events.length).toBeGreaterThanOrEqual(4)
    // 已恢复短停计入当班停机（作战室口径 = 当班累计）：急停 + PACK 短停 8min
    expect(b!.downtime.count).toBe(2)
  })

  it('车间效率 OEE：A×P×Q 勾稽、各线对比含报警线垫底、30 天趋势末点与 KPI 勾稽', () => {
    const b = buildWorkshopBoard('WS-BATTERY')!
    const { overall, availability, performance, quality, byLine } = b.oee
    expect(overall).toBe(Math.round((availability * performance * quality) / 10000))
    for (const v of [availability, performance, quality]) {
      expect(v).toBeGreaterThan(0)
      expect(v).toBeLessThanOrEqual(100)
    }
    // 各线对比：与产线卡一一对应；报警的电芯线 OEE 低于正常的 PACK 线
    expect(byLine.map((l) => l.lineId).sort()).toEqual(b.lines.map((l) => l.id).sort())
    const batOee = byLine.find((l) => l.lineId === 'LN-BAT-1')!
    const packOee = byLine.find((l) => l.lineId === 'LN-BAT-2')!
    expect(batOee.state).toBe('alarm')
    expect(batOee.oee).toBeLessThan(packOee.oee)
    // 近 30 天：三列等长、末点 = 今日截至当前（与 KPI 勾稽）、周日排产低谷
    expect(b.daily30.output).toHaveLength(30)
    expect(b.daily30.plan).toHaveLength(30)
    expect(b.daily30.labels).toHaveLength(30)
    expect(b.daily30.output.at(-1)).toBe(b.output.actual)
    const peakPlan = Math.max(...b.daily30.plan)
    expect(b.daily30.plan.some((p) => p < peakPlan * 0.5)).toBe(true)
  })

  it('勾稽：车间产量/计划/达成/设备数/失联/状态计数 = Σ 本车间产线卡（数字精确同源）', () => {
    for (const id of ['WS-BATTERY', 'WS-STAMP', 'WS-WELD', 'WS-PAINT', 'WS-ASSY']) {
      const b = buildWorkshopBoard(id)!
      expect(b).not.toBeNull()
      expect(b.output.actual).toBe(b.lines.reduce((n, l) => n + l.output.good, 0))
      expect(b.output.plan).toBe(b.lines.reduce((n, l) => n + l.output.plan, 0))
      expect(b.output.achievement).toBe(Math.round((b.output.actual / b.output.plan) * 100))
      expect(b.devices.total).toBe(b.lines.reduce((n, l) => n + l.deviceDots.length, 0))
      expect(b.offlineDevices).toBe(b.lines.reduce((n, l) => n + l.offlineDevices, 0))
      expect(b.lineStates.alarm).toBe(b.lines.filter((l) => l.state === 'alarm').length)
      expect(b.lineStates.attention).toBe(b.lines.filter((l) => l.state === 'attention').length)
      expect(b.lineStates.run).toBe(b.lines.filter((l) => l.state === 'run').length)
      // 质量勾稽：FPY = 良品 / 完工（完工 = 良品+报废+返修）
      const doneQty = b.output.actual + b.quality.scrap + b.quality.rework
      expect(b.quality.fpy).toBe(Math.round((b.output.actual / doneQty) * 1000) / 10)
    }
  })

  it('当班累计曲线：三列等长、单调不减、末点 = 当班累计（与 KPI 大数字勾稽）；分线逐点求和 = 总线', () => {
    const b = buildWorkshopBoard('WS-BATTERY')!
    const c = b.shiftCurve
    expect(c.labels.length).toBe(c.actual.length)
    expect(c.labels.length).toBe(c.plan.length)
    expect(c.actual.length).toBeGreaterThanOrEqual(2)
    expect(c.actual[0]).toBe(0)
    expect(c.plan[0]).toBe(0)
    for (let i = 1; i < c.actual.length; i++) {
      expect(c.actual[i]).toBeGreaterThanOrEqual(c.actual[i - 1])
      expect(c.plan[i]).toBeGreaterThanOrEqual(c.plan[i - 1])
    }
    expect(c.actual.at(-1)).toBe(b.output.actual)
    expect(c.plan.at(-1)).toBe(b.output.plan)
    for (const l of c.labels) expect(l).toMatch(/^\d{2}:\d{2}$/)
    // 分线累计：与产线卡一一对应、各线末点 = 线良品、总曲线 = Σ 各线逐点（构造性勾稽）
    expect(c.byLine.map((l) => l.lineId).sort()).toEqual(b.lines.map((l) => l.id).sort())
    for (const bl of c.byLine) {
      expect(bl.data).toHaveLength(c.actual.length)
      const line = b.lines.find((l) => l.id === bl.lineId)!
      expect(bl.data.at(-1)).toBe(line.output.good)
      for (let i = 1; i < bl.data.length; i++) expect(bl.data[i]).toBeGreaterThanOrEqual(bl.data[i - 1])
    }
    for (let i = 0; i < c.actual.length; i++) {
      expect(c.byLine.reduce((n, bl) => n + bl.data[i], 0)).toBe(c.actual[i])
    }
  })

  it('齐套：WS-ASSY 缺料（总装二线 · 与产线屏 kitting 同源，需求量同当前工单计划数），其余车间 100 全齐', () => {
    const assy = buildWorkshopBoard('WS-ASSY')!
    expect(assy.kitting.rate).toBeLessThan(100)
    expect(assy.kitting.woBlocked).toBeGreaterThanOrEqual(1)
    expect(assy.kitting.shortages.length).toBeGreaterThan(0)
    expect(assy.kitting.shortages.every((s) => s.lineName === '总装二线')).toBe(true)
    // 具体物料名 + MAT 编码 + 需求量与产线屏当前工单 qtyPlan 同式（ceil(plan/100)×100）
    const l2 = assy.lines.find((l) => l.id === 'LN-ASSY-2')!
    for (const s of assy.kitting.shortages) {
      expect(s.material).toBeTruthy()
      expect(s.code).toMatch(/^MAT-/)
      expect(s.wo).toBe(l2.currentWo)
      expect(s.requiredQty).toBe(Math.ceil(l2.output.plan / 100) * 100)
      expect(s.shortQty).toBeGreaterThan(0)
      expect(s.shortQty).toBeLessThan(s.requiredQty)
    }
    // 超期预警（WS-ASSY 超期风险叙事，196x 段编号）
    expect(assy.woAlerts.some((w) => w.kind === 'overdue' && w.code === 'WO-1961')).toBe(true)
    for (const id of ['WS-STAMP', 'WS-WELD', 'WS-PAINT', 'WS-BATTERY']) {
      const b = buildWorkshopBoard(id)!
      expect(b.kitting.rate).toBe(100)
      expect(b.kitting.shortages).toHaveLength(0)
    }
  })

  it('正常车间（WS-STAMP）：无红线、事件流为空（空态=健康，不为填屏造历史）、无停机/交付预警', () => {
    const b = buildWorkshopBoard('WS-STAMP')!
    expect(b.state).not.toBe('alarm')
    expect(b.lines.every((l) => l.state !== 'alarm')).toBe(true)
    expect(b.lineStates.alarm).toBe(0)
    // 冲压仅有一台计划保养待机（计划内，不进异常流/停机统计）→ 健康空态
    expect(b.events).toHaveLength(0)
    expect(b.downtime.count).toBe(0)
    expect(b.downtime.totalMin).toBe(0)
    // 质量屏有两条冲压 NCR（待验证尾声 + 新开待评审）—— 跨屏同一事实，不因空态美学隐藏
    expect(b.quality.ncr.map((n) => n.code).sort()).toEqual(['NCR-26-045', 'NCR-26-053'])
    expect(b.woAlerts).toHaveLength(0)
    // 健康车间 OEE 高位
    expect(b.oee.overall).toBeGreaterThanOrEqual(85)
  })

  it('WS-WELD：失联角标计数 ≥1（防假绿）+ 失联/预警进流；已恢复短停计入当班停机', () => {
    const b = buildWorkshopBoard('WS-WELD')!
    expect(b.offlineDevices).toBeGreaterThanOrEqual(1)
    expect(b.events.some((e) => e.text.includes('数据链路失联'))).toBe(true)
    expect(b.events.some((e) => e.level === 'warn' && !e.resolved)).toBe(true)
    // 输送滚床已恢复短停 5min：进流（灰显沉底）且计入当班停机统计
    const resolved = b.events.find((e) => e.resolved && e.level === 'downtime')
    expect(resolved?.text).toContain('输送滚床')
    expect(b.downtime.count).toBe(1)
    expect(b.downtime.totalMin).toBe(5)
  })

  it('班组诚实口径：应到 8–20 人、技能覆盖 ≤100、大多数车间交接遗留 0', () => {
    const ids = ['WS-STAMP', 'WS-WELD', 'WS-PAINT', 'WS-ASSY', 'WS-BATTERY']
    let zeroHandover = 0
    for (const id of ids) {
      const b = buildWorkshopBoard(id)!
      expect(b.crew.headcountPlanned).toBeGreaterThanOrEqual(8)
      expect(b.crew.headcountPlanned).toBeLessThanOrEqual(20)
      expect(b.crew.skillCoverage).toBeGreaterThan(0)
      expect(b.crew.skillCoverage).toBeLessThanOrEqual(100)
      expect(b.crew.teamName).toBeTruthy()
      expect(b.crew.leader).toBeTruthy()
      if (b.crew.handoverIssues === 0) {
        zeroHandover += 1
        expect(b.crew.handoverNote).toBeUndefined()
      }
    }
    expect(zeroHandover).toBeGreaterThanOrEqual(3)
  })

  it('scope 越权/未知车间返回 null；persona 收窄内可见', () => {
    // workshop-lead（只见电池车间）访问冲压 → null
    expect(buildWorkshopBoard('WS-STAMP', 'F01', ['WS-BATTERY'])).toBeNull()
    // 未知车间 / 跨工厂车间 → null
    expect(buildWorkshopBoard('WS-NOPE')).toBeNull()
    expect(buildWorkshopBoard('WS-INJECT', 'F01')).toBeNull()
    // 合法：F02 注塑车间、workshop-lead 看自己车间
    expect(buildWorkshopBoard('WS-INJECT', 'F02')).not.toBeNull()
    expect(buildWorkshopBoard('WS-BATTERY', 'F01', ['WS-BATTERY'])).not.toBeNull()
  })
})
