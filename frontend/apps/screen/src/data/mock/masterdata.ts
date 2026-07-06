// 工厂→车间→产线→工作中心→设备 映射字典（mock）。
// 真实平台无 workshop/line 聚合维度，最细到 WorkCenter/Device；此处提供前端聚合所需映射真相源。
// 见 spec §1.1「数据现实」。

export interface FactoryRef {
  id: string
  name: string
}
export interface WorkshopRef {
  id: string
  code: string
  name: string
  factoryId: string
  managerName: string
}
export interface LineRef {
  id: string
  code: string
  name: string
  workshopId: string
}
export interface WorkCenterRef {
  id: string
  code: string
  name: string
  workshopId: string
  lineId: string
}
export interface DeviceRef {
  id: string
  code: string
  name: string
  workshopId: string
  lineId: string
  workCenterId: string
}

export const FACTORIES: FactoryRef[] = [
  { id: 'F01', name: '华东智造基地' },
  { id: 'F02', name: '华南制造中心' },
]

export const WORKSHOPS: WorkshopRef[] = [
  { id: 'WS-STAMP', code: 'WS-STAMP', name: '冲压车间', factoryId: 'F01', managerName: '李国强' },
  { id: 'WS-WELD', code: 'WS-WELD', name: '焊装车间', factoryId: 'F01', managerName: '王海涛' },
  { id: 'WS-PAINT', code: 'WS-PAINT', name: '涂装车间', factoryId: 'F01', managerName: '陈晓东' },
  { id: 'WS-ASSY', code: 'WS-ASSY', name: '总装车间', factoryId: 'F01', managerName: '赵敏' },
  { id: 'WS-BATTERY', code: 'WS-BATTERY', name: '电池车间', factoryId: 'F01', managerName: '孙立军' },
  { id: 'WS-INJECT', code: 'WS-INJECT', name: '注塑车间', factoryId: 'F02', managerName: '周文斌' },
  { id: 'WS-MACH', code: 'WS-MACH', name: '机加车间', factoryId: 'F02', managerName: '吴俊' },
]

// 每车间 1–2 条产线
export const LINES: LineRef[] = [
  { id: 'LN-STAMP-1', code: 'LN-STAMP-1', name: '冲压一线', workshopId: 'WS-STAMP' },
  { id: 'LN-STAMP-2', code: 'LN-STAMP-2', name: '冲压二线', workshopId: 'WS-STAMP' },
  { id: 'LN-WELD-1', code: 'LN-WELD-1', name: '焊装一线', workshopId: 'WS-WELD' },
  { id: 'LN-WELD-2', code: 'LN-WELD-2', name: '焊装二线', workshopId: 'WS-WELD' },
  { id: 'LN-PAINT-1', code: 'LN-PAINT-1', name: '涂装线', workshopId: 'WS-PAINT' },
  { id: 'LN-ASSY-1', code: 'LN-ASSY-1', name: '总装一线', workshopId: 'WS-ASSY' },
  { id: 'LN-ASSY-2', code: 'LN-ASSY-2', name: '总装二线', workshopId: 'WS-ASSY' },
  { id: 'LN-BAT-1', code: 'LN-BAT-1', name: '电芯线', workshopId: 'WS-BATTERY' },
  { id: 'LN-BAT-2', code: 'LN-BAT-2', name: 'PACK 线', workshopId: 'WS-BATTERY' },
  { id: 'LN-INJ-1', code: 'LN-INJ-1', name: '注塑一线', workshopId: 'WS-INJECT' },
  { id: 'LN-MACH-1', code: 'LN-MACH-1', name: '机加线', workshopId: 'WS-MACH' },
]

// 每产线 1 个工作中心（mock 简化）
export const WORK_CENTERS: WorkCenterRef[] = LINES.map((l) => ({
  id: `WC-${l.code.replace('LN-', '')}`,
  code: `WC-${l.code.replace('LN-', '')}`,
  name: `${l.name}工作中心`,
  workshopId: l.workshopId,
  lineId: l.id,
}))

// 每产线 2 台设备
const DEVICE_KINDS = ['主机', '辅机']
export const DEVICES: DeviceRef[] = LINES.flatMap((l, li) =>
  DEVICE_KINDS.map((kind, ki) => {
    const n = li * DEVICE_KINDS.length + ki + 1
    return {
      id: `DEV-${String(n).padStart(3, '0')}`,
      code: `DEV-${String(n).padStart(3, '0')}`,
      name: `${l.name}${kind}`,
      workshopId: l.workshopId,
      lineId: l.id,
      workCenterId: `WC-${l.code.replace('LN-', '')}`,
    }
  }),
)

export function workshopsByFactory(factoryId: string): WorkshopRef[] {
  return WORKSHOPS.filter((w) => w.factoryId === factoryId)
}
export function linesByWorkshop(workshopId: string): LineRef[] {
  return LINES.filter((l) => l.workshopId === workshopId)
}
export function workCentersByLine(lineId: string): WorkCenterRef[] {
  return WORK_CENTERS.filter((wc) => wc.lineId === lineId)
}
export function devicesByLine(lineId: string): DeviceRef[] {
  return DEVICES.filter((d) => d.lineId === lineId)
}
export function devicesByWorkshop(workshopId: string): DeviceRef[] {
  return DEVICES.filter((d) => d.workshopId === workshopId)
}
