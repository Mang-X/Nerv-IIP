import {
  getBusinessConsolePrincipalWorkContextQueryOptions,
  type BusinessConsolePrincipalWorkContextResponse,
} from '@nerv-iip/api-client'
import { useQuery, useQueryCache } from '@pinia/colada'
import { computed, onMounted, onUnmounted, shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { usePdaIdentity } from './useWorkbenchHome'

const PDA_STORAGE_PREFIX = 'nerv-iip.business-pda.'

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
  sessionStorage.clear()
}

export function usePdaProfile() {
  const auth = useAuthStore()
  const identity = usePdaIdentity()
  const online = shallowRef(typeof navigator === 'undefined' ? true : navigator.onLine)

  const contextQueries = PROFILE_CONTEXT_PERMISSIONS.map((permissionCode) =>
    useQuery(() => ({
      ...getBusinessConsolePrincipalWorkContextQueryOptions({
        query: {
          organizationId: identity.organizationId.value,
          environmentId: identity.environmentId.value,
          permissionCode,
        },
      }),
      enabled: identity.hasScope.value && identity.can(permissionCode),
    })),
  )

  const contexts = computed(() =>
    contextQueries.flatMap((query) => {
      const envelope = query.data.value
      return envelope?.success && envelope.data ? [envelope.data] : []
    }),
  )
  const aggregate = computed(() => aggregatePdaProfileContexts(contexts.value))

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
    online,
    pending: computed(() => contextQueries.some((query) => query.isLoading.value)),
    error: computed(() => contextQueries.map((query) => query.error.value).find(Boolean)),
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
