import {
  getBusinessConsoleMasterDataResourceDetailQueryOptions,
  getBusinessConsoleMasterDataResourceDetail,
  getBusinessConsoleMaintenanceWorkOrderQueryOptions,
  listBusinessConsoleWorkers,
  listBusinessConsoleMaintenanceWorkOrders,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  type BusinessConsoleMasterDataResourceDetail,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import {
  normalizeMaintenanceWorkOrderStatusFilter,
  type MaintenanceWorkOrderStatusCode,
} from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'

import { useListFreshness, useScopeBoundListResponse } from './useListFreshness'
import { useTaskListPagination } from './useTaskListPagination'
import {
  MAINTENANCE_READ_MODEL_PERMISSIONS,
  canAccessMaintenanceWorkOrderReadModel,
} from '@/permissions/maintenanceReadModelAccess'
import { useAuthStore } from '@/stores/auth'
import { isStrictRfc3339DateTime } from '@/utils/strictRfc3339'

const PAGE_SIZE = 20
const MAX_IDENTITY_REFERENCES = 20
const CANONICAL_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

type WorkOrderListEnvelope =
  | {
      success?: boolean
      data?: {
        items?: BusinessConsoleMaintenanceWorkOrderItem[]
        total?: number
        skip?: number
        take?: number
      } | null
    }
  | undefined

type WorkOrderDetailEnvelope =
  | { success?: boolean; data?: BusinessConsoleMaintenanceWorkOrderItem | null }
  | undefined

type DeviceAssetDetailEnvelope =
  | { success?: boolean; data?: BusinessConsoleMasterDataResourceDetail | null }
  | undefined

interface WorkOrderPage {
  items: BusinessConsoleMaintenanceWorkOrderItem[]
  total: number
}

export interface MaintenanceSelfWorkOrderFilters {
  status: '' | MaintenanceWorkOrderStatusCode
  deviceAssetIds: string[]
  keyword: string
}

export interface MaintenanceWorkOrderIdentityDirectory {
  users: Readonly<Record<string, string>>
  teams: Readonly<Record<string, string>>
}

export type AuthoritativeMaintenanceWorkOrderDetail = BusinessConsoleMaintenanceWorkOrderItem & {
  workOrderId: string
  deviceAssetId: string
  priority: string
  status: string
  openedAtUtc: string
  version: number
  allowedActions: string[]
  blockReasons: string[]
  lifecycle: NonNullable<BusinessConsoleMaintenanceWorkOrderItem['lifecycle']>
  assignedTechnicianUserId: string
  assignedTeamId: string | null
}

function trimToUndefined(value: string) {
  return value.trim() || undefined
}

function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isNullableString(value: unknown): value is string | null | undefined {
  return value === null || value === undefined || typeof value === 'string'
}

function isValidVersion(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

function normalizeCanonicalGuid(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const normalized = value.trim().toLowerCase()
  return CANONICAL_GUID.test(normalized) && normalized !== '00000000-0000-0000-0000-000000000000'
    ? normalized
    : undefined
}

function isCanonicalGuid(value: unknown): value is string {
  return normalizeCanonicalGuid(value) !== undefined
}

function normalizeDeviceReference(value: string) {
  return normalizeCanonicalGuid(value) ?? value.trim()
}

export function normalizeMaintenanceDeviceReferences(values: unknown): string[] {
  if (!Array.isArray(values)) return []
  const unique = new Map<string, string>()
  for (const value of values) {
    if (typeof value !== 'string') continue
    const normalized = value.trim()
    if (!normalized) continue
    const key = normalized.toLowerCase()
    if (!unique.has(key)) unique.set(key, normalized)
  }
  return [...unique.values()].slice(0, 2)
}

function isExplicitAssignment(value: unknown) {
  return value === null || isNonBlankString(value)
}

function isWorkOrderListItem(
  value: unknown,
  principalId: string,
): value is BusinessConsoleMaintenanceWorkOrderItem {
  return Boolean(
    value &&
    typeof value === 'object' &&
    !Array.isArray(value) &&
    isCanonicalGuid((value as BusinessConsoleMaintenanceWorkOrderItem).workOrderId) &&
    isNullableString((value as BusinessConsoleMaintenanceWorkOrderItem).sourceReferenceId) &&
    isNonBlankString((value as BusinessConsoleMaintenanceWorkOrderItem).deviceAssetId) &&
    isNonBlankString((value as BusinessConsoleMaintenanceWorkOrderItem).priority) &&
    isNonBlankString((value as BusinessConsoleMaintenanceWorkOrderItem).status) &&
    isStrictRfc3339DateTime((value as BusinessConsoleMaintenanceWorkOrderItem).openedAtUtc) &&
    isValidVersion((value as BusinessConsoleMaintenanceWorkOrderItem).version) &&
    (value as BusinessConsoleMaintenanceWorkOrderItem).assignedTechnicianUserId === principalId,
  )
}

function parseWorkOrderPage(
  envelope: unknown,
  skip: number,
  take: number,
  principalId: string,
): WorkOrderPage {
  if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) {
    throw new Error('维修工单读取失败，请重试。')
  }
  const response = envelope as Exclude<WorkOrderListEnvelope, undefined>
  const data = response.data
  if (
    response.success !== true ||
    !data ||
    typeof data !== 'object' ||
    Array.isArray(data) ||
    !Array.isArray(data.items) ||
    !data.items.every((item) => isWorkOrderListItem(item, principalId)) ||
    !Number.isSafeInteger(data.total) ||
    (data.total ?? -1) < 0 ||
    !Number.isSafeInteger(data.skip) ||
    data.skip !== skip ||
    !Number.isSafeInteger(data.take) ||
    data.take !== take
  ) {
    throw new Error('维修工单读取失败，请重试。')
  }
  const total = data.total as number
  const items = data.items.map((item) => ({
    ...item,
    workOrderId: normalizeCanonicalGuid(item.workOrderId)!,
    deviceAssetId: normalizeDeviceReference(item.deviceAssetId!),
  }))
  if (items.length > take || total < skip + items.length || (skip < total && items.length === 0)) {
    throw new Error('维修工单分页信息不一致，请重试。')
  }
  return { items, total }
}

function isAuthoritativeMaintenanceWorkOrderDetail(
  value: BusinessConsoleMaintenanceWorkOrderItem | null | undefined,
  requestedWorkOrderId: string,
  principalId: string,
): value is AuthoritativeMaintenanceWorkOrderDetail {
  const terminalStatus =
    value &&
    isNonBlankString(value.status) &&
    ['closed', 'cancelled'].includes(value.status.trim().toLowerCase())
  const terminalBlock =
    value &&
    Array.isArray(value.blockReasons) &&
    value.blockReasons.some(
      (reason) => isNonBlankString(reason) && reason.trim().toLowerCase() === 'terminal-status',
    )
  return Boolean(
    value &&
    typeof value === 'object' &&
    isCanonicalGuid(requestedWorkOrderId) &&
    isCanonicalGuid(value.workOrderId) &&
    normalizeCanonicalGuid(value.workOrderId) === normalizeCanonicalGuid(requestedWorkOrderId) &&
    isNullableString(value.sourceReferenceId) &&
    isNonBlankString(value.deviceAssetId) &&
    isNonBlankString(value.priority) &&
    isNonBlankString(value.status) &&
    isStrictRfc3339DateTime(value.openedAtUtc) &&
    isValidVersion(value.version) &&
    Array.isArray(value.allowedActions) &&
    value.allowedActions.every(isNonBlankString) &&
    (!(terminalStatus || terminalBlock) || value.allowedActions.length === 0) &&
    Array.isArray(value.blockReasons) &&
    value.blockReasons.every(isNonBlankString) &&
    value.assignedTechnicianUserId === principalId &&
    Object.hasOwn(value, 'assignedTeamId') &&
    isExplicitAssignment(value.assignedTeamId) &&
    Array.isArray(value.lifecycle) &&
    value.lifecycle.every(
      (event) =>
        event !== null &&
        typeof event === 'object' &&
        isNonBlankString(event.action) &&
        isNonBlankString(event.fromStatus) &&
        isNonBlankString(event.toStatus) &&
        isNonBlankString(event.actorPrincipalId) &&
        Object.hasOwn(event, 'technicianUserId') &&
        isExplicitAssignment(event.technicianUserId) &&
        Object.hasOwn(event, 'teamId') &&
        isExplicitAssignment(event.teamId) &&
        isNonBlankString(event.reason) &&
        isValidVersion(event.resultingVersion) &&
        isStrictRfc3339DateTime(event.occurredAtUtc),
    ),
  )
}

function collectIdentityReferences(workOrder: AuthoritativeMaintenanceWorkOrderDetail | undefined) {
  const users = new Set<string>()
  const teams = new Set<string>()
  if (workOrder) {
    users.add(workOrder.assignedTechnicianUserId)
    if (workOrder.assignedTeamId) teams.add(workOrder.assignedTeamId)
    for (const event of workOrder.lifecycle) {
      users.add(event.actorPrincipalId!)
      if (event.technicianUserId) users.add(event.technicianUserId)
      if (event.teamId) teams.add(event.teamId)
    }
  }
  const userIds = [...users].sort()
  const teamIds = [...teams].sort()
  return {
    userIds,
    teamIds,
    bounded: userIds.length + teamIds.length <= MAX_IDENTITY_REFERENCES,
  }
}

function parseExactWorker(envelope: unknown, requestedUserId: string) {
  if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) return undefined
  const response = envelope as {
    success?: boolean
    data?: {
      pageIndex?: number
      pageSize?: number
      totalCount?: number
      items?: BusinessConsoleWorkerDirectoryItem[]
    } | null
  }
  const data = response.data
  if (
    response.success !== true ||
    !data ||
    data.pageIndex !== 1 ||
    data.pageSize !== 1 ||
    data.totalCount !== 1 ||
    !Array.isArray(data.items) ||
    data.items.length !== 1
  ) {
    return undefined
  }
  const worker = data.items[0]
  return worker?.userId === requestedUserId &&
    isNonBlankString(worker.employeeNo) &&
    isNonBlankString(worker.displayName) &&
    isNonBlankString(worker.employmentStatus) &&
    typeof worker.active === 'boolean' &&
    Array.isArray(worker.teams) &&
    Array.isArray(worker.skills) &&
    isNonBlankString(worker.snapshotVersion)
    ? worker.displayName.trim()
    : undefined
}

