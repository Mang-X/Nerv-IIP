// 参数类型 → 语义色（图表/数值统一按类型着色；超限 tone 覆盖为红/黄）
import type { ParamKind } from '@/data/contracts/equipment'

export const KIND_COLOR: Record<ParamKind, string> = {
  temp: 'var(--sb-amber)',
  energy: 'var(--sb-amber)',
  pressure: 'var(--sb-cyan)',
  flow: 'var(--sb-cyan)',
  torque: 'var(--sb-cyan)',
  speed: 'var(--sb-green)',
  level: 'var(--sb-green)',
  cycle: 'var(--sb-green)',
  current: 'var(--sb-indigo)',
  vibration: 'var(--sb-indigo)',
}

export function paramColor(kind: ParamKind, tone?: 'warn' | 'bad'): string {
  if (tone === 'bad') return 'var(--sb-red)'
  if (tone === 'warn') return 'var(--sb-amber)'
  return KIND_COLOR[kind]
}
