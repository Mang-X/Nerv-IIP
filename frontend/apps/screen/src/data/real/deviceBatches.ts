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
