import {
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  type BusinessConsoleMesTraceabilityEnvelope,
  type BusinessConsoleMesTraceabilityResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { hasBusinessContext } from '../shared/businessContextBinding'
import { defaultTraceabilityFilters, isNonEmpty, toContextQuery, unwrapData } from './mesShared'

export function useMesTraceability() {
  const filters = defaultTraceabilityFilters()
  const workOrderEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      filters.mode === 'work-order' &&
      isNonEmpty(filters.workOrderId),
  )
  const batchEnabled = computed(
    () =>
      hasBusinessContext(filters) && filters.mode === 'batch' && isNonEmpty(filters.batchOrSerial),
  )
  const materialLotEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      filters.mode === 'material-lot' &&
      isNonEmpty(filters.materialLotId),
  )
  const workOrderQuery = useQuery(() => ({
    ...getBusinessConsoleMesWorkOrderTraceabilityQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    }),
    enabled: workOrderEnabled.value,
  }))
  const batchQuery = useQuery(() => ({
    ...getBusinessConsoleMesBatchTraceabilityQueryOptions({
      path: { batchOrSerial: filters.batchOrSerial },
      query: toContextQuery(filters),
    }),
    enabled: batchEnabled.value,
  }))
  const materialLotQuery = useQuery(() => ({
    ...getBusinessConsoleMesMaterialLotTraceabilityQueryOptions({
      path: { materialLotId: filters.materialLotId },
      query: toContextQuery(filters),
    }),
    enabled: materialLotEnabled.value,
  }))

  const activeEnvelope = computed(() => {
    if (filters.mode === 'batch') return batchQuery.data.value
    if (filters.mode === 'material-lot') return materialLotQuery.data.value
    return workOrderQuery.data.value
  })

  return {
    filters,
    refreshTraceability: () => {
      if (filters.mode === 'batch') {
        return batchEnabled.value ? batchQuery.refetch() : Promise.resolve()
      }
      if (filters.mode === 'material-lot') {
        return materialLotEnabled.value ? materialLotQuery.refetch() : Promise.resolve()
      }
      return workOrderEnabled.value ? workOrderQuery.refetch() : Promise.resolve()
    },
    traceability: computed<BusinessConsoleMesTraceabilityResponse | undefined>(() =>
      unwrapData<BusinessConsoleMesTraceabilityResponse, BusinessConsoleMesTraceabilityEnvelope>(
        activeEnvelope.value,
      ),
    ),
    traceabilityError: computed(() => {
      if (filters.mode === 'batch') return batchQuery.error.value
      if (filters.mode === 'material-lot') return materialLotQuery.error.value
      return workOrderQuery.error.value
    }),
    traceabilityPending: computed(() => {
      if (filters.mode === 'batch') return batchQuery.isLoading.value
      if (filters.mode === 'material-lot') return materialLotQuery.isLoading.value
      return workOrderQuery.isLoading.value
    }),
  }
}
