import { describe, expect, it } from 'vitest'
import { buildLineBoard, buildLineCards, composeLineState, shiftNow } from './line'
import { DEFAULT_FACTORY_ID } from './masterdata'

describe('composeLineState（归并规则）', () => {
  it('任一报警 → 红；停机/待机 → 黄；全运行 → 绿；断线不改灯', () => {
    expect(composeLineState([{ state: 'run' }, { state: 'alarm' }])).toBe('alarm')
    expect(composeLineState([{ state: 'run' }, { state: 'down' }])).toBe('attention')
    expect(composeLineState([{ state: 'run' }, { state: 'idle' }])).toBe('attention')
    expect(composeLineState([{ state: 'run' }, { state: 'offline' }])).toBe('run')
    expect(composeLineState([{ state: 'run' }, { state: 'run' }])).toBe('run')
  })
})

// 设定集 §1 工作制：早班 08:00–16:00、中班 16:00–24:00（各 480 min），
// 00:00–08:00 无排班 —— 宁沪减振没有夜班，凭空造一个当场被现场人员戳穿。
describe('shiftNow（真实时钟 · 设定集双班制）', () => {
  it('早班/中班边界与剩余推算', () => {
    const at = (h: number, m = 0) => shiftNow(new Date(2026, 6, 6, h, m))
    expect(at(8).name).toBe('早班')
    expect(at(8).elapsedMin).toBe(0)
    expect(at(8).range).toBe('08:00–16:00')
    expect(at(15, 59).remainingMin).toBe(1)
    expect(at(16).name).toBe('中班')
    expect(at(16).elapsedMin).toBe(0)
    expect(at(23, 59).remainingMin).toBe(1)
    for (const s of [at(9, 30), at(13), at(18), at(23, 59)]) {
      expect(s.closed).toBe(false)
      expect(s.elapsedMin + s.remainingMin).toBe(480)
    }
  })

  it('00:00–08:00 停产时段：冻结在中班收盘，不伪造夜班', () => {
    const at = (h: number) => shiftNow(new Date(2026, 6, 6, h, 0))
    for (const h of [0, 3, 7]) {
      expect(at(h).closed).toBe(true)
      expect(at(h).name).toContain('中班')
      expect(at(h).elapsedMin).toBe(480)
      expect(at(h).remainingMin).toBe(0)
    }
  })
})

