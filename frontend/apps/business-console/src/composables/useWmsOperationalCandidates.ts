import {
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
  type BusinessConsoleWmsOperationalCandidatesEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, shallowRef, watch } from 'vue'

export type WmsOperationalCandidateKind = 'receipt' | 'shipment' | 'count'

export interface WmsOperationalCandidateFilters {
  organizationId: string
  environmentId: string
  scopeKind?: string
  scopeId?: string
  status?: string
  skuCode?: string
  siteCode?: string
  locationCode?: string
  lotNo?: string
}

export function useWmsOperationalCandidates(
  kind: WmsOperationalCandidateKind,
  filters: WmsOperationalCandidateFilters,
) {
  const searchKeyword = shallowRef('')
  const debouncedKeyword = refDebounced(searchKeyword, 300)
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
      ...(debouncedKeyword.value.trim() ? { keyword: debouncedKeyword.value.trim() } : {}),
      ...(filters.skuCode?.trim() ? { skuCode: filters.skuCode.trim() } : {}),
      ...(filters.siteCode?.trim() ? { siteCode: filters.siteCode.trim() } : {}),
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
  const response = computed(() => {
    const data = envelope.value?.success === true ? envelope.value.data : undefined
    if (
      !ready.value ||
      data?.scopeKind !== filters.scopeKind?.trim() ||
      data?.scopeId !== filters.scopeId?.trim()
    ) {
      return undefined
    }
    return data
  })

  watch(
    [
      () => filters.organizationId,
      () => filters.environmentId,
      () => filters.scopeKind,
      () => filters.scopeId,
      () => filters.status,
    ],
    () => {
      filters.locationCode = undefined
      filters.lotNo = undefined
      searchKeyword.value = ''
    },
    { flush: 'sync' },
  )
  watch(
    () => filters.siteCode,
    () => {
      filters.locationCode = undefined
      filters.lotNo = undefined
    },
    { flush: 'sync' },
  )
  watch(
    () => filters.skuCode,
    () => {
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
  watch(
    response,
    (current) => {
      if (!current || candidatesQuery.isLoading.value || candidatesQuery.error.value) return
      if (
        filters.locationCode &&
        !locationOptions.value.some((option) => option.value === filters.locationCode)
      ) {
        filters.locationCode = undefined
        filters.lotNo = undefined
        return
      }
      if (
        kind !== 'count' &&
        filters.lotNo &&
        !lotOptions.value.some((option) => option.value === filters.lotNo)
      ) {
        filters.lotNo = undefined
      }
    },
    { flush: 'sync' },
  )

  return {
    ready,
    searchKeyword,
    locationOptions,
    lotOptions,
    sourceLabel: computed(() => '当前范围仓储作业记录候选'),
    asOfUtc: computed(() => response.value?.asOfUtc),
    freshnessUtc: computed(() => response.value?.freshnessUtc),
    truncated: computed(() => response.value?.truncated === true),
    pending: candidatesQuery.isLoading,
    error: computed(() => (ready.value ? candidatesQuery.error.value : undefined)),
    refresh: () => (ready.value ? candidatesQuery.refetch() : Promise.resolve()),
  }
}
