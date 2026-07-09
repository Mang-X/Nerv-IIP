// 真实取数适配勾稽测试（MAN-466）：打 business-console facade 契约桩，
// 验证 facade 响应 → 大屏 EquipmentOverview/DeviceDetail 契约的映射（状态归一、
// 断线防假绿、报警 ack #686、报警↔工单联动、点检↔设备联动、聚合可靠性、单机 MTBF/MTTR）。
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleDeviceAssets: vi.fn(),
  getBusinessConsoleEquipmentOverview: vi.fn(),
  getBusinessConsoleEquipmentDevice: vi.fn(),
  listBusinessConsoleEquipmentAlarms: vi.fn(),
  listBusinessConsoleMaintenanceWorkOrders: vi.fn(),
  listBusinessConsoleMaintenancePlans: vi.fn(),
  listBusinessConsoleMaintenanceInspections: vi.fn(),
  queryBusinessConsoleMaintenanceAssetReliability: vi.fn(),
}))

import * as api from '@nerv-iip/api-client'
import { resetDeviceRosterCache, setScreenSession } from '@/data/session'
import { fetchRealDeviceDetail, fetchRealEquipmentOverview } from './equipment'

/** SDK（throwOnError:true）返回 { data: envelope }，envelope = { success, data }。 */
function ok(data: unknown) {
  return { data: { success: true, data } } as never
}

const ROSTER = {
  resources: [
    {
      code: 'DEV-CNC-01',
      displayName: '加工中心 M01',
      lineCode: 'LN-MACH-1',
      workshopCode: 'WS-MACH',
      workCenterCode: 'WC-MACH-1',
    },
    {
      code: 'DEV-OIL-02',
      displayName: '液压站',
      lineCode: 'LN-ASSY-1',
      workshopCode: 'WS-ASSY',
      workCenterCode: 'WC-ASSY-1',
    },
    { code: 'DEV-OFF-03', displayName: '断线设备', lineCode: 'LN-MACH-1', workshopCode: 'WS-MACH' },
  ],
}

beforeEach(() => {
  vi.clearAllMocks()
  resetDeviceRosterCache()
  setScreenSession({ organizationId: 'org-001', environmentId: 'env-dev' })
})

