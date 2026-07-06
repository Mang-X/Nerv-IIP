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

// 每车间 2–3 条产线（2026-07 生产走查：产线数扩至接近真实工厂规模，供监控屏滚动）
export const LINES: LineRef[] = [
  { id: 'LN-STAMP-1', code: 'LN-STAMP-1', name: '冲压一线', workshopId: 'WS-STAMP' },
  { id: 'LN-STAMP-2', code: 'LN-STAMP-2', name: '冲压二线', workshopId: 'WS-STAMP' },
  { id: 'LN-STAMP-3', code: 'LN-STAMP-3', name: '冲压三线', workshopId: 'WS-STAMP' },
  { id: 'LN-WELD-1', code: 'LN-WELD-1', name: '焊装一线', workshopId: 'WS-WELD' },
  { id: 'LN-WELD-2', code: 'LN-WELD-2', name: '焊装二线', workshopId: 'WS-WELD' },
  { id: 'LN-WELD-3', code: 'LN-WELD-3', name: '焊装三线', workshopId: 'WS-WELD' },
  { id: 'LN-PAINT-1', code: 'LN-PAINT-1', name: '涂装一线', workshopId: 'WS-PAINT' },
  { id: 'LN-PAINT-2', code: 'LN-PAINT-2', name: '面漆线', workshopId: 'WS-PAINT' },
  { id: 'LN-ASSY-1', code: 'LN-ASSY-1', name: '总装一线', workshopId: 'WS-ASSY' },
  { id: 'LN-ASSY-2', code: 'LN-ASSY-2', name: '总装二线', workshopId: 'WS-ASSY' },
  { id: 'LN-ASSY-3', code: 'LN-ASSY-3', name: '总装三线', workshopId: 'WS-ASSY' },
  { id: 'LN-BAT-1', code: 'LN-BAT-1', name: '电芯线', workshopId: 'WS-BATTERY' },
  { id: 'LN-BAT-2', code: 'LN-BAT-2', name: 'PACK 线', workshopId: 'WS-BATTERY' },
  { id: 'LN-INJ-1', code: 'LN-INJ-1', name: '注塑一线', workshopId: 'WS-INJECT' },
  { id: 'LN-INJ-2', code: 'LN-INJ-2', name: '注塑二线', workshopId: 'WS-INJECT' },
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

// 每产线真实设备清单（数量/命名贴近真实产线，共 56 台；2026-07 生产走查：
// 原「每线 2 台主/辅机」过于理想化，缺少大量设备场景）
const LINE_DEVICES: Record<string, string[]> = {
  'LN-STAMP-1': ['800T 压机 1#', '800T 压机 2#', '送料机器人', '模具清洗机', '板料对中台'],
  'LN-STAMP-2': ['1000T 压机', '600T 压机', '上料机械手', '端拾器库'],
  'LN-STAMP-3': ['1600T 压机', '送料机器人 2#', '落料线', '废料输送机'],
  'LN-WELD-1': ['焊接机器人 R01', '焊接机器人 R02', '焊接机器人 R03', '点焊控制柜', '输送滚床 1#', '涂胶机'],
  'LN-WELD-2': ['焊接机器人 R11', '焊接机器人 R12', '激光焊接站', '夹具切换台', '输送滚床 2#'],
  'LN-WELD-3': ['焊接机器人 R21', '焊接机器人 R22', '螺柱焊机', '输送滚床 3#', '视觉检测站'],
  'LN-PAINT-1': ['前处理线体', '电泳槽', '喷涂机器人 P01', '喷涂机器人 P02', '流平烘干炉', '空调送风机组'],
  'LN-PAINT-2': ['面漆机器人 F01', '面漆机器人 F02', '烘干炉 2#', '喷房送风机'],
  'LN-ASSY-1': ['拧紧工作站 1#', '拧紧工作站 2#', '油液加注机', '合装举升机', 'AGV 牵引车 01', '下线检测台'],
  'LN-ASSY-2': ['拧紧工作站 3#', '内饰装配线体', 'AGV 牵引车 02', '风挡涂胶机', '四轮定位仪'],
  'LN-ASSY-3': ['拧紧工作站 4#', '玻璃安装机器人', '注油机', '路试台'],
  'LN-BAT-1': ['卷绕机 1#', '卷绕机 2#', '注液机', '化成柜 A', '化成柜 B', '分容柜'],
  'LN-BAT-2': ['模组堆叠机', 'PACK 线体', '气密检测台', 'EOL 测试柜'],
  'LN-INJ-1': ['注塑机 1600T', '注塑机 800T', '取件机械手', '原料干燥机'],
  'LN-INJ-2': ['注塑机 1200T', '注塑机 650T', '机械手 2#', '混料机'],
  'LN-MACH-1': ['加工中心 M01', '加工中心 M02', '车铣复合 M03', '零件清洗机', '三坐标测量机'],
}
let deviceSeq = 0
export const DEVICES: DeviceRef[] = LINES.flatMap((l) =>
  (LINE_DEVICES[l.id] ?? []).map((name) => {
    deviceSeq += 1
    return {
      id: `DEV-${String(deviceSeq).padStart(3, '0')}`,
      code: `DEV-${String(deviceSeq).padStart(3, '0')}`,
      name,
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
