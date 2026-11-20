// BusinessConsoleEquipmentOverviewRequestValidator limits both the CSV and its item count.
export function deviceIdBatches(ids: readonly string[]): string[] {
  const batches: string[] = []
  let batch = ''
  let count = 0
  for (const id of ids) {
    if (!id || id.includes(',') || id.length > 1000) throw new Error('设备编号不符合查询契约')
    if (count === 50 || batch.length + id.length + 1 > 1000) {
      if (count) batches.push(batch)
      batch = ''
      count = 0
    }
    batch = batch ? batch + ',' + id : id
    count++
  }
  if (count) batches.push(batch)
  return batches
}

export function coveredDevices<T extends { deviceAssetId?: string | null; isSourceFresh?: boolean | null; activeAlarmCount?: number | null }>(ids: string, rows: T[]) {
  const allowed = new Set(ids.split(','))
  const seen = new Set<string>()
  const valid = new Map<string,T>()
  for (const row of rows) {
    const id = row.deviceAssetId
    if (!id || !allowed.has(id)) continue
    if (seen.has(id)) valid.delete(id)
    else if (typeof row.isSourceFresh === 'boolean' && typeof row.activeAlarmCount === 'number' && Number.isSafeInteger(row.activeAlarmCount) && row.activeAlarmCount >= 0) valid.set(id,row)
    seen.add(id)
  }
  return [...valid.values()]
}
