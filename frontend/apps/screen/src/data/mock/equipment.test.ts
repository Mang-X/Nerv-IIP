import { describe, expect, it } from 'vitest'
import { buildEquipmentOverview, chunkIds, DEVICE_BATCH_LIMIT } from './equipment'
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
      // 维修单进度在界内，且存在 超时/待确认 两种演示态
      for (const r of s.repairs) {
        expect(r.progress).toBeGreaterThanOrEqual(0)
        expect(r.progress).toBeLessThanOrEqual(100)
      }
      expect(s.repairs.some((r) => r.overdue)).toBe(true)
      expect(s.repairs.some((r) => r.awaitingConfirm)).toBe(true)
      // 可靠性：F01 样本充足，MTBF/MTTR 有值
      expect(s.reliability.mtbfHours).not.toBeNull()
      expect(s.reliability.availability).toBeGreaterThanOrEqual(0)
      expect(s.reliability.availability).toBeLessThanOrEqual(100)
    }
  })

  it('F02：小样本 MTBF/MTTR 为 null（页面显「—」）', () => {
    const s = buildEquipmentOverview('F02')
    expect(s.devices.length).toBeLessThan(6)
    expect(s.reliability.mtbfHours).toBeNull()
    expect(s.reliability.mttrMinutes).toBeNull()
  })

  it('scope 收窄：只聚合白名单车间设备', () => {
    const s = buildEquipmentOverview('F01', ['WS-BATTERY'])
    expect(s.devices).toHaveLength(devicesByWorkshop('WS-BATTERY').length)
    for (const d of s.devices) expect(['电芯线', 'PACK 线']).toContain(d.lineName)
  })
})
