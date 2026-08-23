import type { Persona } from '@/data/contracts/scope'

// 演示 persona：只证明"按权限进入 + 收窄车间/产线"，不写死真实策略；
// IAM 接入后本表由真实 claims 派生。见 spec §1.2。
export const PERSONAS: Persona[] = [
  {
    id: 'plant-admin',
    label: '全厂管理',
    factoryIds: ['SITE-001'],
    workshopIds: 'all',
    lineIds: 'all',
    allowedScreens: ['factory', 'equipment', 'line', 'workshop', 'warehouse', 'quality'],
  },
  {
    // 设定集 §5：宁沪减振没有「线长」岗位，车间层的负责人是车间主任（EMP-001..003）
    id: 'workshop-lead',
    label: '装配车间主任',
    factoryIds: ['SITE-001'],
    workshopIds: ['WS-02'],
    lineIds: 'all',
    allowedScreens: ['line', 'workshop'],
  },
]

export const DEFAULT_PERSONA_ID = 'plant-admin'