function parseExactTeam(
  envelope: unknown,
  requestedTeamId: string,
  organizationId: string,
  environmentId: string,
) {
  if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) return undefined
  const response = envelope as Exclude<DeviceAssetDetailEnvelope, undefined>
  const team = response.success === true ? response.data : undefined
  return team?.resourceType === 'team' &&
    team.code === requestedTeamId &&
    team.organizationId === organizationId &&
    team.environmentId === environmentId &&
    typeof team.active === 'boolean' &&
    isNonBlankString(team.snapshotVersion) &&
    isNonBlankString(team.displayName)
    ? team.displayName.trim()
    : undefined
}

function useMaintenanceSelfScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId?.trim() ?? '')
  const environmentId = computed(() => auth.principal?.environmentId?.trim() ?? '')
  const principalId = computed(() => auth.principal?.principalId?.trim() ?? '')
  const permissions = computed(() => new Set(auth.principal?.permissionCodes ?? []))
  const canReadMaintenance = computed(() =>
    permissions.value.has(MAINTENANCE_READ_MODEL_PERMISSIONS.workOrders),
  )
  const canReadDevice = computed(() =>
    permissions.value.has(MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources),
  )
  const canRead = computed(() => canAccessMaintenanceWorkOrderReadModel(permissions.value))
  const scopeReady = computed(
    () =>
      Boolean(organizationId.value && environmentId.value && principalId.value) && canRead.value,
  )
  const scopeKey = computed(() =>
    scopeReady.value
      ? `${organizationId.value}:${environmentId.value}:self:${principalId.value}`
      : '',
  )
  const queryScope = () => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
    scopeKind: 'self',
    scopeId: principalId.value,
  })

  return {
    organizationId,
    environmentId,
    principalId,
    permissions,
    canRead,
    canReadMaintenance,
    canReadDevice,
    scopeReady,
    scopeKey,
    queryScope,
  }
}

