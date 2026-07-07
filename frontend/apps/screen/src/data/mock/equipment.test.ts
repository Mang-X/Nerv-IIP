import { describe, expect, it } from 'vitest'
import { REPAIR_STAGES } from '@/data/contracts/equipment'
import {
  buildDeviceDetail,
  buildEquipmentOverview,
  buildParamsTick,
  chunkIds,
  DEVICE_BATCH_LIMIT,
} from './equipment'
import { devicesByWorkshop, workshopsByFactory } from './masterdata'

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
  it('F01：设备数对账、五态互斥计数、断线防假绿、报警→工单闭环', () => {
    const expectDevices = workshopsByFactory('F01').reduce(
      (n, w) => n + devicesByWorkshop(w.id).length,
      0,
    )
    for (let i = 0; i < ROUNDS; i++) {
      const s = buildEquipmentOverview('F01')
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
      // 报警行都已触发工单（闭环 ✅）
      for (const a of s.alarms) expect(a.wo).toMatch(/^WO-/)
      // 维修单按状态机 + 时间衡量（非百分比）；存在 超时/待确认/待备件 三种演示态
      for (const r of s.repairs) {
        expect(REPAIR_STAGES).toContain(r.stage)
        expect(r.elapsedMin).toBeGreaterThan(0)
        expect(r.reportedAt).toMatch(/^\d{2}:\d{2}$/)
        expect(r.assignee).toBeTruthy()
      }
      expect(s.repairs.some((r) => r.overdue)).toBe(true)
      expect(s.repairs.some((r) => r.awaitingConfirm)).toBe(true)
      expect(s.repairs.some((r) => r.blockedBy)).toBe(true)
      // 可靠性：F01 样本充足，MTBF/MTTR 有值
      expect(s.reliability.mtbfHours).not.toBeNull()
      expect(s.reliability.availability).toBeGreaterThanOrEqual(0)
      expect(s.reliability.availability).toBeLessThanOrEqual(100)
    }
  })

  it('小样本保护：无设备样本 MTBF/MTTR 为 null（诚实缺口，不硬算）；样本足有值', () => {
    // M2 扩容后所有真实车间均 ≥6 台（机加 2 线 9 台），小样本 null 分支用空收窄验证
    const empty = buildEquipmentOverview('F01', ['WS-NONE'])
    expect(empty.devices.length).toBe(0)
    expect(empty.reliability.mtbfHours).toBeNull()
    expect(empty.reliability.mttrMinutes).toBeNull()
    const mach = buildEquipmentOverview('F02', ['WS-MACH'])
    expect(mach.devices.length).toBeGreaterThanOrEqual(6)
    expect(mach.reliability.mtbfHours).not.toBeNull()
    const full = buildEquipmentOverview('F02')
    expect(full.reliability.mtbfHours).not.toBeNull()
  })

  it('scope 收窄：设备/报警/维修/保养/点检各档案同步收窄', () => {
    const s = buildEquipmentOverview('F01', ['WS-BATTERY'])
    expect(s.devices).toHaveLength(devicesByWorkshop('WS-BATTERY').length)
    const names = new Set(s.devices.map((d) => d.name))
    const batLines = ['电芯线', '电芯二线', '模组线', 'PACK 线', 'PACK 二线']
    for (const d of s.devices) expect(batLines).toContain(d.lineName)
    for (const a of s.alarms) expect(batLines).toContain(a.line)
    for (const r of s.repairs) expect(names.has(r.device)).toBe(true)
    for (const t of s.pmTasks) expect(names.has(t.device)).toBe(true)
    for (const i of s.inspections) expect(names.has(i.device)).toBe(true)
  })

  it('档案量符合「正常工厂日」画像：异常少量、台账留档（F01）', () => {
    const s = buildEquipmentOverview('F01')
    // 异常是例外：未恢复报警/进行中维修各只有少量
    expect(s.alarms.length).toBeGreaterThanOrEqual(4)
    expect(s.alarms.length).toBeLessThanOrEqual(9)
    expect(s.repairs.length).toBeGreaterThanOrEqual(3)
    expect(s.repairs.length).toBeLessThanOrEqual(6)
    expect(s.pmTasks.length).toBeGreaterThanOrEqual(4)
    // 点检台账（合格记录）保留在设备档案维度
    expect(s.inspections.length).toBeGreaterThanOrEqual(6)
  })

  it('格上关键参数：每台 ≥2 且带类型；断线全「—」；报警设备存在超限红参数', () => {
    const s = buildEquipmentOverview('F01')
    for (const d of s.devices) {
      expect(d.params.length).toBeGreaterThanOrEqual(2)
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
    const s = buildEquipmentOverview('F01')
    const tick = buildParamsTick('F01')
    for (const d of s.devices) {
      expect(tick[d.id]).toBeDefined()
      expect(tick[d.id].length).toBeGreaterThanOrEqual(2)
    }
    const visible = s.devices.slice(0, 6).map((d) => d.id)
    const partial = buildParamsTick('F01', 'all', visible)
    expect(Object.keys(partial).sort()).toEqual([...visible].sort())
  })
})

describe('buildDeviceDetail', () => {
  it('同源画像、4 参数 12 点趋势（值=末点）、维修档案联动、断线无数据', () => {
    const s = buildEquipmentOverview('F01')
    const alarm = s.devices.find((d) => d.state === 'alarm')!
    const det = buildDeviceDetail(alarm.id)
    expect(det).not.toBeNull()
    expect(det!.device.id).toBe(alarm.id)
    expect(det!.params).toHaveLength(4)
    for (const p of det!.params) {
      expect(p.spark).toHaveLength(12)
      expect(p.value).toBe(p.spark[p.spark.length - 1])
    }
    // 报警设备 ↔ 维修单闭环（WO-1934）；有故障样本 → 单机 MTBF 有值
    expect(det!.repairs.some((r) => r.wo === 'WO-1934')).toBe(true)
    expect(det!.mtbfHours).not.toBeNull()
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