describe('buildLineCards（选择器 · 与设备屏同源）', () => {
  it('14 条产线；活塞杆一线红（DEV-CNC-03 同源）、换型/停机线黄、阀系预装线失联角标、红线置顶', () => {
    const cards = buildLineCards()
    expect(cards).toHaveLength(14) // 设定集 §2：机加 5 + 装配 6 + 表面与包装 3
    const rod1 = cards.find((c) => c.name === '活塞杆一线')
    expect(rod1?.state).toBe('alarm')
    expect(rod1?.alert).toContain('DEV-CNC-03')
    // 前减装配三线换型待机、电泳涂装线停机待修、精磨线计划保养 → 黄
    expect(cards.find((c) => c.name === '前减装配三线')?.state).toBe('attention')
    expect(cards.find((c) => c.name === '电泳涂装线')?.state).toBe('attention')
    expect(cards.find((c) => c.name === '精磨线')?.state).toBe('attention')
    // 断线不改灯，走失联角标（DEV-AUX-06 挂装配车间辅助动力 → 阀系预装线）
    const valve = cards.find((c) => c.name === '阀系预装线')
    expect(valve?.state).toBe('run')
    expect(valve?.offlineDevices).toBe(1)
    // 异常是例外：14 条线里绿灯仍是多数
    expect(cards.filter((c) => c.state === 'run').length).toBeGreaterThanOrEqual(9)
    // 红线置顶
    expect(cards[0].state).toBe('alarm')
    const rank = { alarm: 0, attention: 1, run: 2 }
    const seqRank = cards.map((c) => rank[c.state])
    expect([...seqRank].sort((a, b) => a - b)).toEqual(seqRank)
  })

  it('工单号走设定集 §9 的 WO-2026-##### 段，跨屏可对', () => {
    const cards = buildLineCards()
    for (const c of cards) expect(c.currentWo).toMatch(/^WO-2026-\d{5}$/)
    expect(cards.find((c) => c.name === '活塞杆一线')?.currentWo).toBe('WO-2026-03421')
  })

  it('scope 收窄（workshop-lead）：只见装配车间六条线', () => {
    const cards = buildLineCards(DEFAULT_FACTORY_ID, ['WS-02'])
    expect(new Set(cards.map((c) => c.name))).toEqual(
      new Set([
        '前减装配一线',
        '前减装配二线',
        '前减装配三线',
        '后减装配一线',
        '后减装配二线',
        '阀系预装线',
      ]),
    )
  })

  it('卡片信息密度：设备点排与设备数一致、产量/迷你趋势齐备', () => {
    const cards = buildLineCards()
    for (const c of cards) {
      expect(c.deviceDots.length).toBeGreaterThan(0)
      expect(c.output.plan).toBeGreaterThan(0)
      expect(c.output.good).toBeLessThanOrEqual(c.output.plan)
      expect(c.hourly).toHaveLength(12)
    }
    // 活塞杆一线 = DEV-CNC-01/02/03（WC-ROD-01）
    expect(cards.find((c) => c.name === '活塞杆一线')?.deviceDots).toHaveLength(3)
  })

  it('视野内 seam：视野外产线不生成趋势序列（hourly 空），状态/产量/排序仍全量', () => {
    const all = buildLineCards()
    const visible = all.slice(0, 3).map((c) => c.id)
    const narrowed = buildLineCards(DEFAULT_FACTORY_ID, 'all', visible)
    expect(narrowed.map((c) => c.id)).toEqual(all.map((c) => c.id))
    for (const c of narrowed) {
      expect(c.output.plan).toBeGreaterThan(0)
      expect(c.deviceDots.length).toBeGreaterThan(0)
      expect(c.hourly).toHaveLength(visible.includes(c.id) ? 12 : 0)
    }
  })
})

