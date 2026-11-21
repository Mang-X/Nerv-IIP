import { runNodeSource, type SdkCallResult } from './fulfillmentErrors'

// 列表按包含关系搜索。逐页核对完整来源键，并给单次打开的时间线设定读取上限。
const pageSize = 50

const maxPages = 4

export async function readCandidatePages<T>(
  read: (
    skip: number,
    take: number,
  ) => Promise<
    SdkCallResult<{ success?: boolean; data?: { items?: T[] | null; total?: number } | null }>
  >,
) {
  const items: T[] = []
  for (let page = 0; page < maxPages; page++) {
    const result = await runNodeSource(async () => {
      const { data, error, response } = await read(page * pageSize, pageSize)
      return { data: data?.data, error: data?.success === false ? data : error, response }
    })
    const rows = result?.items ?? []
    items.push(...rows)
    if (rows.length < pageSize || (result?.total != null && items.length >= result.total))
      return { items, limited: false }
  }
  return { items, limited: true }
}
