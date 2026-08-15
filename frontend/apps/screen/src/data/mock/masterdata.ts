// 工厂→车间→产线→工作中心→设备 映射字典（mock）。
// 真实平台无 workshop/line 聚合维度，最细到 WorkCenter/Device；此处提供前端聚合所需映射真相源。
// 见 spec §1.1「数据现实」。
//
// ⚠️ 本文件是《工厂世界观设定集》（docs/superpowers/plans/2026-07-26-factory-world-bible.md）
// L0 主数据在大屏侧的镜像：编码与中文名**逐字**取自后端权威种子
// `backend/services/Business/MasterData/.../Application/Seed/WorldBibleSpec.cs`
// （SITE-001 一号工厂 / 3 车间 / 14 产线 / 17 工作中心 / 46 台设备）。
// 领导会在 PC 控制台与大屏之间来回看，两边必须是同一家工厂 —— 改这里前先改设定集。

export interface FactoryRef {
  id: string
  name: string
}
export interface WorkshopRef {
  id: string
  code: string
  /** L0 全名（与 PC 控制台一致），如「一车间 · 机加车间」 */
  name: string
  /** 远视距短名（角标/事件流/chip 用），如「机加车间」 */
  shortName: string
  factoryId: string
  /** 车间主任（L0 §5：EMP-001..003） */
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
  /** 设备型号名（L0 §3），如「数控车床 CK6150」 */
  name: string
  /** 设备类别键，参数模板与报警语义按它匹配 */
  category: DeviceCategory
  workshopId: string
  lineId: string
  workCenterId: string
}

export type DeviceCategory =
  | 'cnc'
  | 'grinder'
  | 'welding-robot'
  | 'assembly-station'
  | 'coating'
  | 'test-bench'
  | 'packaging-line'
  | 'utility'

// 设定集 §1：宁沪减振科技只有一个基地 SITE-001「一号工厂」——
// 门厅的工厂切换器在只有一个工厂时自动隐藏（index.vue `factories.length > 1`）。
export const FACTORIES: FactoryRef[] = [{ id: 'SITE-001', name: '宁沪减振 · 一号工厂' }]

/** 默认工厂 id：各 build* 纯函数的缺省 scope。 */
export const DEFAULT_FACTORY_ID = 'SITE-001'

export const WORKSHOPS: WorkshopRef[] = [
  {
    id: 'WS-01',
    code: 'WS-01',
    name: '一车间 · 机加车间',
    shortName: '机加车间',
    factoryId: DEFAULT_FACTORY_ID,
    managerName: '王建国',
  },
  {
    id: 'WS-02',
    code: 'WS-02',
    name: '二车间 · 装配车间',
    shortName: '装配车间',
    factoryId: DEFAULT_FACTORY_ID,
    managerName: '李春梅',
  },
  {
    id: 'WS-03',
    code: 'WS-03',
    name: '三车间 · 表面与包装车间',
    shortName: '表面与包装车间',
    factoryId: DEFAULT_FACTORY_ID,
    managerName: '张玉兰',
  },
]

// 设定集 §2：机加 5 + 装配 6 + 表面与包装 3 = 14 条产线。
// ⚠️ 顺序即 L0 声明序，工单号推导（line/quality 两处 woOf 同式）依赖下标 ——
// 新增产线一律追加尾部，中间插入会让跨屏工单号整体漂移。
export const LINES: LineRef[] = [
  { id: 'LINE-WB-ROD-01', code: 'LINE-WB-ROD-01', name: '活塞杆一线', workshopId: 'WS-01' },
  { id: 'LINE-WB-ROD-02', code: 'LINE-WB-ROD-02', name: '活塞杆二线', workshopId: 'WS-01' },
  { id: 'LINE-WB-TUB-01', code: 'LINE-WB-TUB-01', name: '缸筒一线', workshopId: 'WS-01' },
  { id: 'LINE-WB-TUB-02', code: 'LINE-WB-TUB-02', name: '缸筒二线', workshopId: 'WS-01' },
  { id: 'LINE-WB-GRD-01', code: 'LINE-WB-GRD-01', name: '精磨线', workshopId: 'WS-01' },
  { id: 'LINE-WB-FA-01', code: 'LINE-WB-FA-01', name: '前减装配一线', workshopId: 'WS-02' },
  { id: 'LINE-WB-FA-02', code: 'LINE-WB-FA-02', name: '前减装配二线', workshopId: 'WS-02' },
  { id: 'LINE-WB-FA-03', code: 'LINE-WB-FA-03', name: '前减装配三线', workshopId: 'WS-02' },
  { id: 'LINE-WB-RA-01', code: 'LINE-WB-RA-01', name: '后减装配一线', workshopId: 'WS-02' },
  { id: 'LINE-WB-RA-02', code: 'LINE-WB-RA-02', name: '后减装配二线', workshopId: 'WS-02' },
  { id: 'LINE-WB-VA-01', code: 'LINE-WB-VA-01', name: '阀系预装线', workshopId: 'WS-02' },
  { id: 'LINE-WB-CT-01', code: 'LINE-WB-CT-01', name: '电泳涂装线', workshopId: 'WS-03' },
  { id: 'LINE-WB-TS-01', code: 'LINE-WB-TS-01', name: '性能检测线', workshopId: 'WS-03' },
  { id: 'LINE-WB-PK-01', code: 'LINE-WB-PK-01', name: '包装线', workshopId: 'WS-03' },
]

