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

export async function runQueryOption<T>(options: {
  query: (context: never) => Promise<T>
}): Promise<T> {
  return options.query({} as never)
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
