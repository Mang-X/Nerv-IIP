// 参数类型 → 语义色（图表/数值统一按类型着色；超限 tone 覆盖为红/黄）
import type { ParamKind } from '@/data/contracts/equipment'

export const KIND_COLOR: Record<ParamKind, string> = {
  temp: 'var(--nv-scr-amber)',
  energy: 'var(--nv-scr-amber)',
  pressure: 'var(--nv-scr-cyan)',
  flow: 'var(--nv-scr-cyan)',
  torque: 'var(--nv-scr-cyan)',
  speed: 'var(--nv-scr-green)',
  level: 'var(--nv-scr-green)',
  cycle: 'var(--nv-scr-green)',
  current: 'var(--nv-scr-indigo)',
  vibration: 'var(--nv-scr-indigo)',
}

export function paramColor(kind: ParamKind, tone?: 'warn' | 'bad'): string {
  if (tone === 'bad') return 'var(--nv-scr-red)'
  if (tone === 'warn') return 'var(--nv-scr-amber)'
  return KIND_COLOR[kind]
}
