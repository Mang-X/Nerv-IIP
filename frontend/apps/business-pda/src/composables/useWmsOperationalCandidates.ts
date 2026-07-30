import {
  listBusinessConsoleWmsCountOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsReceiptOperationalCandidatesQueryOptions,
  listBusinessConsoleWmsShipmentOperationalCandidatesQueryOptions,
  type BusinessConsoleWmsOperationalCandidatesEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { refDebounced } from '@vueuse/core'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'

export type WmsOperationalCandidateKind = 'receipt' | 'shipment' | 'count'
export type WmsOperationalCandidateScanTarget = 'location' | 'lot'

export interface WmsOperationalCandidateFilters {
  status?: string
  skuCode?: string
  locationCode?: string
  lotNo?: string
}

export interface UseWmsOperationalCandidatesOptions {
  organizationId: MaybeRefOrGetter<string>
  environmentId: MaybeRefOrGetter<string>
  scopeKind: MaybeRefOrGetter<string | undefined>
  scopeId: MaybeRefOrGetter<string | undefined>
  scopeReady: MaybeRefOrGetter<boolean>
  filters: WmsOperationalCandidateFilters
}

export function useWmsOperationalCandidates(
  kind: WmsOperationalCandidateKind,
  options: UseWmsOperationalCandidatesOptions,
) {
  const searchKeyword = shallowRef('')
  const explicitScanOverrides = shallowRef<
    Partial<Record<WmsOperationalCandidateScanTarget, string>>
  >({})
  const debouncedKeyword = refDebounced(searchKeyword, 300)
  const selectedScope = computed(() => ({
    scopeKind: toValue(options.scopeKind)?.trim() ?? '',
    scopeId: toValue(options.scopeId)?.trim() ?? '',
  }))
  const ready = computed(
    () =>
      toValue(options.scopeReady) === true &&
      Boolean(toValue(options.organizationId).trim()) &&
      Boolean(toValue(options.environmentId).trim()) &&
      Boolean(selectedScope.value.scopeKind) &&
      Boolean(selectedScope.value.scopeId),
  )
  const queryInput = () => ({
    query: {
      organizationId: toValue(options.organizationId),
      environmentId: toValue(options.environmentId),
      scopeKind: selectedScope.value.scopeKind,
      scopeId: selectedScope.value.scopeId,
      take: 100,
      ...(debouncedKeyword.value.trim() ? { keyword: debouncedKeyword.value.trim() } : {}),
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
  const response = computed(() => {
    const data = envelope.value?.success === true ? envelope.value.data : undefined
    if (
      !ready.value ||
      data?.scopeKind !== selectedScope.value.scopeKind ||
      data?.scopeId !== selectedScope.value.scopeId
    ) {
      return undefined
    }
    return data
  })
  function setScanOverride(target: WmsOperationalCandidateScanTarget, value: string | undefined) {
    explicitScanOverrides.value = {
      ...explicitScanOverrides.value,
      [target]: value?.trim() || undefined,
    }
  }

  watch(
    [
      () => toValue(options.organizationId),
      () => toValue(options.environmentId),
      () => toValue(options.scopeReady),
      () => toValue(options.scopeKind),
      () => toValue(options.scopeId),
      () => options.filters.status,
    ],
    () => {
      options.filters.locationCode = undefined
      options.filters.lotNo = undefined
      searchKeyword.value = ''
      explicitScanOverrides.value = {}
    },
    { flush: 'sync' },
  )
  watch(
    () => options.filters.skuCode,
    () => {
      options.filters.lotNo = undefined
      setScanOverride('lot', undefined)
      if (explicitScanOverrides.value.location) {
        options.filters.locationCode = undefined
        setScanOverride('location', undefined)
      }
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
  watch(
    response,
    (current) => {
      if (!current || candidatesQuery.isLoading.value || candidatesQuery.error.value) return
      if (
        options.filters.locationCode &&
        explicitScanOverrides.value.location !== options.filters.locationCode &&
        !locationOptions.value.some((option) => option.value === options.filters.locationCode)
      ) {
        options.filters.locationCode = undefined
        options.filters.lotNo = undefined
        return
      }
      if (
        kind !== 'count' &&
        options.filters.lotNo &&
        explicitScanOverrides.value.lot !== options.filters.lotNo &&
        !lotOptions.value.some((option) => option.value === options.filters.lotNo)
      ) {
        options.filters.lotNo = undefined
      }
    },
    { flush: 'sync' },
  )

  return {
    ready,
    searchKeyword,
    scanOverrides: computed(() => ({ ...explicitScanOverrides.value })),
    setScanOverride,
    locationOptions,
    lotOptions,
    sourceLabel: computed(() => '当前范围仓储作业记录候选'),
    asOfUtc: computed(() => response.value?.asOfUtc ?? undefined),
    freshnessUtc: computed(() => response.value?.freshnessUtc ?? undefined),
    truncated: computed(() => response.value?.truncated === true),
    pending: candidatesQuery.isLoading,
    error: computed(() => (ready.value ? candidatesQuery.error.value : undefined)),
    refresh: () => (ready.value ? candidatesQuery.refetch() : Promise.resolve()),
  }
}
