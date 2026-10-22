export function lastPageForTotal(total: number, pageSize: number): number {
  const safeTotal = Number.isFinite(total) ? Math.max(0, total) : 0
  const safePageSize = Number.isFinite(pageSize) ? Math.max(1, Math.floor(pageSize)) : 1
  return Math.max(1, Math.ceil(safeTotal / safePageSize))
}
