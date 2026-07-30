import {
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
  type BusinessConsoleWmsOperationalCandidatesEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, toValue, watch, type MaybeRefOrGetter } from 'vue'

export type WmsOperationalCandidateKind = 'receipt' | 'shipment' | 'count'

export interface WmsOperationalCandidateFilters {
  status?: string
  skuCode?: string
  locationCode?: string
  lotNo?: string
}

export interface UseWmsOperationalCandidatesOptions {
  organizationId: MaybeRefOrGetter<string>
  environmentId: MaybeRefOrGetter<string>
  scopeKey: MaybeRefOrGetter<string | undefined>
  filters: WmsOperationalCandidateFilters
}

export function useWmsOperationalCandidates(
  kind: WmsOperationalCandidateKind,
  options: UseWmsOperationalCandidatesOptions,
) {
  const selectedScope = computed(() => {
    const [scopeKind, ...scopeIdParts] = (toValue(options.scopeKey) ?? '').split(':')
    const scopeId = scopeIdParts.join(':').trim()
    if (!['self', 'work-pool', 'site'].includes(scopeKind) || !scopeId) return undefined
    return { scopeKind, scopeId }
  })
  const ready = computed(
    () =>
      Boolean(toValue(options.organizationId).trim()) &&
      Boolean(toValue(options.environmentId).trim()) &&
      selectedScope.value !== undefined,
  )
  const queryInput = () => ({
    query: {
      organizationId: toValue(options.organizationId),
      environmentId: toValue(options.environmentId),
      scopeKind: selectedScope.value?.scopeKind ?? '',
      scopeId: selectedScope.value?.scopeId ?? '',
      take: 100,
      ...(options.filters.skuCode?.trim() ? { skuCode: options.filters.skuCode.trim() } : {}),
      ...(options.filters.locationCode?.trim()
        ? { locationCode: options.filters.locationCode.trim() }
        : {}),
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
    [() => toValue(options.scopeKey), () => options.filters.status],
    () => {
      options.filters.locationCode = undefined
      options.filters.lotNo = undefined
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
    const locationCode = options.filters.locationCode?.trim()
    const skuCode = options.filters.skuCode?.trim()
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
    sourceKind: computed(() => response.value?.sourceKind ?? undefined),
    asOfUtc: computed(() => response.value?.asOfUtc ?? undefined),
    freshnessUtc: computed(() => response.value?.freshnessUtc ?? undefined),
    truncated: computed(() => response.value?.truncated === true),
    pending: candidatesQuery.isLoading,
    error: candidatesQuery.error,
    refresh: () => (ready.value ? candidatesQuery.refetch() : Promise.resolve()),
  }
}
