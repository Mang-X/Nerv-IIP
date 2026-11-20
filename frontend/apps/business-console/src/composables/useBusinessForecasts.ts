import {
  createOrUpdateBusinessConsolePlanningForecastMutationOptions,
  listBusinessConsolePlanningForecastsQueryOptions,
  type BusinessConsoleCreateOrUpdateForecastInputRequest,
  type BusinessConsoleForecastInputItem,
  type BusinessConsoleForecastInputListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useBusinessContextStore } from '@/stores/businessContext'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'
import { assertEnvelopeSuccess } from './serviceEnvelope'

export interface ForecastFilters {
  organizationId: string
  environmentId: string
  skuCode: string
  siteCode: string
  fromDate: string
  toDate: string
}

export type ForecastForm = BusinessConsoleCreateOrUpdateForecastInputRequest

function unwrapItems(
  envelope: BusinessConsoleForecastInputListEnvelope | undefined,
): BusinessConsoleForecastInputItem[] {
  if (!envelope?.success) return []
  return envelope.data?.items ?? []
}

export function useBusinessForecasts() {
  const businessContext = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive<ForecastFilters>({
      organizationId: businessContext.organizationId,
      environmentId: businessContext.environmentId,
      skuCode: '',
      siteCode: '',
      fromDate: '',
      toDate: '',
    }),
  )

  const auth = useAuthStore()
  const canQuery = computed(
    () =>
      hasBusinessContext(filters) &&
      auth.principal?.permissionCodes?.includes(P.planningDemandsRead),
  )
  const forecastsQuery = useQuery(() => ({
    ...listBusinessConsolePlanningForecastsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skuCode: filters.skuCode === 'all' ? undefined : filters.skuCode.trim() || undefined,
        siteCode: filters.siteCode === 'all' ? undefined : filters.siteCode.trim() || undefined,
        fromDate: filters.fromDate || undefined,
        toDate: filters.toDate || undefined,
      },
    }),
    enabled: !!canQuery.value,
  }))
  const saveMutation = useMutation(createOrUpdateBusinessConsolePlanningForecastMutationOptions())

  async function refreshForecasts() {
    if (canQuery.value) await forecastsQuery.refetch()
  }
  return {
    filters,
    forecasts: computed(() =>
      unwrapItems(
        forecastsQuery.data.value as BusinessConsoleForecastInputListEnvelope | undefined,
      ),
    ),
    forecastsError: forecastsQuery.error,
    forecastsPending: forecastsQuery.isLoading,
    refreshForecasts,
    saveForecast: async (form: ForecastForm) => {
      const envelope = await saveMutation.mutateAsync({ body: form })
      const result = assertEnvelopeSuccess(envelope, '保存预测失败，请稍后重试。')
      await refreshForecasts()
      return result
    },
    saveForecastError: saveMutation.error,
    saveForecastPending: saveMutation.isLoading,
  }
}
