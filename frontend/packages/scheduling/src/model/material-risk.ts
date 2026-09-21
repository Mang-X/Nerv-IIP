import type { MaterialRisk } from './types'

/** 同一物料风险事实在卡片、tooltip 与详情的统一显示口径。 */
export function materialReadyLabel(risk?: MaterialRisk): string | undefined {
  if (!risk?.materialReadyUtc) return undefined
  const date = new Date(risk.materialReadyUtc)
  if (Number.isNaN(date.getTime())) return undefined

  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `预计到料 ${month}-${day}`
}