describe('fetchRealEquipmentOverview', () => {
  beforeEach(() => {
    vi.mocked(api.listBusinessConsoleDeviceAssets).mockResolvedValue(ok(ROSTER))
    vi.mocked(api.getBusinessConsoleEquipmentOverview).mockResolvedValue(
      ok({
        devices: [
          {
            deviceAssetId: 'DEV-CNC-01',
            currentState: 'running',
            isSourceFresh: true,
            activeAlarmCount: 0,
            activeBlockCount: 0,
          },
          {
            deviceAssetId: 'DEV-OIL-02',
            currentState: 'faulted',
            isSourceFresh: true,
            activeAlarmCount: 1,
            activeBlockCount: 1,
          },
          {
            deviceAssetId: 'DEV-OFF-03',
            currentState: null,
            isSourceFresh: false,
            activeAlarmCount: 0,
            activeBlockCount: 0,
          },
        ],
        activeBlocks: [
          {
            deviceAssetId: 'DEV-OIL-02',
            reasonCode: 'equipment.activeAlarm',
            severity: 'critical',
          },
        ],
      }),
    )
    vi.mocked(api.listBusinessConsoleEquipmentAlarms).mockResolvedValue(
      ok({
        items: [
          {
            alarmEventId: 'AL-1',
            deviceAssetId: 'DEV-OIL-02',
            alarmCode: 'PRESS_HIGH',
            severity: 'critical',
            raisedAtUtc: '2026-07-08T02:00:00Z',
            acknowledgedAtUtc: null,
            escalatedAtUtc: '2026-07-08T02:10:00Z',
          },
          {
            alarmEventId: 'AL-2',
            deviceAssetId: 'DEV-CNC-01',
            alarmCode: 'TEMP_WARN',
            severity: 'warning',
            raisedAtUtc: '2026-07-08T01:00:00Z',
            acknowledgedAtUtc: '2026-07-08T01:05:00Z',
            acknowledgedBy: '张三',
          },
          {
            alarmEventId: 'AL-3',
            deviceAssetId: 'DEV-CNC-01',
            alarmCode: 'DOOR_OPEN',
            severity: 'info',
            raisedAtUtc: '2026-07-08T00:00:00Z',
            clearedAtUtc: '2026-07-08T00:30:00Z',
          },
        ],
      }),
    )
    vi.mocked(api.listBusinessConsoleMaintenanceWorkOrders).mockResolvedValue(
      ok({
        items: [
          {
            workOrderId: 'WO-1',
            deviceAssetId: 'DEV-OIL-02',
            status: 'Open',
            sourceAlarmId: 'AL-1',
            openedAtUtc: '2026-07-08T01:30:00Z',
            assignedTechnicianUserId: '李四',
            estimatedLaborMinutes: 60,
          },
          {
            workOrderId: 'WO-2',
            deviceAssetId: 'DEV-CNC-01',
            status: 'Completed',
            openedAtUtc: '2026-07-07T10:00:00Z',
          },
        ],
      }),
    )
    vi.mocked(api.listBusinessConsoleMaintenancePlans).mockResolvedValue(
      ok({
        items: [
          {
            planId: 'PM-1',
            deviceAssetId: 'DEV-CNC-01',
            planCode: 'PM-MONTHLY',
            interval: 'P30D',
            startsOn: '2026-07-10T00:00:00Z',
          },
        ],
      }),
    )
    vi.mocked(api.listBusinessConsoleMaintenanceInspections).mockResolvedValue(
      ok({
        items: [
          {
            inspectionId: 'IN-1',
            workOrderId: 'WO-1',
            inspector: '王五',
            result: 'pass',
            inspectedAtUtc: '2026-07-08T02:10:00Z',
            measurements: [{ characteristicCode: '油位' }],
          },
        ],
      }),
    )
  })

  it('设备状态归一 + 断线防假绿 + roster 名称联动 + 无 historian 参数为空', async () => {
    const ov = await fetchRealEquipmentOverview('F01')
    expect(ov.factoryId).toBe('F01')
    expect(ov.devices).toHaveLength(3)

    const cnc = ov.devices.find((d) => d.id === 'DEV-CNC-01')!
    expect(cnc.state).toBe('run') // running → run
    expect(cnc.name).toBe('加工中心 M01') // roster 台账联动
    expect(cnc.params).toEqual([]) // historian 待 #570，诚实空

    const oil = ov.devices.find((d) => d.id === 'DEV-OIL-02')!
    expect(oil.state).toBe('alarm') // activeAlarmCount>0 覆盖物理态
    expect(oil.block).toBe('设备报警未解除') // reasonCode → 中文

    const off = ov.devices.find((d) => d.id === 'DEV-OFF-03')!
    expect(off.state).toBe('offline') // !isSourceFresh → offline（不算 run）
    expect(off.sourceFresh).toBe(false)

    // 五态计数与设备逐台归并一致
    const sum =
      ov.counts.run + ov.counts.idle + ov.counts.down + ov.counts.alarm + ov.counts.offline
    expect(sum).toBe(3)
    expect(ov.counts.run).toBe(1)
    expect(ov.counts.alarm).toBe(1)
    expect(ov.counts.offline).toBe(1)
  })

  it('活动报警：#686 响应状态 + 级别 + 报警↔工单联动', async () => {
    const ov = await fetchRealEquipmentOverview('F01')
    expect(ov.alarms).toHaveLength(3)

    const press = ov.alarms.find((a) => a.name === 'PRESS_HIGH')!
    expect(press.acked).toBe(false) // 未确认 → 高亮
    expect(press.escalated).toBe(true)
    expect(press.level).toBe('sev') // critical → sev
    expect(press.wo).toBe('WO-1') // sourceAlarmId=AL-1 → WO-1
    expect(press.line).toBe('液压站') // roster 名称

    const temp = ov.alarms.find((a) => a.name === 'TEMP_WARN')!
    expect(temp.acked).toBe(true)
    expect(temp.ackBy).toBe('张三')
    expect(temp.level).toBe('gen')

    const cleared = ov.alarms.find((a) => a.name === 'DOOR_OPEN')!
    expect(cleared.status.startsWith('已恢复')).toBe(true) // 页面据此过滤合并流
  })

  it('维修工单：Open+技师→维修中，Completed→已关闭；聚合可靠性可用率=运行占比、聚合MTBF为null', async () => {
    const ov = await fetchRealEquipmentOverview('F01')
    const wo1 = ov.repairs.find((r) => r.wo === 'WO-1')!
    expect(wo1.stage).toBe('维修中')
    expect(wo1.device).toBe('液压站')
    expect(wo1.assignee).toBe('李四')
    const wo2 = ov.repairs.find((r) => r.wo === 'WO-2')!
    expect(wo2.stage).toBe('已关闭')

    expect(ov.reliability.availability).toBe(33) // run(1)/total(3) → 33%
    expect(ov.reliability.mtbfHours).toBeNull() // 聚合无端点，诚实 null
    expect(ov.reliability.mttrMinutes).toBeNull()
    expect(ov.reliability.repairs).toBe(1) // 进行中（非已关闭）维修单数
  })

  it('PM 计划与点检台账（点检经工单反查设备）', async () => {
    const ov = await fetchRealEquipmentOverview('F01')
    expect(ov.pmTasks).toHaveLength(1)
    expect(ov.pmTasks[0].device).toBe('加工中心 M01')
    expect(ov.pmTasks[0].state).toBe('due')

    expect(ov.inspections).toHaveLength(1)
    expect(ov.inspections[0].device).toBe('液压站') // IN-1.workOrderId=WO-1 → DEV-OIL-02
    expect(ov.inspections[0].result).toBe('合格') // pass → 合格
  })

  it('空设备台账：不请求 overview，返回空设备但不崩', async () => {
    vi.mocked(api.listBusinessConsoleDeviceAssets).mockResolvedValue(ok({ items: [] }))
    const ov = await fetchRealEquipmentOverview('F01')
    expect(ov.devices).toHaveLength(0)
    expect(api.getBusinessConsoleEquipmentOverview).not.toHaveBeenCalled()
    expect(ov.reliability.availability).toBe(0)
  })
})

