export type LineSideInventoryAgeCompleteness = 'complete' | 'partial' | 'unavailable'

export interface LineSideInventoryAgeLike {
  ageDays?: number | null
  ageCompleteness?: LineSideInventoryAgeCompleteness
}

export interface LineSideInventoryAgePresentation {
  detail: string
  label: string
  tone: 'neutral' | 'success' | 'warning'
}

export function lineSideInventoryAgePresentation(
  item: LineSideInventoryAgeLike,
): LineSideInventoryAgePresentation {
  if (item.ageCompleteness === 'complete' && item.ageDays != null) {
    return {
      detail: `${item.ageDays} 天`,
      label: '账龄完整',
      tone: 'success',
    }
  }

  if (item.ageCompleteness === 'partial' && item.ageDays != null) {
    return {
      detail: `${item.ageDays} 天（部分批次缺少生产日期）`,
      label: '账龄部分可知',
      tone: 'warning',
    }
  }

  return {
    detail: '账龄未知（批次缺少生产日期）',
    label: '账龄未知',
    tone: 'neutral',
  }
}
