import { describe, expect, it } from 'vitest'
import { REPAIR_STAGES } from '@/data/contracts/equipment'
import {
  buildDeviceDetail,
  buildEquipmentOverview,
  buildParamsTick,
  chunkIds,
  DEVICE_BATCH_LIMIT,
} from './equipment'
import { DEFAULT_FACTORY_ID, devicesByWorkshop, workshopsByFactory } from './masterdata'

const ROUNDS = 15

describe('chunkIds（deviceAssetIds ≤ 50/批 约束）', () => {
  it('每批不超过上限，合并后与原序一致', () => {
    const ids = Array.from({ length: 137 }, (_, i) => `DEV-${i}`)
    const batches = chunkIds(ids)
    for (const b of batches) expect(b.length).toBeLessThanOrEqual(DEVICE_BATCH_LIMIT)
    expect(batches.flat()).toEqual(ids)
  })
})

describe('buildEquipmentOverview', () => {
  it('设备数对账、五态互斥计数、断线防假绿、报警→维修工单闭环', () => {
    const expectDevices = workshopsByFactory(DEFAULT_FACTORY_ID).reduce(
      (n, w) => n + devicesByWorkshop(w.id).length,
      0,
    )
    expect(expectDevices).toBe(46) // 设定集 §3
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildEquipmentOverview()
      expect(s.devices).toHaveLength(expectDevices)
      // 五态计数 = 墙体逐台归并，和恒等于总数
      const sum = s.counts.run + s.counts.idle + s.counts.down + s.counts.alarm + s.counts.offline
      expect(sum).toBe(expectDevices)
      for (const st of ['run', 'idle', 'down', 'alarm', 'offline'] as const) {
        expect(s.counts[st]).toBe(s.devices.filter((d) => d.state === st).length)
      }
      // 断线防假绿：sourceFresh=false 的设备必须是 offline，绝不能算 run
      for (const d of s.devices.filter((x) => !x.sourceFresh)) expect(d.state).toBe('offline')
      expect(s.counts.offline).toBeGreaterThan(0)
      // 报警行都已触发维修工单（设定集 §9 的 MWO-2026-#### 段）
      for (const a of s.alarms) expect(a.wo).toMatch(/^MWO-2026-\d{4}$/)
      // 维修单按状态机 + 时间衡量（非百分比）
      for (const r of s.repairs) {
        expect(REPAIR_STAGES).toContain(r.stage)
        expect(r.elapsedMin).toBeGreaterThan(0)
        expect(r.reportedAt).toMatch(/^\d{2}:\d{2}$/)
        expect(r.assignee).toBeTruthy()
        expect(r.wo).toMatch(/^MWO-2026-\d{4}$/)
      }
      expect(s.repairs.some((r) => r.overdue)).toBe(true)
      expect(s.repairs.some((r) => r.awaitingConfirm)).toBe(true)
      expect(s.repairs.some((r) => r.blockedBy)).toBe(true)
      // 可靠性：样本充足，MTBF/MTTR 有值；可用率 = 在运占比（真实计数推导，不是拍的）
      expect(s.reliability.mtbfHours).not.toBeNull()
      expect(s.reliability.availability).toBe(Math.round((s.counts.run / expectDevices) * 100))
    }
  })

  it('设备显示名带编码：同型号多台必须可区分（DEV-CNC-01/02/03 都是 CK6150）', () => {
    const s = buildEquipmentOverview()
    const cnc = s.devices.filter((d) => d.code.startsWith('DEV-CNC-'))
    expect(cnc).toHaveLength(10)
    expect(new Set(cnc.map((d) => d.name)).size).toBe(10)
    expect(s.devices.find((d) => d.code === 'DEV-CNC-03')!.name).toBe('DEV-CNC-03 数控车床 CK6150')
  })

  it('小样本保护：无设备样本 MTBF/MTTR 为 null（诚实缺口，不硬算）；样本足有值', () => {
    const empty = buildEquipmentOverview(DEFAULT_FACTORY_ID, ['WS-NONE'])
    expect(empty.devices.length).toBe(0)
    expect(empty.reliability.mtbfHours).toBeNull()
    expect(empty.reliability.mttrMinutes).toBeNull()
    const surface = buildEquipmentOverview(DEFAULT_FACTORY_ID, ['WS-03'])
    expect(surface.devices.length).toBeGreaterThanOrEqual(6)
    expect(surface.reliability.mtbfHours).not.toBeNull()
  })

  it('scope 收窄：设备/报警/维修/保养/点检各档案同步收窄', () => {
    const s = buildEquipmentOverview(DEFAULT_FACTORY_ID, ['WS-02'])
    expect(s.devices).toHaveLength(devicesByWorkshop('WS-02').length)
    const names = new Set(s.devices.map((d) => d.name))
    const asmLines = [
      '前减装配一线',
      '前减装配二线',
      '前减装配三线',
      '后减装配一线',
      '后减装配二线',
      '阀系预装线',
    ]
    for (const d of s.devices) expect(asmLines).toContain(d.lineName)
    for (const a of s.alarms) expect(asmLines).toContain(a.line)
    for (const r of s.repairs) expect(names.has(r.device)).toBe(true)
    for (const t of s.pmTasks) expect(names.has(t.device)).toBe(true)
    for (const i of s.inspections) expect(names.has(i.device)).toBe(true)
  })

  it('档案量符合「正常工厂日」画像：异常少量、台账留档', () => {
    const s = buildEquipmentOverview()
    // 异常是例外：未恢复报警/进行中维修各只有少量
    expect(s.alarms.length).toBeGreaterThanOrEqual(4)
    expect(s.alarms.length).toBeLessThanOrEqual(9)
    expect(s.repairs.length).toBeGreaterThanOrEqual(3)
    expect(s.repairs.length).toBeLessThanOrEqual(6)
    expect(s.pmTasks.length).toBeGreaterThanOrEqual(4)
    expect(s.inspections.length).toBeGreaterThanOrEqual(6)
  })

  it('#686 报警响应状态：首发未确认+已升级（高亮），已确认项带确认人', () => {
    const s = buildEquipmentOverview()
    const critical = s.alarms.find((a) => a.name.includes('DEV-CNC-03'))!
    expect(critical.level).toBe('sev')
    expect(critical.acked).toBe(false)
    expect(critical.escalated).toBe(true)
    const acked = s.alarms.find((a) => a.acked === true)
    expect(acked).toBeDefined()
    expect(acked!.ackBy).toBeTruthy()
  })

  it('格上关键参数：每台 ≥1 且带类型；断线全「—」；报警设备存在超限红参数', () => {
    const s = buildEquipmentOverview()
    for (const d of s.devices) {
      expect(d.params.length).toBeGreaterThanOrEqual(1)
      for (const p of d.params) expect(p.kind).toBeTruthy()
    }
    const off = s.devices.find((d) => d.state === 'offline')
    expect(off).toBeDefined()
    expect(off!.params.every((p) => p.value === '—')).toBe(true)
    const alarm = s.devices.find((d) => d.state === 'alarm')
    expect(alarm).toBeDefined()
    expect(alarm!.params.some((p) => p.tone === 'bad')).toBe(true)
  })

  it('参数快刷 tick：缺省全量；传可见集则只含视野内设备（视野外停更）', () => {
    const s = buildEquipmentOverview()
    const tick = buildParamsTick()
    for (const d of s.devices) {
      expect(tick[d.id]).toBeDefined()
      expect(tick[d.id].length).toBeGreaterThanOrEqual(1)
    }
    const visible = s.devices.slice(0, 6).map((d) => d.id)
    const partial = buildParamsTick(DEFAULT_FACTORY_ID, 'all', visible)
    expect(Object.keys(partial).sort()).toEqual([...visible].sort())
  })
})

