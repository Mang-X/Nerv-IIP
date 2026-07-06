import type { ScreenKey } from '@/data/mock/scope'

export interface ScreenDef {
  key: ScreenKey
  route: string
  title: string
  desc: string
  icon: string // lucide-vue-next 图标名
  accent: 'cyan' | 'green' | 'amber' | 'red' | 'indigo'
}

export const SCREENS: ScreenDef[] = [
  { key: 'factory', route: '/factory', title: '工厂总览', desc: '全厂健康度 · 指挥中心', icon: 'LayoutDashboard', accent: 'cyan' },
  { key: 'equipment', route: '/equipment', title: '设备监控', desc: '设备健康 + 维修作战图', icon: 'Cpu', accent: 'green' },
  { key: 'line', route: '/line', title: '产线监控', desc: '现场作业状态监控屏', icon: 'Factory', accent: 'amber' },
]

/** 路径归属哪块大屏（含子路由如 /line/[id]）；非大屏路由返回 undefined。 */
export function screenForPath(path: string): ScreenKey | undefined {
  const hit = SCREENS.find((s) => path === s.route || path.startsWith(`${s.route}/`))
  return hit?.key
}
