import {
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  type BusinessConsoleMesTraceabilityEnvelope,
  type BusinessConsoleMesTraceabilityResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { businessReadState } from '../shared/businessReadState'
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

  const activeQuery = computed(() => {
    if (filters.mode === 'batch') return batchQuery
    if (filters.mode === 'material-lot') return materialLotQuery
    return workOrderQuery
  })
  const state = businessReadState(
    {
      data: activeEnvelope,
      error: computed(() => activeQuery.value.error.value),
      isLoading: computed(() => activeQuery.value.isLoading.value),
    },
    () => workOrderEnabled.value || batchEnabled.value || materialLotEnabled.value,
  )

  return {
    filters,
    traceabilityState: state,
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
    traceabilityError: computed(() =>
      state.value === 'error'
        ? (activeQuery.value.error.value ?? new Error('追溯数据读取失败'))
        : undefined,
    ),
    traceabilityPending: computed(
      () => state.value === 'loading' || activeQuery.value.isLoading.value,
    ),
  }
}
