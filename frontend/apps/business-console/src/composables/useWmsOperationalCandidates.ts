import {
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
  type BusinessConsoleWmsOperationalCandidatesEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, watch } from 'vue'

export type WmsOperationalCandidateKind = 'receipt' | 'shipment' | 'count'

export interface WmsOperationalCandidateFilters {
  organizationId: string
  environmentId: string
  scopeKind?: string
  scopeId?: string
  status?: string
  skuCode?: string
  locationCode?: string
  lotNo?: string
}

export function useWmsOperationalCandidates(
  kind: WmsOperationalCandidateKind,
  filters: WmsOperationalCandidateFilters,
) {
  const ready = computed(
    () =>
      Boolean(filters.organizationId.trim()) &&
      Boolean(filters.environmentId.trim()) &&
      Boolean(filters.scopeKind?.trim()) &&
      Boolean(filters.scopeId?.trim()),
  )
  const queryInput = () => ({
    query: {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      scopeKind: filters.scopeKind!,
      scopeId: filters.scopeId!,
      take: 100,
      ...(filters.skuCode?.trim() ? { skuCode: filters.skuCode.trim() } : {}),
      ...(filters.locationCode?.trim() ? { locationCode: filters.locationCode.trim() } : {}),
    },
  })
  const candidatesQuery = useQuery(() => ({
    ...(kind === 'receipt'
      ? listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions(queryInput())
      : kind === 'shipment'
        ? listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions(queryInput())
        : listBusinessConsoleWmsCountOperationalCandidatesQueryOptions(queryInput())),
    enabled: ready.value,
  }))
  const envelope = computed(
    () => candidatesQuery.data.value as BusinessConsoleWmsOperationalCandidatesEnvelope | undefined,
  )
  const response = computed(() =>
    envelope.value?.success === true ? envelope.value.data : undefined,
  )

  watch(
    [() => filters.scopeKind, () => filters.scopeId, () => filters.status],
    () => {
      filters.locationCode = undefined
      filters.lotNo = undefined
    },
    { flush: 'sync' },
  )

  const locationOptions = computed(() =>
    (response.value?.locations ?? []).flatMap((candidate) => {
      const value = candidate.locationCode?.trim()
      if (!value) return []
      const skuHint = candidate.skuCodes?.filter(Boolean).join('、')
      return [
        {
          value,
          label: value,
          hint: [candidate.siteCode, skuHint].filter(Boolean).join(' · ') || undefined,
        },
      ]
    }),
  )
  const lotOptions = computed(() => {
    if (kind === 'count') return []
    const locationCode = filters.locationCode?.trim()
    const skuCode = filters.skuCode?.trim()
    return (response.value?.lots ?? []).flatMap((candidate) => {
      const value = candidate.lotNo?.trim()
      if (!value) return []
      if (locationCode && !(candidate.locationCodes ?? []).includes(locationCode)) return []
      if (skuCode && candidate.skuCode !== skuCode) return []
      return [
        {
          value,
          label: value,
          hint: [candidate.skuCode, ...(candidate.locationCodes ?? [])].filter(Boolean).join(' · '),
        },
      ]
    })
  })

  return {
    locationOptions,
    lotOptions,
    sourceLabel: computed(() => '当前范围仓储作业记录候选'),
    sourceKind: computed(() => response.value?.sourceKind),
    asOfUtc: computed(() => response.value?.asOfUtc),
    freshnessUtc: computed(() => response.value?.freshnessUtc),
    truncated: computed(() => response.value?.truncated === true),
    pending: candidatesQuery.isLoading,
    error: candidatesQuery.error,
    refresh: () => (ready.value ? candidatesQuery.refetch() : Promise.resolve()),
  }
}