describe('fetchRealEquipmentOverview code≠deviceAssetId 联动', () => {
  it('roster 以 deviceAssetId 作 join 键（非 code）；overview/alarm 按 deviceAssetId 命中，code 仅展示', async () => {
    // master-data code=DEV-001 但 telemetry deviceAssetId=018f-cnc-01（网关测试覆盖二者分离）。
    vi.mocked(api.listBusinessConsoleDeviceAssets).mockResolvedValue(
      ok({
        resources: [
          {
            code: 'DEV-001',
            deviceAssetId: '018f-cnc-01',
            displayName: '加工中心 M01',
            lineCode: 'LN-MACH-1',
            workshopCode: 'WS-MACH',
          },
        ],
      }),
    )
    vi.mocked(api.getBusinessConsoleEquipmentOverview).mockResolvedValue(
      ok({
        devices: [
          {
            deviceAssetId: '018f-cnc-01',
            currentState: 'running',
            isSourceFresh: true,
            activeAlarmCount: 0,
            activeBlockCount: 0,
          },
        ],
        activeBlocks: [],
      }),
    )
    vi.mocked(api.listBusinessConsoleEquipmentAlarms).mockResolvedValue(
      ok({
        items: [
          {
            alarmEventId: 'AL-9',
            deviceAssetId: '018f-cnc-01',
            alarmCode: 'TEMP_HIGH',
            severity: 'warning',
            raisedAtUtc: '2026-07-08T02:00:00Z',
          },
        ],
      }),
    )
    vi.mocked(api.listBusinessConsoleMaintenanceWorkOrders).mockResolvedValue(ok({ items: [] }))
    vi.mocked(api.listBusinessConsoleMaintenancePlans).mockResolvedValue(ok({ items: [] }))
    vi.mocked(api.listBusinessConsoleMaintenanceInspections).mockResolvedValue(ok({ items: [] }))

    const ov = await fetchRealEquipmentOverview('F01')

    // overview 查询用的是 deviceAssetId，而非 master-data code
    expect(api.getBusinessConsoleEquipmentOverview).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({ deviceAssetIds: '018f-cnc-01' }),
      }),
    )
    // 设备命中：id/join=deviceAssetId、name 来自 roster、code 展示 master-data code
    expect(ov.devices).toHaveLength(1)
    expect(ov.devices[0].id).toBe('018f-cnc-01')
    expect(ov.devices[0].name).toBe('加工中心 M01')
    expect(ov.devices[0].code).toBe('DEV-001')
    expect(ov.devices[0].state).toBe('run')
    // 报警按 deviceAssetId 命中设备名（code≠deviceAssetId 时不丢失联动）
    expect(ov.alarms[0].line).toBe('加工中心 M01')
  })
})

