export function readScreenPayload<T>(result: {
  data?: { success?: boolean; data?: T | null } | null
}): T {
  if (result.data?.success !== true || result.data.data == null) throw new Error('数据响应不完整')
  return result.data.data
}

export function readRows<T>(rows: T[] | null | undefined): T[] {
  if (!Array.isArray(rows)) throw new Error('数据列表不完整')
  return rows
}
