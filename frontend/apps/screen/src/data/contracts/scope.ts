// 大屏访问范围契约：有哪几块屏、一个访问主体能看到什么。
//
// 本文件只放**类型**：当前由 mock persona 表（`data/mock/scope.ts`）实现，
// 接入真实 IAM 后改由 claims 派生，消费方（launcher / access store / 各屏）不动。
// 见 spec §1.2。

export type ScreenKey = 'factory' | 'equipment' | 'line' | 'workshop' | 'warehouse' | 'quality'

export interface Persona {
  id: string
  label: string
  factoryIds: string[]
  /** 'all' = 该工厂全部车间；否则白名单 workshopId */
  workshopIds: string[] | 'all'
  /** 'all' = 可见车间下全部产线；否则白名单 lineId */
  lineIds: string[] | 'all'
  allowedScreens: ScreenKey[]
}