export function useMaintenanceSelfWorkOrders() {
  const scope = useMaintenanceSelfScope()
  const filters = reactive<MaintenanceSelfWorkOrderFilters>({
    status: '',
    deviceAssetIds: [],
    keyword: '',
  })
  const normalizedDeviceAssetIds = computed(() =>
    normalizeMaintenanceDeviceReferences(filters.deviceAssetIds),
  )
  const listQueryParameters = () => ({
    ...scope.queryScope(),
    status: trimToUndefined(normalizeMaintenanceWorkOrderStatusFilter(filters.status)),
    deviceAssetIds: trimToUndefined(normalizedDeviceAssetIds.value.join(',')),
    keyword: trimToUndefined(filters.keyword),
  })
  const identity = computed(
    () =>
      `${scope.scopeKey.value}:${normalizeMaintenanceWorkOrderStatusFilter(filters.status)}:${normalizedDeviceAssetIds.value.join('\u001f')}:${filters.keyword.trim()}`,
  )
  const principalIdentityQuery = useQuery(() => ({
    key: [{ _id: 'maintenance-list-principal', scope: scope.scopeKey.value }],
    enabled: scope.scopeReady.value,
    query: async () => {
      const { data } = await listBusinessConsoleWorkers({
        query: {
          organizationId: scope.organizationId.value,
          environmentId: scope.environmentId.value,
          userId: scope.principalId.value,
          pageIndex: 1,
          pageSize: 1,
          includeDisabled: true,
        },
        throwOnError: true,
      })
      const displayName = parseExactWorker(data, scope.principalId.value)
      if (!displayName) throw new Error('身份资料暂不可用')
      return displayName
    },
  }))
  const principalIdentityResponse = useScopeBoundListResponse(
    () => principalIdentityQuery.data.value,
    scope.scopeKey,
    scope.scopeReady,
  )
  const principalDisplayName = computed(() =>
    !principalIdentityQuery.isLoading.value && !principalIdentityQuery.error.value
      ? principalIdentityResponse.value
      : undefined,
  )

  const listQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenanceWorkOrdersQueryOptions({
      query: {
        ...listQueryParameters(),
        skip: 0,
        take: PAGE_SIZE,
      },
    }),
    enabled: scope.scopeReady.value,
  }))
  const response = useScopeBoundListResponse(() => listQuery.data.value, identity, scope.scopeReady)
  const firstPageState = computed<{ page?: WorkOrderPage; error?: Error }>(() => {
    if (response.value === undefined) return {}
    try {
      return { page: parseWorkOrderPage(response.value, 0, PAGE_SIZE, scope.principalId.value) }
    } catch (error) {
      return {
        error: error instanceof Error ? error : new Error('维修工单读取失败，请重试。'),
      }
    }
  })
  const firstPage = computed(() => firstPageState.value.page)
  const pager = useTaskListPagination<BusinessConsoleMaintenanceWorkOrderItem>({
    identity,
    firstPage,
    pageSize: PAGE_SIZE,
    itemKey: (item) => item.workOrderId ?? '',
    fetchPage: async ({ skip, take }) => {
      const { data } = await listBusinessConsoleMaintenanceWorkOrders({
        query: {
          ...listQueryParameters(),
          skip,
          take,
        },
        throwOnError: true,
      })
      return parseWorkOrderPage(data, skip, take, scope.principalId.value)
    },
    refreshFirstPage: listQuery.refetch,
  })
  const pending = computed(() => listQuery.isLoading.value || pager.refreshing.value)
  const hasSuccessfulResponse = computed(
    () =>
      scope.scopeReady.value &&
      !pending.value &&
      !listQuery.error.value &&
      Boolean(firstPage.value),
  )
  const hasFailedResponse = computed(
    () =>
      scope.scopeReady.value &&
      !pending.value &&
      Boolean(listQuery.error.value || firstPageState.value.error),
  )
  const error = computed(() => listQuery.error.value ?? firstPageState.value.error)
  const freshness = useListFreshness(
    computed(() => {
      if (firstPage.value) return { success: true }
      if (firstPageState.value.error) return { success: false }
      return undefined
    }),
    scope.scopeReady,
  )
  const visibleItems = computed(() =>
    pending.value || hasFailedResponse.value ? [] : pager.items.value,
  )
  const visibleTotal = computed(() =>
    pending.value || hasFailedResponse.value ? 0 : pager.total.value,
  )
  const visibleLoaded = computed(() => visibleItems.value.length)
  const visibleHasMore = computed(
    () => !pending.value && !hasFailedResponse.value && pager.hasMore.value,
  )
  const refresh = async () => {
    if (!scope.scopeReady.value) return
    await pager.refresh()
    await principalIdentityQuery.refetch()
  }

  return {
    ...scope,
    filters,
    principalDisplayName,
    items: visibleItems,
    total: visibleTotal,
    loaded: visibleLoaded,
    hasMore: visibleHasMore,
    loadingMore: pager.loadingMore,
    refreshing: pager.refreshing,
    loadMoreError: pager.loadMoreError,
    loadMore: pager.loadMore,
    refresh,
    pending,
    error,
    lastUpdatedAt: freshness,
    hasSuccessfulResponse,
    hasFailedResponse,
  }
}

