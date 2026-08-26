import {
  resolveBusinessConsoleBarcode,
  searchBusinessConsoleObjects,
  type BusinessConsoleBarcodeResolveCandidate,
  type BusinessConsoleBarcodeResolveEnvelope,
  type BusinessConsoleBarcodeResolveRequest,
  type BusinessConsoleSearchEnvelope,
  type BusinessConsoleSearchResult,
} from '@nerv-iip/api-client'
import { shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import type { RouteLocationRaw } from 'vue-router'

import { barcodeCandidateRoute } from '@/components/barcode/barcodeRoute'

export type BarcodeResolveStatus =
  | 'idle'
  | 'pending'
  | 'resolved'
  | 'ambiguous'
  | 'unknown'
  | 'unsupported'
  | 'forbidden'
  | 'error'

type SearchStatus = 'idle' | 'pending' | 'resolved' | 'forbidden' | 'error'

interface BarcodeResolverOptions {
  organizationId: MaybeRefOrGetter<string>
  environmentId: MaybeRefOrGetter<string>
  resolveBarcode?: (
    request: BusinessConsoleBarcodeResolveRequest,
  ) => Promise<BusinessConsoleBarcodeResolveEnvelope>
  searchCandidates?: (query: string) => Promise<BusinessConsoleSearchEnvelope>
}

function isForbidden(error: unknown) {
  if (!error || typeof error !== 'object') return false
  const value = error as { status?: number; response?: { status?: number } }
  return value.status === 403 || value.response?.status === 403
}

async function defaultResolveBarcode(request: BusinessConsoleBarcodeResolveRequest) {
  const response = await resolveBusinessConsoleBarcode({ body: request, throwOnError: true })
  return response.data
}

async function defaultSearchCandidates(query: string) {
  const response = await searchBusinessConsoleObjects({
    query: { q: query, take: 10 },
    throwOnError: true,
  })
  return response.data
}

export function usePdaBarcodeResolver(options: BarcodeResolverOptions) {
  const status = shallowRef<BarcodeResolveStatus>('idle')
  const scannedValue = shallowRef('')
  const reasonCode = shallowRef<string | null>(null)
  const candidates = shallowRef<BusinessConsoleBarcodeResolveCandidate[]>([])
  const searchStatus = shallowRef<SearchStatus>('idle')
  const searchResults = shallowRef<BusinessConsoleSearchResult[]>([])
  let generation = 0

  const resolveBarcode = options.resolveBarcode ?? defaultResolveBarcode
  const searchCandidates = options.searchCandidates ?? defaultSearchCandidates

  function currentScope() {
    return {
      organizationId: toValue(options.organizationId).trim(),
      environmentId: toValue(options.environmentId).trim(),
    }
  }

  function resetSearch() {
    searchStatus.value = 'idle'
    searchResults.value = []
  }

  function invalidate() {
    generation += 1
    status.value = 'idle'
    scannedValue.value = ''
    reasonCode.value = null
    candidates.value = []
    resetSearch()
  }

  watch(
    [() => toValue(options.organizationId).trim(), () => toValue(options.environmentId).trim()],
    invalidate,
    { flush: 'sync' },
  )

  async function resolve(value: string): Promise<RouteLocationRaw | null> {
    const currentGeneration = ++generation
    const normalized = value.trim()
    const scope = currentScope()
    scannedValue.value = normalized
    reasonCode.value = null
    candidates.value = []
    resetSearch()

    if (!normalized || !scope.organizationId || !scope.environmentId) {
      status.value = 'error'
      return null
    }

    status.value = 'pending'
    try {
      const envelope = await resolveBarcode({
        organizationId: scope.organizationId,
        environmentId: scope.environmentId,
        scannedValue: normalized,
        pageIndex: 1,
        pageSize: 20,
      })
      if (currentGeneration !== generation) return null
      if (!envelope.success || !envelope.data) {
        status.value = 'error'
        return null
      }

      reasonCode.value = envelope.data.reasonCode ?? null
      const receivedCandidates = envelope.data.candidates ?? []
      if (envelope.data.status === 'resolved') {
        if (receivedCandidates.length !== 1) {
          status.value = 'unsupported'
          return null
        }
        const route = barcodeCandidateRoute(receivedCandidates[0]!)
        status.value = route ? 'resolved' : 'unsupported'
        return route
      }
      if (envelope.data.status === 'ambiguous') {
        candidates.value = receivedCandidates
        status.value = 'ambiguous'
        return null
      }
      if (envelope.data.status === 'unknown') {
        status.value = 'unknown'
        return null
      }
      if (envelope.data.status === 'forbidden') {
        status.value = 'forbidden'
        return null
      }
      status.value = 'unsupported'
      return null
    } catch (error) {
      if (currentGeneration !== generation) return null
      status.value = isForbidden(error) ? 'forbidden' : 'error'
      return null
    }
  }

  function selectCandidate(candidate: BusinessConsoleBarcodeResolveCandidate) {
    const route = barcodeCandidateRoute(candidate)
    if (!route) status.value = 'unsupported'
    return route
  }

  async function searchUnknownCandidates() {
    if (status.value !== 'unknown' || !scannedValue.value) return
    const currentGeneration = generation
    const query = scannedValue.value
    searchStatus.value = 'pending'
    try {
      const envelope = await searchCandidates(query)
      if (
        currentGeneration !== generation ||
        status.value !== 'unknown' ||
        scannedValue.value !== query
      )
        return
      if (!envelope.success || !envelope.data) {
        searchStatus.value = 'error'
        return
      }
      searchResults.value = envelope.data.results ?? []
      searchStatus.value = 'resolved'
    } catch (error) {
      if (
        currentGeneration !== generation ||
        status.value !== 'unknown' ||
        scannedValue.value !== query
      )
        return
      searchStatus.value = isForbidden(error) ? 'forbidden' : 'error'
    }
  }

  return {
    status,
    scannedValue,
    reasonCode,
    candidates,
    searchStatus,
    searchResults,
    resolve,
    selectCandidate,
    searchUnknownCandidates,
    cancel: invalidate,
  }
}
