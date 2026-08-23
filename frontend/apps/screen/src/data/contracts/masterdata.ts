// 工厂→车间→产线→工作中心→设备 的主数据引用类型。
// 真实平台无 workshop/line 聚合维度，最细到 WorkCenter/Device；前端聚合按这套形状消费。
// 见 spec §1.1「数据现实」。
//
// 本文件只放**类型**：mock 与 real 两侧的主数据实现都实现它，
// 页面/store/fetcher 一律从这里取类型，不反向依赖 `data/mock/`。

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
