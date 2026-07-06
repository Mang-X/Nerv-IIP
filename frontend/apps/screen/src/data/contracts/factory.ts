// 工厂总览大屏数据契约（MAN-314，spec §二）。
// 🟠 车间维度聚合当前无真实端点（见 #570），mock 先按此形状产出；接入后仅换 fetchers/factory.ts。

export interface OeeItem {
  label: string
  value: number
}

export type WorkshopHealth = 'red' | 'yellow' | 'green'

export interface FactoryKpis {
  /** 今日全厂达成率 0–100 🟡 */
  achievement: number
  /** 在产工单 ✅ */
  wipOrders: number
  /** 超期/风险工单 🟡 */
  riskOrders: number
  /** 未恢复告警（总）✅ */
  openAlarms: number
  /** 其中 critical 🟡 */
  criticalAlarms: number
  /** Open 停机 ✅ */
  openDowntime: number
  /** Open NCR ✅ */
  openNcr: number
}

/** 车间状态矩阵单元（厂长 3 秒判绿/黄/红）。 */
export interface WorkshopCell {
  id: string
  name: string
  /** 车间主管 ✅ */
  manager: string
  /** 健康度：合成规则见 mock/factory.ts composeHealth（红卡置顶） */
  health: WorkshopHealth
  stateLabel: string
  /** 在产工单/工序 🟡 */
  wip: number
  planQty: number
  actualQty: number
  /** 达成率 0–100 🟡 */
  rate: number
  /** 超期工单 🟡 */
  overdue: number
  /** 未恢复 critical 告警 🟠 设备→车间映射占位 */
  critAlarms: number
  /** Open 停机 🟡 */
  openDowntime: number
}

export interface FeedItem {
  id: string
  level: 'critical' | 'warning' | 'info'
  text: string
  time: string
}

export interface FactoryOverview {
  factoryId: string
  kpis: FactoryKpis
  /** 健康度红卡在前 */
  workshops: WorkshopCell[]
  /** 可用率（≈实测）+ 性能率/良品率（🟠 占位）；综合 OEE ≈ 可用率，渲染必须诚实标注 */
  oee: OeeItem[]
  /** 实时告警流 */
  alarms: FeedItem[]
  /** 停机事件流 */
  downtimes: FeedItem[]
}
