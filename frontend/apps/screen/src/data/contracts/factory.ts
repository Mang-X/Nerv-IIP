// 工厂总览大屏数据契约。字段对齐 @nerv-iip/api-client business-console；
// 🟠 车间维度聚合当前无真实端点（见 #570），mock 先按此形状产出。

export interface KpiItem {
  label: string
  value: number
  unit?: string
  delta?: string
  spark?: number[]
}

export interface WorkshopStatus {
  name: string
  state: string
  label: string
  tone: 'run' | 'idle' | 'alarm'
  plan: string
  actual: string
  rate: string
  downtime: string
}

export interface OeeItem {
  label: string
  value: number
}

export interface AlarmItem {
  id: string
  level: 'critical' | 'warning'
  text: string
  time: string
}

export interface FactoryOverview {
  kpis: KpiItem[]
  workshops: WorkshopStatus[]
  oee: OeeItem[]
  alarms: AlarmItem[]
}
