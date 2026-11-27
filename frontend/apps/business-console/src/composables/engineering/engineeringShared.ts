import { type UseQueryEntry } from '@pinia/colada'

export const DEFAULT_TAKE = 100

/** MBOM / 工艺路线选择器只取后端真枚举 `Published`（不是 `Released`）。 */
export const PUBLISHED = 'Published'

export function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

export function unwrapItems<T>(
  envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined,
): T[] {
  if (!envelope?.success) return []
  return envelope.data?.items ?? []
}

export function unwrapTotal(
  envelope: { success?: boolean; data?: { total?: number } | null } | undefined,
) {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}

/** 解包 get-by-id 单体响应（query option 返回 `{ success, data }` envelope）。 */
export function unwrapDetail<T>(
  envelope: { success?: boolean; data?: T | null } | undefined,
): T | undefined {
  if (!envelope?.success) return undefined
  return envelope.data ?? undefined
}

export async function runQueryOption<T>(
  options: { query: (context: never) => Promise<T> },
  signal?: AbortSignal,
): Promise<T> {
  return options.query({ signal } as never)
}

export async function loadEngineeringCatalogPages<T>(
  fetchPage: (skip: number) => Promise<{ items: T[]; total: number }>,
  keyOf: (item: T) => string,
  signal: AbortSignal,
) {
  const maxPages = 50
  const byKey = new Map<string, T>()
  let total = 0
  let skip = 0
  for (let page = 0; page < maxPages; page += 1) {
    signal.throwIfAborted()
    const batch = await fetchPage(skip)
    signal.throwIfAborted()
    total = batch.total
    const previousSize = byKey.size
    for (const item of batch.items) byKey.set(keyOf(item), item)
    skip += batch.items.length
    if (byKey.size === previousSize || skip >= total) break
  }
  return { items: [...byKey.values()], total }
}

export function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some(
      (part) => typeof part === 'object' && part !== null && '_id' in part && part._id === id,
    )
  }
}

export function ignoreBackgroundError(_error: unknown) {}
