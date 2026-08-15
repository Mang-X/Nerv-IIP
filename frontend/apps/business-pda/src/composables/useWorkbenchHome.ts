import {
  listBusinessConsoleQualityInspectionTasksQueryOptions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWorkersQueryOptions,
  type BusinessConsoleQualityInspectionTaskItem,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'

import { useAuthStore } from '@/stores/auth'
import { MAINTENANCE_READ_MODEL_PERMISSIONS } from '@/permissions/maintenanceReadModelAccess'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'

/**
 * 工作台首页数据封装：按登录人权限裁剪各域摘要。
 *
 * - 权限判定用 `principal.permissionCodes`（真实授权来自网关逐请求校验，这里只决定
 *   首页渲染哪些板块，避免无权限板块发起注定 403 的请求）。
 * - 各域空 scope（未登录 / principal 缺 org/env）时一律不发请求。
 */

/** 首页各板块的权限门槛（与 BusinessGateway 各 facade 实际要求一致）。 */
export const HOME_PERMISSIONS = {
  mesOperations: 'business.mes.operations.read',
  workerProfile: MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources,
  wmsReceipts: 'business.wms.receipts.read',
  wmsShipments: 'business.wms.shipments.read',
  wmsCounts: 'business.wms.counts.read',
  quality: 'business.quality.inspection-records.read',
  alarms: 'business.iiot.alarms.read',
  maintenanceWorkOrders: MAINTENANCE_READ_MODEL_PERMISSIONS.workOrders,
  masterDataResources: MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources,
} as const

const HOME_TAKE = 100

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

export function usePdaIdentity() {
  const auth = useAuthStore()
  const principalId = computed(() => auth.principal?.principalId ?? '')
  const loginName = computed(() => auth.principal?.loginName ?? '')
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const permissionCodes = computed(() => new Set(auth.principal?.permissionCodes ?? []))
  const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
  const can = (code: string) => permissionCodes.value.has(code)

  // 工人档案（姓名/工号/岗位/班组）来自 MasterData 员工目录；无档案（如 admin）时回落登录名。
  const workerQuery = useQuery(() => ({
    ...listBusinessConsoleWorkersQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        userId: principalId.value,
        pageIndex: 1,
        pageSize: 1,
      },
    }),
    enabled: hasScope.value && Boolean(principalId.value) && can(HOME_PERMISSIONS.workerProfile),
  }))

  const worker = computed<BusinessConsoleWorkerDirectoryItem | undefined>(() => {
    const envelope = workerQuery.data.value
    if (!envelope?.success) return undefined
    return envelope.data?.items?.[0]
  })

  return {
    principalId,
    loginName,
    organizationId,
    environmentId,
    hasScope,
    permissionCodes,
    can,
    worker,
    displayName: computed(() => worker.value?.displayName || loginName.value),
  }
}

export interface WarehouseSummaryEntry {
  key: 'inbound' | 'putaway' | 'pick' | 'count'
  label: string
  route: string
  count: number
}

export function useWarehouseSummary() {
  const identity = usePdaIdentity()
  const canReceipts = computed(
    () => identity.hasScope.value && identity.can(HOME_PERMISSIONS.wmsReceipts),
  )
  const canShipments = computed(
    () => identity.hasScope.value && identity.can(HOME_PERMISSIONS.wmsShipments),
  )
  const canCounts = computed(
    () => identity.hasScope.value && identity.can(HOME_PERMISSIONS.wmsCounts),
  )
  const enabled = computed(() => canReceipts.value || canShipments.value || canCounts.value)

  const scopeQuery = () => ({
    organizationId: identity.organizationId.value,
    environmentId: identity.environmentId.value,
    status: 'Open',
    skip: 0,
    take: 1,
  })

  const inboundQuery = useQuery(() => ({
    ...listBusinessConsoleWmsInboundOrdersQueryOptions({ query: scopeQuery() }),
    enabled: canReceipts.value,
  }))
  const putawayQuery = useQuery(() => ({
    ...listBusinessConsoleWmsPutawayTasksQueryOptions({ query: scopeQuery() }),
    enabled: canReceipts.value,
  }))
  const pickingQuery = useQuery(() => ({
    ...listBusinessConsoleWmsPickingTasksQueryOptions({ query: scopeQuery() }),
    enabled: canShipments.value,
  }))
  const countQuery = useQuery(() => ({
    ...listBusinessConsoleWmsCountExecutionsQueryOptions({ query: scopeQuery() }),
    enabled: canCounts.value,
  }))

  const entries = computed<WarehouseSummaryEntry[]>(() => {
    const result: WarehouseSummaryEntry[] = []
    if (canReceipts.value) {
      result.push({
        key: 'inbound',
        label: '待收货',
        route: '/wms/inbound',
        count: listTotal(inboundQuery.data.value),
      })
      result.push({
        key: 'putaway',
        label: '待上架',
        route: '/wms/putaway',
        count: listTotal(putawayQuery.data.value),
      })
    }
    if (canShipments.value) {
      result.push({
        key: 'pick',
        label: '待拣货',
        route: '/wms/pick',
        count: listTotal(pickingQuery.data.value),
      })
    }
    if (canCounts.value) {
      result.push({
        key: 'count',
        label: '待盘点',
        route: '/wms/count',
        count: listTotal(countQuery.data.value),
      })
    }
    return result
  })

  return {
    enabled,
    entries,
    pending: computed(
      () =>
        inboundQuery.isLoading.value ||
        putawayQuery.isLoading.value ||
        pickingQuery.isLoading.value ||
        countQuery.isLoading.value,
    ),
  }
}

export function usePendingInspectionSummary() {
  const identity = usePdaIdentity()
  const visible = computed(() => identity.can(HOME_PERMISSIONS.quality))
  const scopeReady = identity.hasScope
  const enabled = computed(() => scopeReady.value && visible.value)

  const tasksQuery = useQuery(() => ({
    ...listBusinessConsoleQualityInspectionTasksQueryOptions({
      query: {
        organizationId: identity.organizationId.value,
        environmentId: identity.environmentId.value,
        status: 'pending',
        skip: 0,
        take: HOME_TAKE,
      },
    }),
    enabled: enabled.value,
  }))
  const scopeKey = computed(() =>
    scopeReady.value ? `${identity.organizationId.value}:${identity.environmentId.value}` : '',
  )
  const currentResponse = useScopeBoundListResponse(() => tasksQuery.data.value, scopeKey, enabled)
  const lastUpdatedAt = useListFreshness(currentResponse, enabled)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    enabled,
    tasksQuery.isLoading,
  )

  return {
    visible,
    scopeReady,
    enabled,
    tasks: computed<BusinessConsoleQualityInspectionTaskItem[]>(() =>
      listItems<BusinessConsoleQualityInspectionTaskItem>(currentResponse.value),
    ),
    total: computed(() => listTotal(currentResponse.value)),
    pending: tasksQuery.isLoading,
    error: tasksQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (enabled.value ? tasksQuery.refetch() : Promise.resolve()),
  }
}
