export type ScreenKey = 'factory' | 'equipment' | 'line'

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

// 演示 persona：只证明"按权限进入 + 收窄车间/产线"，不写死真实策略；
// IAM 接入后本表由真实 claims 派生。见 spec §1.2。
export const PERSONAS: Persona[] = [
  {
    id: 'plant-admin',
    label: '全厂管理',
    factoryIds: ['F01', 'F02'],
    workshopIds: 'all',
    lineIds: 'all',
    allowedScreens: ['factory', 'equipment', 'line'],
  },
  {
    id: 'workshop-lead',
    label: '电池车间线长',
    factoryIds: ['F01'],
    workshopIds: ['WS-BATTERY'],
    lineIds: 'all',
    allowedScreens: ['line'],
  },
]

export const DEFAULT_PERSONA_ID = 'plant-admin'