// 设定集 §2：14 个产线工作中心 + 3 个车间级辅助动力工作中心（承载空压机/冷干机）。
// 辅助工作中心的产线归属取该车间末道线 —— 与 L0 逐字一致（平台没有「公用工程」层）。
const WORK_CENTER_DEFS: { code: string; name: string; lineId: string }[] = [
  { code: 'WC-ROD-01', name: '活塞杆加工中心一线', lineId: 'LINE-WB-ROD-01' },
  { code: 'WC-ROD-02', name: '活塞杆加工中心二线', lineId: 'LINE-WB-ROD-02' },
  { code: 'WC-TUB-01', name: '缸筒加工中心一线', lineId: 'LINE-WB-TUB-01' },
  { code: 'WC-TUB-02', name: '缸筒加工中心二线', lineId: 'LINE-WB-TUB-02' },
  { code: 'WC-GRD-01', name: '精磨中心', lineId: 'LINE-WB-GRD-01' },
  { code: 'WC-FA-01', name: '前减装配中心一线', lineId: 'LINE-WB-FA-01' },
  { code: 'WC-FA-02', name: '前减装配中心二线', lineId: 'LINE-WB-FA-02' },
  { code: 'WC-FA-03', name: '前减装配中心三线', lineId: 'LINE-WB-FA-03' },
  { code: 'WC-RA-01', name: '后减装配中心一线', lineId: 'LINE-WB-RA-01' },
  { code: 'WC-RA-02', name: '后减装配中心二线', lineId: 'LINE-WB-RA-02' },
  { code: 'WC-VA-01', name: '阀系预装中心', lineId: 'LINE-WB-VA-01' },
  { code: 'WC-CT-01', name: '电泳涂装中心', lineId: 'LINE-WB-CT-01' },
  { code: 'WC-TS-01', name: '性能检测中心', lineId: 'LINE-WB-TS-01' },
  { code: 'WC-PK-01', name: '包装中心', lineId: 'LINE-WB-PK-01' },
  { code: 'WC-AUX-MC', name: '机加车间辅助动力', lineId: 'LINE-WB-GRD-01' },
  { code: 'WC-AUX-AS', name: '装配车间辅助动力', lineId: 'LINE-WB-VA-01' },
  { code: 'WC-AUX-SP', name: '表面与包装车间辅助动力', lineId: 'LINE-WB-PK-01' },
]

const workshopOfLine = new Map(LINES.map((l) => [l.id, l.workshopId]))

export const WORK_CENTERS: WorkCenterRef[] = WORK_CENTER_DEFS.map((wc) => ({
  id: wc.code,
  code: wc.code,
  name: wc.name,
  lineId: wc.lineId,
  workshopId: workshopOfLine.get(wc.lineId)!,
}))