describe('buildLineBoard（单线大屏）', () => {
  it('报警线（活塞杆一线）：红灯 + 横幅 + 达成掉 + 节拍落后 + 产量勾稽', () => {
    const b = buildLineBoard('LINE-WB-ROD-01')
    expect(b).not.toBeNull()
    expect(b!.state).toBe('alarm')
    expect(b!.banner?.level).toBe('alarm')
    expect(b!.banner?.text).toContain('DEV-CNC-03')
    // 产量勾稽：good+scrap+rework = 完工数 = plan×达成率
    const total = b!.output.good + b!.output.scrap + b!.output.rework
    expect(total).toBeLessThanOrEqual(b!.output.plan)
    // 完工数由 round(plan × 达成率) 得来，反算回达成率允许 ±1 的取整误差
    expect(
      Math.abs(b!.output.achievement - Math.round((total / b!.output.plan) * 100)),
    ).toBeLessThanOrEqual(1)
    // 节拍落后为正 → 红；标准节拍取 world.ts 节拍表（活塞杆线 36 s/件）
    expect(b!.takt.standardSec).toBe(36)
    expect(b!.takt.deviationPct).toBeGreaterThan(0)
    expect(b!.takt.actualSec).toBeGreaterThan(b!.takt.standardSec)
    expect(b!.hourly).toHaveLength(12)
    // 工序流：累计完成沿流向单调递减、末道 = 工单完成数、WIP = 首末差
    const st = b!.wo!.stations
    expect(st.length).toBeGreaterThanOrEqual(3)
    for (let i = 1; i < st.length; i++) expect(st[i - 1].done).toBeGreaterThanOrEqual(st[i].done)
    expect(st[st.length - 1].done).toBe(b!.wo!.qtyDone)
    expect(b!.wo!.wip).toBe(st[0].done - st[st.length - 1].done)
    expect(st.find((s) => s.state === 'blocked')?.name).toBe('CNC 精车')
    expect(b!.wo!.product).toBe('活塞杆 φ22×420')
    expect(b!.wo!.code).toBe('WO-2026-03421')
    expect(b!.wo!.dueInMin).toBeGreaterThan(0)
    // FPY 勾稽 = 良品/完工；报警线停机统计 ≥1 次；安灯有响应中记录
    expect(b!.fpy).toBe(Math.round((b!.output.good / total) * 1000) / 10)
    expect(b!.downtime.count).toBeGreaterThanOrEqual(1)
    expect(b!.downtime.totalMin).toBeGreaterThan(0)
    expect(b!.andon).toHaveLength(1)
    expect(b!.andon[0].state).toBe('响应中')
    // 趋势标签/节拍产能参考
    expect(b!.hourLabels).toHaveLength(12)
    for (const l of b!.hourLabels) expect(l).toMatch(/^\d{2}:00$/)
    expect(b!.planPerHour).toBe(100)
    // 近 30 天：三列等长、周日停产（计划为 0）—— 设定集 §1 标准日历
    expect(b!.daily30.output).toHaveLength(30)
    expect(b!.daily30.plan).toHaveLength(30)
    expect(b!.daily30.labels).toHaveLength(30)
    expect(b!.daily30.plan.some((p) => p === 0)).toBe(true)
    // 班组长取该车间当班班组（设定集 §5：班组是车间级，没有「线长」岗位）
    expect(['刘立新', '陈雪梅']).toContain(b!.crew.leader)
    // 设备带带首个关键参数（非断线设备）+ 折叠详情参数带趋势
    expect(b!.devices.some((d) => d.param)).toBe(true)
    for (const d of b!.devices) {
      expect(d.params.length).toBeGreaterThanOrEqual(1)
      if (d.state !== 'offline') expect(d.params.every((p) => p.spark.length === 12)).toBe(true)
    }
    // 产线 OEE 勾稽（班内推算：A×P×Q）与范围
    const { overall, availability, performance, quality } = b!.oee
    expect(overall).toBe(Math.round((availability * performance * quality) / 10000))
    for (const v of [availability, performance, quality]) {
      expect(v).toBeGreaterThanOrEqual(0)
      expect(v).toBeLessThanOrEqual(100)
    }
    // 24h OEE 热力：24 格、报警线近时段低谷
    expect(b!.hourlyOee).toHaveLength(24)
    for (const v of b!.hourlyOee) {
      expect(v).toBeGreaterThanOrEqual(0)
      expect(v).toBeLessThanOrEqual(100)
    }
    expect(b!.hourlyOee[23]).toBeLessThan(60)
  })

  it('正常线（活塞杆二线）：绿灯、无横幅、无安灯记录、无停机（异常是例外）', () => {
    const b = buildLineBoard('LINE-WB-ROD-02')
    expect(b!.state).toBe('run')
    expect(b!.banner).toBeUndefined()
    expect(b!.takt.deviationPct).toBeLessThanOrEqual(6)
    expect(b!.andon).toHaveLength(0)
    expect(b!.downtime.count).toBe(0)
    expect(b!.oee.availability).toBe(100)
    expect(b!.hourlyOee[23]).toBeGreaterThanOrEqual(60)
    expect(b!.wo!.stations.every((s) => s.state === 'run')).toBe(true)
    expect(b!.wo!.kitting).toBe('ok')
  })

  it('前减装配二线：弹簧二供切换期缺料 → kitting short（与车间屏同口径）', () => {
    expect(buildLineBoard('LINE-WB-FA-02')!.wo!.kitting).toBe('short')
  })

  it('scope 外的线返回 null（越权防护）；未知线 null', () => {
    expect(buildLineBoard('LINE-WB-ROD-01', DEFAULT_FACTORY_ID, ['WS-02'])).toBeNull()
    expect(buildLineBoard('LINE-NOPE')).toBeNull()
  })
})
