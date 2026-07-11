import type { ScreenKey } from '@/data/mock/scope'

export interface ScreenDef {
  key: ScreenKey
  route: string
  title: string
  desc: string
  icon: string // lucide-vue-next 图标名
}

export const SCREENS: ScreenDef[] = [
  { key: 'factory', route: '/factory', title: '工厂总览', desc: '全厂健康度 · 指挥中心', icon: 'LayoutDashboard' },
  { key: 'equipment', route: '/equipment', title: '设备监控', desc: '设备健康 + 维修作战图', icon: 'Cpu' },
  { key: 'line', route: '/line', title: '产线监控', desc: '现场作业状态监控屏', icon: 'Factory' },
  { key: 'workshop', route: '/workshop', title: '车间总览', desc: '车间主任当班作战室', icon: 'Building2' },
  { key: 'warehouse', route: '/warehouse', title: '仓储物流', desc: 'WMS 作业指挥 · 积压与告警', icon: 'Warehouse' },
  { key: 'quality', route: '/quality', title: '质量看板', desc: '质量健康度 + 待办闭环', icon: 'ShieldCheck' },
]

/** 路径归属哪块大屏（含子路由如 /line/[id]）；非大屏路由返回 undefined。 */
export function screenForPath(path: string): ScreenKey | undefined {
  const hit = SCREENS.find((s) => path === s.route || path.startsWith(`${s.route}/`))
  return hit?.key
}
