import {
  getBusinessConsolePrincipalWorkContextQueryOptions,
  type BusinessConsolePrincipalWorkContextResponse,
} from '@nerv-iip/api-client'
import { useQuery, useQueryCache } from '@pinia/colada'
import { computed, onMounted, onUnmounted, shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import {
  useWmsWorkScope,
  type WmsWorkScopeCatalogKind,
  type WmsWorkScopeOption,
} from './useWmsWorkScope'
import { usePdaIdentity } from './useWorkbenchHome'

const PDA_STORAGE_PREFIX = 'nerv-iip.business-pda.'

const WMS_PROFILE_CATALOGS = [
  { catalog: 'receipts', label: '收货/上架', permission: 'business.wms.receipts.read' },
  { catalog: 'shipments', label: '拣货/复核', permission: 'business.wms.shipments.read' },
  { catalog: 'counts', label: '盘点', permission: 'business.wms.counts.read' },
] as const

export const PROFILE_CONTEXT_PERMISSIONS = [
  'business.mes.dispatch.read',
  'business.mes.operations.read',
  'business.mes.reporting.read',
  'business.mes.materials.read',
  'business.mes.receipts.read',
  'business.quality.inspection-records.read',
  'business.wms.receipts.read',
  'business.wms.shipments.read',
  'business.wms.counts.read',
  'business.maintenance.work-orders.read',
  'business.maintenance.plans.read',
  'business.iiot.alarms.read',
] as const

const SCOPE_KIND_LABELS: Record<string, string> = {
  organization: '组织',
  self: '本人',
  team: '班组',
  'work-center': '工作中心',
  workshop: '车间',
}

export interface AggregatedPdaProfile {
  departmentName: string
  displayName: string
  employeeNo: string
  jobTitle: string
  roleNames: string[]
  scopeLabels: string[]
  teamNames: string[]
}

export interface WmsProfileScopeSource {
  catalog: WmsWorkScopeCatalogKind
  options: WmsWorkScopeOption[]
  selectedValue?: string
}

const WMS_CATALOG_LABELS: Record<WmsWorkScopeCatalogKind, string> = {
  receipts: '收货/上架',
  shipments: '拣货/复核',
  counts: '盘点',
}

const WMS_SCOPE_KIND_LABELS: Record<string, string> = {
  self: '本人',
  'work-pool': '作业池',
  site: '站点',
}

export function aggregateWmsProfileScopes(sources: WmsProfileScopeSource[]) {
  const authorizedLabels: string[] = []
  const currentLabels: string[] = []
  for (const source of sources) {
    const catalogLabel = WMS_CATALOG_LABELS[source.catalog]
    for (const option of source.options) {
      const separator = option.value.indexOf(':')
      if (separator <= 0 || separator === option.value.length - 1) continue
      const kind = option.value.slice(0, separator)
      const id = option.value.slice(separator + 1)
      const kindLabel = WMS_SCOPE_KIND_LABELS[kind] ?? kind
      const name = kind === 'self' ? id : option.label
      const label = `${catalogLabel} · ${kindLabel} · ${name}`
      authorizedLabels.push(label)
      if (option.value === source.selectedValue) currentLabels.push(label)
    }
  }
  return {
    authorizedLabels: unique(authorizedLabels),
    currentLabels: unique(currentLabels),
  }
}

export function aggregatePdaProfileContexts(
  contexts: BusinessConsolePrincipalWorkContextResponse[],
): AggregatedPdaProfile {
  const firstWorker = contexts.map((context) => context.worker).find(Boolean)
  const roleNames = unique(
    contexts.flatMap((context) =>
      (context.principal?.roles ?? []).map((role) => role.displayName?.trim() ?? ''),
    ),
  )
  const teamNames = unique(
    contexts.flatMap((context) => (context.teams ?? []).map((team) => team.name?.trim() ?? '')),
  )
  const scopeLabels = unique(
    contexts.flatMap((context) =>
      (context.authorizedScopes ?? []).map((scope) => {
        const kind = scope.kind?.trim() ?? ''
        const name = scope.displayName?.trim() || scope.id?.trim() || ''
        return kind && name ? `${SCOPE_KIND_LABELS[kind] ?? kind} · ${name}` : ''
      }),
    ),
  )

  return {
    departmentName: firstWorker?.departmentName?.trim() ?? '',
    displayName: firstWorker?.name?.trim() ?? '',
    employeeNo: firstWorker?.employeeNo?.trim() ?? '',
    jobTitle: firstWorker?.jobTitle?.trim() ?? '',
    roleNames,
    scopeLabels,
    teamNames,
  }
}

export function clearPdaApplicationStorage() {
  for (let index = localStorage.length - 1; index >= 0; index -= 1) {
    const key = localStorage.key(index)
    if (key?.startsWith(PDA_STORAGE_PREFIX)) localStorage.removeItem(key)
  }
  for (let index = sessionStorage.length - 1; index >= 0; index -= 1) {
    const key = sessionStorage.key(index)
    if (key?.startsWith(PDA_STORAGE_PREFIX)) sessionStorage.removeItem(key)
  }
}

export function usePdaProfile() {
  const auth = useAuthStore()
  const identity = usePdaIdentity()
  const online = shallowRef(typeof navigator === 'undefined' ? true : navigator.onLine)

  const contextQueries = PROFILE_CONTEXT_PERMISSIONS.map((permissionCode) => ({
    permissionCode,
    query: useQuery(() => ({
      ...getBusinessConsolePrincipalWorkContextQueryOptions({
        query: {
          organizationId: identity.organizationId.value,
          environmentId: identity.environmentId.value,
          permissionCode,
        },
      }),
      enabled: identity.hasScope.value && identity.can(permissionCode),
    })),
  }))
  const wmsScopes = WMS_PROFILE_CATALOGS.map((definition) => {
    const enabled = computed(() => identity.hasScope.value && identity.can(definition.permission))
    return {
      ...definition,
      enabled,
      scope: useWmsWorkScope(definition.catalog, enabled),
    }
  })

  const activeContextQueries = computed(() =>
    contextQueries.filter(({ permissionCode }) => identity.can(permissionCode)),
  )
  const activeWmsScopes = computed(() => wmsScopes.filter(({ enabled }) => enabled.value))

  const contexts = computed(() =>
    activeContextQueries.value.flatMap(({ query }) => {
      const envelope = query.data.value
      return envelope?.success && envelope.data ? [envelope.data] : []
    }),
  )
  const aggregate = computed(() => aggregatePdaProfileContexts(contexts.value))
  const wmsAggregate = computed(() =>
    aggregateWmsProfileScopes(
      activeWmsScopes.value.map(({ catalog, scope }) => ({
        catalog,
        options: scope.scopeOptions.value,
        selectedValue: scope.scopeKey.value,
      })),
    ),
  )
  const pending = computed(
    () =>
      activeContextQueries.value.some(({ query }) => query.isLoading.value) ||
      activeWmsScopes.value.some(({ scope }) => scope.pending.value),
  )
  const successfulResponseCount = computed(
    () =>
      contexts.value.length +
      activeWmsScopes.value.filter(({ scope }) => scope.hasSuccessfulResponse.value).length,
  )
  const failedResponseCount = computed(
    () =>
      activeContextQueries.value.filter(({ query }) => {
        const envelope = query.data.value
        return (
          Boolean(query.error.value) ||
          (!query.isLoading.value && envelope !== undefined && !(envelope.success && envelope.data))
        )
      }).length + activeWmsScopes.value.filter(({ scope }) => scope.hasFailedResponse.value).length,
  )
  const state = computed<'loading' | 'error' | 'partial' | 'ready'>(() => {
    if (pending.value) return 'loading'
    if (failedResponseCount.value > 0) {
      return successfulResponseCount.value > 0 ? 'partial' : 'error'
    }
    return 'ready'
  })
  const resolvedAtUtc = computed(
    () =>
      contexts.value
        .map((context) => context.resolvedAtUtc?.trim() ?? '')
        .filter(Boolean)
        .sort()[0] ?? '',
  )

  function updateNetworkStatus() {
    online.value = navigator.onLine
  }

  onMounted(() => {
    window.addEventListener('online', updateNetworkStatus)
    window.addEventListener('offline', updateNetworkStatus)
  })
  onUnmounted(() => {
    window.removeEventListener('online', updateNetworkStatus)
    window.removeEventListener('offline', updateNetworkStatus)
  })

  return {
    principalId: identity.principalId,
    principalType: computed(() => auth.principal?.principalType ?? ''),
    loginName: identity.loginName,
    displayName: computed(
      () =>
        aggregate.value.displayName ||
        identity.worker.value?.displayName ||
        identity.loginName.value,
    ),
    employeeNo: computed(
      () => aggregate.value.employeeNo || identity.worker.value?.employeeNo || '',
    ),
    jobTitle: computed(() => aggregate.value.jobTitle || identity.worker.value?.jobTitle || ''),
    departmentName: computed(() => aggregate.value.departmentName),
    teamNames: computed(() =>
      aggregate.value.teamNames.length > 0
        ? aggregate.value.teamNames
        : unique((identity.worker.value?.teams ?? []).map((team) => team.teamName ?? '')),
    ),
    roleNames: computed(() => aggregate.value.roleNames),
    scopeLabels: computed(() => aggregate.value.scopeLabels),
    wmsAuthorizedScopeLabels: computed(() => wmsAggregate.value.authorizedLabels),
    wmsCurrentScopeLabels: computed(() => wmsAggregate.value.currentLabels),
    resolvedAtUtc,
    online,
    state,
    refresh: () =>
      Promise.allSettled([
        ...activeContextQueries.value.map(({ query }) => query.refetch()),
        ...activeWmsScopes.value.map(({ scope }) => scope.refresh()),
      ]),
  }
}

export function usePdaLogout() {
  const queryCache = useQueryCache()

  function clearCache() {
    queryCache.cancelQueries(undefined, 'logout')
    for (const entry of [...queryCache.getEntries()]) queryCache.remove(entry)
    clearPdaApplicationStorage()
  }

  return { clearCache }
}

function unique(values: string[]) {
  return [...new Set(values.filter(Boolean))]
}