describe('fetchRealEquipmentOverview 会话守卫', () => {
  it('无 org/env 上下文 → 抛错（useScreenData 标 stale、不打后端）', async () => {
    setScreenSession({ organizationId: '', environmentId: '' })
    await expect(fetchRealEquipmentOverview('F01')).rejects.toThrow()
    expect(api.listBusinessConsoleDeviceAssets).not.toHaveBeenCalled()
  })
})

describe('fetchRealDeviceDetail', () => {
  beforeEach(() => {
    vi.mocked(api.listBusinessConsoleDeviceAssets).mockResolvedValue(ok(ROSTER))
    vi.mocked(api.getBusinessConsoleEquipmentDevice).mockResolvedValue(
      ok({
        currentState: {
          deviceAssetId: 'DEV-OIL-02',
          currentState: 'faulted',
          isSourceFresh: true,
          activeAlarms: [{ alarmEventId: 'AL-1' }],
        },
      }),
    )
    vi.mocked(api.queryBusinessConsoleMaintenanceAssetReliability).mockResolvedValue(
      ok({
        deviceAssetId: 'DEV-OIL-02',
        failureCount: 3,
        repairCount: 2,
        mtbfHours: 76,
        mttrMinutes: 42,
      }),
    )
    vi.mocked(api.listBusinessConsoleMaintenanceWorkOrders).mockResolvedValue(
      ok({
        items: [
          {
            workOrderId: 'WO-1',
            deviceAssetId: 'DEV-OIL-02',
            status: 'Open',
            openedAtUtc: '2026-07-08T01:30:00Z',
            assignedTechnicianUserId: '李四',
          },
        ],
      }),
    )
    vi.mocked(api.listBusinessConsoleMaintenancePlans).mockResolvedValue(ok({ items: [] }))
    vi.mocked(api.listBusinessConsoleMaintenanceInspections).mockResolvedValue(ok({ items: [] }))
  })

  it('单机 MTBF/MTTR 取 reliability 端点；只含本设备维修单', async () => {
    const det = await fetchRealDeviceDetail('DEV-OIL-02')
    expect(det).not.toBeNull()
    expect(det!.device.name).toBe('液压站')
    expect(det!.device.state).toBe('alarm') // 有 activeAlarms
    expect(det!.mtbfHours).toBe(76)
    expect(det!.mttrMinutes).toBe(42)
    expect(det!.repairs).toHaveLength(1)
    expect(det!.repairs[0].device).toBe('液压站')
    expect(det!.params).toEqual([]) // 趋势待 historian
  })

  it('facade 返回 success=false → null（未知设备）', async () => {
    vi.mocked(api.getBusinessConsoleEquipmentDevice).mockResolvedValue({
      data: { success: false, data: null },
    } as never)
    const det = await fetchRealDeviceDetail('DEV-UNKNOWN')
    expect(det).toBeNull()
  })
})
