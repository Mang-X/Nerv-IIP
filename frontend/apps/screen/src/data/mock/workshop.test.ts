import { describe, expect, it } from 'vitest'
import { DEFAULT_FACTORY_ID } from './masterdata'
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

const ALL_WORKSHOPS = ['WS-01', 'WS-02', 'WS-03']

describe('buildWorkshopBoard（报警车间 · 与产线/设备屏同源）', () => {
  it('WS-01 机加车间：红灯 + 活塞杆一线红卡置顶 + DEV-CNC-03 同源事件 + 交接遗留叙事', () => {
    const b = buildWorkshopBoard('WS-01')
    expect(b).not.toBeNull()
    expect(b!.workshopName).toBe('一车间 · 机加车间')
    expect(b!.managerName).toBe('王建国') // L0 §5 EMP-001
    expect(b!.state).toBe('alarm')
    // 设定集 §2：机加 5 条线；红线置顶（沿 buildLineCards 排序）
    expect(b!.lines).toHaveLength(5)
    expect(new Set(b!.lines.map((l) => l.name))).toEqual(
      new Set(['活塞杆一线', '活塞杆二线', '缸筒一线', '缸筒二线', '精磨线']),
    )
    expect(b!.lines[0].name).toBe('活塞杆一线')
    expect(b!.lines[0].state).toBe('alarm')
    // 事件流与设备屏画像同源：DEV-CNC-03 振动超限 + 维修派工张红梅（REPAIR_POOL 同人）
    const alarmEv = b!.events.find((e) => e.level === 'alarm')
    expect(alarmEv?.text).toContain('DEV-CNC-03')
    expect(alarmEv?.lineName).toBe('活塞杆一线')
    expect(alarmEv?.status).toContain('张红梅')
    expect(b!.downtime.count).toBeGreaterThanOrEqual(1)
    expect(b!.downtime.totalMin).toBeGreaterThan(0)
    // 设备屏 ALARM_POOL 同叙事的预警也在流内
    expect(b!.events.some((e) => e.level === 'warn' && e.text.includes('DEV-GRD-02'))).toBe(true)
    // 班组：设定集 §5 车间级班组（早/中班），交接遗留 → 当班报警，叙事闭环
    expect(['机加车间早班组', '机加车间中班组']).toContain(b!.crew.teamName)
    expect(['刘立新', '陈雪梅']).toContain(b!.crew.leader)
    expect(b!.crew.handoverIssues).toBe(1)
    expect(b!.crew.handoverNote).toContain('DEV-CNC-03')
    // 临期预警引用活塞杆一线当前工单（与产线屏同号）
    const rod = b!.lines.find((l) => l.id === 'LINE-WB-ROD-01')!
    expect(b!.woAlerts.some((w) => w.kind === 'dueSoon' && w.code === rod.currentWo)).toBe(true)
    // NCR 与质量屏严格同一批（单号/缺陷从 buildQualityBoard 过滤本车间，不另编）
    const qCodes = new Set(buildQualityBoard().ncrs.map((r) => r.code))
    expect(b!.quality.ncr.length).toBeGreaterThanOrEqual(2)
    for (const n of b!.quality.ncr) expect(qCodes.has(n.code)).toBe(true)
    expect(
      b!.quality.ncr.some((n) => n.code === 'NCR-2026-0158' && n.text === '活塞杆表面振纹'),
    ).toBe(true)
    // 事件流含已恢复历史（当班全貌），且历史全部沉底、活跃异常在前
    const firstResolved = b!.events.findIndex((e) => e.resolved)
    expect(firstResolved).toBeGreaterThan(0)
    expect(b!.events.slice(firstResolved).every((e) => e.resolved)).toBe(true)
    expect(b!.events.length).toBeGreaterThanOrEqual(4)
    // 已恢复短停计入当班停机（作战室口径 = 当班累计）：报警停摆 + 缸筒一线短停 7min
    expect(b!.downtime.count).toBe(2)
  })

  it('车间效率 OEE：A×P×Q 勾稽、各线对比含报警线垫底、30 天趋势末点与 KPI 勾稽', () => {
    const b = buildWorkshopBoard('WS-01')!
    const { overall, availability, performance, quality, byLine } = b.oee
    expect(overall).toBe(Math.round((availability * performance * quality) / 10000))
    for (const v of [availability, performance, quality]) {
      expect(v).toBeGreaterThan(0)
      expect(v).toBeLessThanOrEqual(100)
    }
    // 各线对比：与产线卡一一对应；报警的活塞杆一线 OEE 低于正常的活塞杆二线
    expect(byLine.map((l) => l.lineId).sort()).toEqual(b.lines.map((l) => l.id).sort())
    const rod1 = byLine.find((l) => l.lineId === 'LINE-WB-ROD-01')!
    const rod2 = byLine.find((l) => l.lineId === 'LINE-WB-ROD-02')!
    expect(rod1.state).toBe('alarm')
    expect(rod1.oee).toBeLessThan(rod2.oee)
    // 近 30 天：三列等长、末点 = 今日截至当前（与 KPI 勾稽）、周日停产（计划 0）
    expect(b.daily30.output).toHaveLength(30)
    expect(b.daily30.plan).toHaveLength(30)
    expect(b.daily30.labels).toHaveLength(30)
    expect(b.daily30.output.at(-1)).toBe(b.output.actual)
    expect(b.daily30.plan.some((p) => p === 0)).toBe(true)
  })

  it('勾稽：车间产量/计划/达成/设备数/失联/状态计数 = Σ 本车间产线卡（数字精确同源）', () => {
    for (const id of ALL_WORKSHOPS) {
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

  it('三车间设备数合计 = 46（设定集 §3），全部经工作中心归到产线', () => {
    const total = ALL_WORKSHOPS.reduce((n, id) => n + buildWorkshopBoard(id)!.devices.total, 0)
    expect(total).toBe(46)
  })

  it('当班累计曲线：三列等长、单调不减、末点 = 当班累计；分线逐点求和 = 总线', () => {
    const b = buildWorkshopBoard('WS-01')!
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
      for (let i = 1; i < bl.data.length; i++)
        expect(bl.data[i]).toBeGreaterThanOrEqual(bl.data[i - 1])
    }
    for (let i = 0; i < c.actual.length; i++) {
      expect(c.byLine.reduce((n, bl) => n + bl.data[i], 0)).toBe(c.actual[i])
    }
  })

  it('齐套：WS-02 前减装配二线弹簧二供切换缺料（与产线屏同源），其余车间 100 全齐', () => {
    const asm = buildWorkshopBoard('WS-02')!
    expect(asm.kitting.rate).toBeLessThan(100)
    expect(asm.kitting.woBlocked).toBeGreaterThanOrEqual(1)
    expect(asm.kitting.shortages.length).toBeGreaterThan(0)
    expect(asm.kitting.shortages.every((s) => s.lineName === '前减装配二线')).toBe(true)
    // 物料编码走 L0 §4 原材料段；需求量与产线屏当前工单 qtyPlan 同式（ceil(plan/100)×100）
    const fa2 = asm.lines.find((l) => l.id === 'LINE-WB-FA-02')!
    for (const s of asm.kitting.shortages) {
      expect(s.material).toBeTruthy()
      expect(s.code).toMatch(/^RM-/)
      expect(s.wo).toBe(fa2.currentWo)
      expect(s.requiredQty).toBe(Math.ceil(fa2.output.plan / 100) * 100)
      expect(s.shortQty).toBeGreaterThan(0)
      expect(s.shortQty).toBeLessThan(s.requiredQty)
    }
    // 超期预警引用该线当前工单（跨屏对得上）
    expect(asm.woAlerts.some((w) => w.kind === 'overdue' && w.code === fa2.currentWo)).toBe(true)
    for (const id of ['WS-01', 'WS-03']) {
      const b = buildWorkshopBoard(id)!
      expect(b.kitting.rate).toBe(100)
      expect(b.kitting.shortages).toHaveLength(0)
    }
  })

  it('WS-03 表面与包装：停机待修进流、维修责任人同源、无交付预警', () => {
    const b = buildWorkshopBoard('WS-03')!
    expect(b.managerName).toBe('张玉兰') // L0 §5 EMP-003
    const down = b.events.find((e) => e.level === 'downtime' && !e.resolved)
    expect(down?.text).toContain('DEV-CTG-02')
    expect(down?.lineName).toBe('电泳涂装线')
    expect(down?.status).toContain('刘秀英')
    expect(b.downtime.count).toBe(1)
    expect(b.woAlerts).toHaveLength(0)
  })

  it('WS-02 装配车间：失联角标计数 ≥1（防假绿）+ 失联/换型进流；已恢复短停计入当班停机', () => {
    const b = buildWorkshopBoard('WS-02')!
    expect(b.offlineDevices).toBeGreaterThanOrEqual(1)
    expect(b.events.some((e) => e.text.includes('数据链路失联'))).toBe(true)
    expect(b.events.some((e) => e.text.includes('换型待机'))).toBe(true)
    expect(b.events.some((e) => e.level === 'warn' && !e.resolved)).toBe(true)
    // 已恢复短停 5min：进流（灰显沉底）且计入当班停机统计（换型 + 短停 = 2 次）
    const resolved = b.events.find((e) => e.resolved && e.level === 'downtime')
    expect(resolved?.text).toContain('DEV-ASM-02')
    expect(b.downtime.count).toBe(2)
  })

  it('班组诚实口径：在册人数按 L0 班组花名册（班组长 1 + 操作工 3–4），技能覆盖 ≤100', () => {
    let zeroHandover = 0
    for (const id of ALL_WORKSHOPS) {
      const b = buildWorkshopBoard(id)!
      // 设定集 §5：全厂 19 名操作工分 6 组，不是「每车间 8–20 人」的整车厂编制
      expect(b.crew.headcountPlanned).toBeGreaterThanOrEqual(4)
      expect(b.crew.headcountPlanned).toBeLessThanOrEqual(5)
      expect(b.crew.skillCoverage).toBeGreaterThan(0)
      expect(b.crew.skillCoverage).toBeLessThanOrEqual(100)
      expect(b.crew.teamName).toContain('班组')
      expect(b.crew.leader).toBeTruthy()
      if (b.crew.handoverIssues === 0) {
        zeroHandover += 1
        expect(b.crew.handoverNote).toBeUndefined()
      }
    }
    expect(zeroHandover).toBeGreaterThanOrEqual(1)
  })

  it('scope 越权/未知车间返回 null；persona 收窄内可见', () => {
    // workshop-lead（只见装配车间）访问机加 → null
    expect(buildWorkshopBoard('WS-01', DEFAULT_FACTORY_ID, ['WS-02'])).toBeNull()
    // 未知车间 → null
    expect(buildWorkshopBoard('WS-NOPE')).toBeNull()
    // 合法：workshop-lead 看自己车间
    expect(buildWorkshopBoard('WS-02', DEFAULT_FACTORY_ID, ['WS-02'])).not.toBeNull()
  })
})
