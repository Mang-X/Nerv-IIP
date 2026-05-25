export interface VisibleRowRange {
  startIndex: number
  endIndex: number
}

export function calculateVisibleRowRange(options: {
  scrollTop: number
  viewportHeight: number
  rowHeight: number
  rowCount: number
  overscan?: number
}): VisibleRowRange {
  const overscan = options.overscan ?? 2
  const first = Math.floor(options.scrollTop / options.rowHeight)
  const visibleCount = Math.ceil(options.viewportHeight / options.rowHeight)

  return {
    startIndex: Math.max(0, first - overscan),
    endIndex: Math.min(options.rowCount, first + visibleCount + overscan),
  }
}
