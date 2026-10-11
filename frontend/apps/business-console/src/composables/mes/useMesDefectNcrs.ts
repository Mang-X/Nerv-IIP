import {
  listBusinessConsoleQualityNcrs,
  type BusinessConsoleQualityNcrItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext, type BusinessContextFields } from '../businessContextBinding'
import { businessReadState } from '../businessReadState'

interface DefectNcrSource extends BusinessContextFields {
  defectNos: string[]
}

/** Quality 来源单据是 MES 不良编号；keyword 只缩小候选集，不能证明关联。 */
export function useMesDefectNcrs(source: () => DefectNcrSource) {
  const query = useQuery(() => {
    const { organizationId, environmentId, defectNos } = source()
    return {
      key: ['mes-defect-ncrs', organizationId, environmentId, ...defectNos],
      enabled: hasBusinessContext({ organizationId, environmentId }) && defectNos.length > 0,
      query: async () => {
        const entries = await Promise.all(
          defectNos.map(async (defectNo) => {
            let skip = 0
            let match: BusinessConsoleQualityNcrItem | undefined
            let total: number
            do {
              const response = await listBusinessConsoleQualityNcrs({
                query: { organizationId, environmentId, keyword: defectNo, skip, take: 100 },
                throwOnError: true,
              })
              const data = response.data?.data
              if (response.data?.success !== true || !data?.items || data.total === undefined) {
                throw new Error('NCR 关联读取失败，请刷新重试。')
              }
              total = data.total
              match = data.items.find(
                (ncr) => ncr.sourceType === 'in-process' && ncr.sourceDocumentId === defectNo,
              )
              if (data.items.length === 0 && skip < total) {
                throw new Error('NCR 关联读取失败，请刷新重试。')
              }
              skip += data.items.length
            } while (!match && skip < total)
            return [defectNo, match ?? null] as const
          }),
        )
        return { success: true, ncrs: Object.fromEntries(entries) }
      },
    }
  })
  return {
    ncrs: computed(() => query.data.value?.ncrs ?? {}),
    state: businessReadState(
      query,
      () => hasBusinessContext(source()) && source().defectNos.length > 0,
    ),
    pending: query.isLoading,
    error: query.error,
    refresh: () =>
      hasBusinessContext(source()) && source().defectNos.length > 0
        ? query.refetch()
        : Promise.resolve(),
  }
}