describe('buildDeviceDetail', () => {
  it('同源画像、参数 12 点趋势（值=末点）、维修档案联动、断线无数据', () => {
    const s = buildEquipmentOverview()
    const alarm = s.devices.find((d) => d.state === 'alarm')!
    const det = buildDeviceDetail(alarm.id)
    expect(det).not.toBeNull()
    expect(det!.device.id).toBe(alarm.id)
    // DEV-CNC-03 的采集点位就是 3 个（主轴温度/振动/主轴转速）——
    // 与后端遥测种子 WorldHistoryDeviceSpec 的 DEV-CNC- 类别逐条一致，不多编第 4 个。
    expect(det!.params.map((p) => p.label)).toEqual(['主轴温度', '振动', '主轴转速'])
    for (const p of det!.params) {
      expect(p.spark).toHaveLength(12)
      expect(p.value).toBe(p.spark[p.spark.length - 1])
    }
    // 报警设备 ↔ 维修单闭环；有故障样本 → 单机 MTBF 有值
    expect(det!.repairs.some((r) => r.wo === 'MWO-2026-0118')).toBe(true)
    expect(det!.mtbfHours).not.toBeNull()
    expect(det!.workCenterName).toBe('活塞杆加工中心一线')
    // 正常设备无故障样本 → 单机 MTBF/MTTR null（页面显「—」）
    const ok = s.devices.find((d) => d.state === 'run')!
    const detOk = buildDeviceDetail(ok.id)!
    expect(detOk.mtbfHours).toBeNull()
    expect(detOk.mttrMinutes).toBeNull()
    // 断线设备：参数无数据（value null + spark 空 → 图示虚线占位）
    const off = s.devices.find((d) => d.state === 'offline')!
    const detOff = buildDeviceDetail(off.id)!
    expect(detOff.params.every((p) => p.value === null && p.spark.length === 0)).toBe(true)
    // 未知设备 → null
    expect(buildDeviceDetail('DEV-999')).toBeNull()
  })
})