// 设定集 §3 设备台账（46 台）：编码段 / 型号 / 工作中心归属与 L0 逐字一致。
// CNC 10 + 磨床 4 + 装配台 12 + 焊接机器人 3 + 电泳 3 + 试验台 4 + 包装 2 + 辅助 8 = 46。
const DEVICE_DEFS: { code: string; name: string; category: DeviceCategory; wc: string }[] = [
  { code: 'DEV-CNC-01', name: '数控车床 CK6150', category: 'cnc', wc: 'WC-ROD-01' },
  { code: 'DEV-CNC-02', name: '数控车床 CK6150', category: 'cnc', wc: 'WC-ROD-01' },
  { code: 'DEV-CNC-03', name: '数控车床 CK6150', category: 'cnc', wc: 'WC-ROD-01' },
  { code: 'DEV-CNC-04', name: '数控车床 CK6150', category: 'cnc', wc: 'WC-ROD-02' },
  { code: 'DEV-CNC-05', name: '数控车床 CK6150', category: 'cnc', wc: 'WC-ROD-02' },
  { code: 'DEV-CNC-06', name: '数控车床 CK6150', category: 'cnc', wc: 'WC-ROD-02' },
  { code: 'DEV-CNC-07', name: '立式加工中心 VMC-850', category: 'cnc', wc: 'WC-TUB-01' },
  { code: 'DEV-CNC-08', name: '立式加工中心 VMC-850', category: 'cnc', wc: 'WC-TUB-01' },
  { code: 'DEV-CNC-09', name: '立式加工中心 VMC-850', category: 'cnc', wc: 'WC-TUB-02' },
  { code: 'DEV-CNC-10', name: '立式加工中心 VMC-850', category: 'cnc', wc: 'WC-TUB-02' },
  { code: 'DEV-GRD-01', name: '数控外圆磨床 MK1332', category: 'grinder', wc: 'WC-GRD-01' },
  { code: 'DEV-GRD-02', name: '数控外圆磨床 MK1332', category: 'grinder', wc: 'WC-GRD-01' },
  { code: 'DEV-GRD-03', name: '数控外圆磨床 MK1332', category: 'grinder', wc: 'WC-GRD-01' },
  { code: 'DEV-GRD-04', name: '数控外圆磨床 MK1332', category: 'grinder', wc: 'WC-GRD-01' },
  {
    code: 'DEV-ASM-01',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-FA-01',
  },
  {
    code: 'DEV-ASM-02',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-FA-01',
  },
  {
    code: 'DEV-ASM-03',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-FA-02',
  },
  {
    code: 'DEV-ASM-04',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-FA-02',
  },
  {
    code: 'DEV-ASM-05',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-FA-03',
  },
  {
    code: 'DEV-ASM-06',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-FA-03',
  },
  {
    code: 'DEV-ASM-07',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-RA-01',
  },
  {
    code: 'DEV-ASM-08',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-RA-01',
  },
  {
    code: 'DEV-ASM-09',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-RA-02',
  },
  {
    code: 'DEV-ASM-10',
    name: '减振器装配台（气动压装）',
    category: 'assembly-station',
    wc: 'WC-RA-02',
  },
  {
    code: 'DEV-ASM-11',
    name: '阀系预装台（伺服压装）',
    category: 'assembly-station',
    wc: 'WC-VA-01',
  },
  {
    code: 'DEV-ASM-12',
    name: '阀系预装台（伺服压装）',
    category: 'assembly-station',
    wc: 'WC-VA-01',
  },
  { code: 'DEV-WLD-01', name: '六轴焊接机器人', category: 'welding-robot', wc: 'WC-TUB-01' },
  { code: 'DEV-WLD-02', name: '六轴焊接机器人', category: 'welding-robot', wc: 'WC-TUB-01' },
  { code: 'DEV-WLD-03', name: '六轴焊接机器人', category: 'welding-robot', wc: 'WC-TUB-02' },
  { code: 'DEV-CTG-01', name: '电泳前处理槽', category: 'coating', wc: 'WC-CT-01' },
  { code: 'DEV-CTG-02', name: '电泳槽', category: 'coating', wc: 'WC-CT-01' },
  { code: 'DEV-CTG-03', name: '固化炉', category: 'coating', wc: 'WC-CT-01' },
  { code: 'DEV-TST-01', name: '电液伺服试验台', category: 'test-bench', wc: 'WC-TS-01' },
  { code: 'DEV-TST-02', name: '电液伺服试验台', category: 'test-bench', wc: 'WC-TS-01' },
  { code: 'DEV-TST-03', name: '电液伺服试验台', category: 'test-bench', wc: 'WC-TS-01' },
  { code: 'DEV-TST-04', name: '电液伺服试验台', category: 'test-bench', wc: 'WC-TS-01' },
  { code: 'DEV-PKG-01', name: '自动装箱线', category: 'packaging-line', wc: 'WC-PK-01' },
  { code: 'DEV-PKG-02', name: '自动装箱线', category: 'packaging-line', wc: 'WC-PK-01' },
  { code: 'DEV-AUX-01', name: '螺杆空压机 SA-75', category: 'utility', wc: 'WC-AUX-MC' },
  { code: 'DEV-AUX-02', name: '螺杆空压机 SA-75', category: 'utility', wc: 'WC-AUX-MC' },
  { code: 'DEV-AUX-03', name: '冷冻式干燥机 CD-20', category: 'utility', wc: 'WC-AUX-MC' },
  { code: 'DEV-AUX-04', name: '螺杆空压机 SA-55', category: 'utility', wc: 'WC-AUX-AS' },
  { code: 'DEV-AUX-05', name: '螺杆空压机 SA-55', category: 'utility', wc: 'WC-AUX-AS' },
  { code: 'DEV-AUX-06', name: '冷冻式干燥机 CD-15', category: 'utility', wc: 'WC-AUX-AS' },
  { code: 'DEV-AUX-07', name: '螺杆空压机 SA-37', category: 'utility', wc: 'WC-AUX-SP' },
  { code: 'DEV-AUX-08', name: '冷冻式干燥机 CD-10', category: 'utility', wc: 'WC-AUX-SP' },
]

const workCenterById = new Map(WORK_CENTERS.map((wc) => [wc.id, wc]))

export const DEVICES: DeviceRef[] = DEVICE_DEFS.map((d) => {
  const wc = workCenterById.get(d.wc)!
  return {
    id: d.code,
    code: d.code,
    name: d.name,
    category: d.category,
    workCenterId: wc.id,
    lineId: wc.lineId,
    workshopId: wc.workshopId,
  }
})

/** 同型号设备在一条线上有多台 —— 大屏必须能一眼分辨是哪一台，故显示名带编码。 */
export function deviceLabel(d: Pick<DeviceRef, 'code' | 'name'>): string {
  return `${d.code} ${d.name}`
}

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