export function useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId: MaybeRefOrGetter<string>) {
  const scope = useMaintenanceSelfScope()
  const workOrderId = computed(() => normalizeCanonicalGuid(toValue(requestedWorkOrderId)) ?? '')
  const enabled = computed(() => scope.scopeReady.value && Boolean(workOrderId.value))
  const detailIdentity = computed(() =>
    enabled.value ? `${scope.scopeKey.value}:work-order:${workOrderId.value}` : '',
  )
  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleMaintenanceWorkOrderQueryOptions({
      path: { workOrderId: workOrderId.value },
      query: scope.queryScope(),
    }),
    enabled: enabled.value,
  }))
  const detailEnvelope = useScopeBoundListResponse(
    () => detailQuery.data.value as WorkOrderDetailEnvelope,
    detailIdentity,
    enabled,
  )
  const validatedWorkOrder = computed(() => {
    const envelope = detailEnvelope.value
    const item = envelope?.success === true ? envelope.data : undefined
    return isAuthoritativeMaintenanceWorkOrderDetail(
      item,
      workOrderId.value,
      scope.principalId.value,
    )
      ? {
          ...item,
          workOrderId: normalizeCanonicalGuid(item.workOrderId)!,
          deviceAssetId: normalizeDeviceReference(item.deviceAssetId),
        }
      : undefined
  })
  const authoritativePending = computed(() => enabled.value && detailQuery.isLoading.value)
  const authoritativeHasSuccessfulResponse = computed(
    () =>
      enabled.value &&
      !authoritativePending.value &&
      !detailQuery.error.value &&
      Boolean(validatedWorkOrder.value),
  )
  const authoritativeHasFailedResponse = computed(
    () =>
      enabled.value &&
      !authoritativePending.value &&
      Boolean(
        detailQuery.error.value ||
        (detailEnvelope.value !== undefined && !validatedWorkOrder.value),
      ),
  )
  const authoritativeWorkOrder = computed(() =>
    authoritativeHasSuccessfulResponse.value ? validatedWorkOrder.value : undefined,
  )

  const identityReferences = computed(() => collectIdentityReferences(validatedWorkOrder.value))
  const identityEnabled = computed(
    () =>
      authoritativeHasSuccessfulResponse.value &&
      identityReferences.value.bounded &&
      identityReferences.value.userIds.length + identityReferences.value.teamIds.length > 0,
  )
  const identityKey = computed(() =>
    identityEnabled.value
      ? `${detailIdentity.value}:identities:${identityReferences.value.userIds.join(',')}:${identityReferences.value.teamIds.join(',')}`
      : '',
  )
  const identityQuery = useQuery(() => ({
    key: [{ _id: 'maintenance-identities', identity: identityKey.value }],
    enabled: identityEnabled.value,
    query: async (): Promise<MaintenanceWorkOrderIdentityDirectory> => {
      const userEntries = await Promise.all(
        identityReferences.value.userIds.map(async (userId) => {
          const { data } = await listBusinessConsoleWorkers({
            query: {
              organizationId: scope.organizationId.value,
              environmentId: scope.environmentId.value,
              userId,
              pageIndex: 1,
              pageSize: 1,
              includeDisabled: true,
            },
            throwOnError: true,
          })
          const displayName = parseExactWorker(data, userId)
          if (!displayName) throw new Error('身份资料暂不可用')
          return [userId, displayName] as const
        }),
      )
      const teamEntries = await Promise.all(
        identityReferences.value.teamIds.map(async (teamId) => {
          const { data } = await getBusinessConsoleMasterDataResourceDetail({
            path: { resourceType: 'team', code: teamId },
            query: {
              organizationId: scope.organizationId.value,
              environmentId: scope.environmentId.value,
            },
            throwOnError: true,
          })
          const displayName = parseExactTeam(
            data,
            teamId,
            scope.organizationId.value,
            scope.environmentId.value,
          )
          if (!displayName) throw new Error('身份资料暂不可用')
          return [teamId, displayName] as const
        }),
      )
      return {
        users: Object.fromEntries(userEntries),
        teams: Object.fromEntries(teamEntries),
      }
    },
  }))
  const identityResponse = useScopeBoundListResponse(
    () => identityQuery.data.value,
    identityKey,
    identityEnabled,
  )
  const identities = computed(() =>
    identityEnabled.value && !identityQuery.isLoading.value && !identityQuery.error.value
      ? identityResponse.value
      : undefined,
  )
  const identityPending = computed(() => identityEnabled.value && identityQuery.isLoading.value)
  const identitiesUnavailable = computed(
    () =>
      Boolean(validatedWorkOrder.value) &&
      (!identityReferences.value.bounded ||
        Boolean(identityQuery.error.value) ||
        (identityEnabled.value && !identityPending.value && identityResponse.value === undefined)),
  )

  const deviceAssetId = computed(() =>
    validatedWorkOrder.value
      ? normalizeDeviceReference(validatedWorkOrder.value.deviceAssetId)
      : '',
  )
  const deviceEnabled = computed(() => enabled.value && Boolean(deviceAssetId.value))
  const deviceIdentity = computed(() =>
    deviceEnabled.value ? `${scope.scopeKey.value}:device:${deviceAssetId.value}` : '',
  )
  const deviceQuery = useQuery(() => ({
    ...getBusinessConsoleMasterDataResourceDetailQueryOptions({
      path: {
        resourceType: 'device-asset',
        code: deviceAssetId.value,
      },
      query: {
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
      },
    }),
    enabled: deviceEnabled.value,
  }))
  const deviceResponse = useScopeBoundListResponse(
    () => deviceQuery.data.value as DeviceAssetDetailEnvelope,
    deviceIdentity,
    deviceEnabled,
  )
  const validatedDevice = computed<BusinessConsoleMasterDataResourceDetail | undefined>(() => {
    const envelope = deviceResponse.value
    if (envelope?.success !== true) return undefined
    const item = envelope.data
    if (!item || typeof item !== 'object' || Array.isArray(item)) return undefined

    const presentationFields = [
      item.siteCode,
      item.plantCode,
      item.workshopCode,
      item.lineCode,
      item.workCenterCode,
      item.stationCode,
    ]
    if (
      !isNonBlankString(item.resourceType) ||
      !isNonBlankString(item.organizationId) ||
      !isNonBlankString(item.environmentId) ||
      !isNonBlankString(item.code) ||
      !isCanonicalGuid(item.deviceAssetId) ||
      !isNonBlankString(item.displayName) ||
      typeof item.active !== 'boolean' ||
      !isNonBlankString(item.snapshotVersion) ||
      !presentationFields.every(isNullableString)
    ) {
      return undefined
    }

    return item.resourceType.trim().toLowerCase() === 'device-asset' &&
      item.organizationId.trim() === scope.organizationId.value &&
      item.environmentId.trim() === scope.environmentId.value &&
      (item.code === deviceAssetId.value ||
        normalizeCanonicalGuid(item.deviceAssetId) === normalizeCanonicalGuid(deviceAssetId.value))
      ? { ...item, deviceAssetId: normalizeCanonicalGuid(item.deviceAssetId)! }
      : undefined
  })
  const deviceResponseAvailable = computed(() => deviceResponse.value !== undefined)
  const hasSuccessfulResponse = computed(() =>
    Boolean(
      validatedWorkOrder.value &&
      validatedDevice.value &&
      !detailQuery.isLoading.value &&
      !deviceQuery.isLoading.value &&
      !detailQuery.error.value &&
      !deviceQuery.error.value,
    ),
  )
  const workOrder = computed(() =>
    hasSuccessfulResponse.value ? validatedWorkOrder.value : undefined,
  )
  const device = computed(() => (hasSuccessfulResponse.value ? validatedDevice.value : undefined))
  const pending = computed(
    () =>
      enabled.value &&
      (detailQuery.isLoading.value ||
        Boolean(
          !detailQuery.error.value &&
          validatedWorkOrder.value &&
          (deviceQuery.isLoading.value ||
            (!deviceResponseAvailable.value && !deviceQuery.error.value)),
        )),
  )
  const hasFailedResponse = computed(
    () =>
      enabled.value &&
      !pending.value &&
      (Boolean(detailQuery.error.value) ||
        (detailEnvelope.value !== undefined && !validatedWorkOrder.value) ||
        Boolean(
          validatedWorkOrder.value &&
          (deviceQuery.error.value || (deviceResponseAvailable.value && !validatedDevice.value)),
        )),
  )
  const error = computed(() => detailQuery.error.value ?? deviceQuery.error.value)

  const refresh = async () => {
    if (!enabled.value) return
    await detailQuery.refetch()
    if (validatedWorkOrder.value && !detailQuery.error.value) {
      const enrichmentRefreshes: Promise<unknown>[] = []
      if (deviceEnabled.value) enrichmentRefreshes.push(deviceQuery.refetch())
      if (identityEnabled.value) enrichmentRefreshes.push(identityQuery.refetch())
      await Promise.all(enrichmentRefreshes)
    }
  }

  return {
    ...scope,
    workOrderId,
    enabled,
    workOrder,
    authoritativeWorkOrder,
    device,
    identities,
    identityPending,
    identitiesUnavailable,
    canReadDevice: scope.canReadDevice,
    pending,
    error,
    hasSuccessfulResponse,
    hasFailedResponse,
    authoritativePending,
    authoritativeError: computed(() => detailQuery.error.value),
    authoritativeHasSuccessfulResponse,
    authoritativeHasFailedResponse,
    deviceError: computed(() => deviceQuery.error.value),
    deviceHasFailedResponse: computed(
      () =>
        Boolean(validatedWorkOrder.value) &&
        !deviceQuery.isLoading.value &&
        Boolean(
          deviceQuery.error.value || (deviceResponseAvailable.value && !validatedDevice.value),
        ),
    ),
    refresh,
  }
}
