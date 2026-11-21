import {
  type BusinessConsoleMasterDataResourceDetail,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import { isStrictRfc3339DateTime } from '@/utils/strictRfc3339'
import {
  normalizeCanonicalGuid,
  normalizeMaintenanceDeviceReference,
} from '../maintenancePublicIds'

const MAX_IDENTITY_REFERENCES = 20

export type WorkOrderListEnvelope =
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

export type WorkOrderDetailEnvelope =
  | { success?: boolean; data?: BusinessConsoleMaintenanceWorkOrderItem | null }
  | undefined

export type DeviceAssetDetailEnvelope =
  | { success?: boolean; data?: BusinessConsoleMasterDataResourceDetail | null }
  | undefined

export interface WorkOrderPage {
  items: BusinessConsoleMaintenanceWorkOrderItem[]
  total: number
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

export function trimToUndefined(value: string) {
  return value.trim() || undefined
}

export function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

export function isNullableString(value: unknown): value is string | null | undefined {
  return value === null || value === undefined || typeof value === 'string'
}

function isValidVersion(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

export function isCanonicalGuid(value: unknown): value is string {
  return normalizeCanonicalGuid(value) !== undefined
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
    isNullableString((value as BusinessConsoleMaintenanceWorkOrderItem).workOrderNo) &&
    isNonBlankString((value as BusinessConsoleMaintenanceWorkOrderItem).deviceAssetId) &&
    isNonBlankString((value as BusinessConsoleMaintenanceWorkOrderItem).priority) &&
    isNonBlankString((value as BusinessConsoleMaintenanceWorkOrderItem).status) &&
    isStrictRfc3339DateTime((value as BusinessConsoleMaintenanceWorkOrderItem).openedAtUtc) &&
    isValidVersion((value as BusinessConsoleMaintenanceWorkOrderItem).version) &&
    (value as BusinessConsoleMaintenanceWorkOrderItem).assignedTechnicianUserId === principalId,
  )
}

export function parseWorkOrderPage(
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
    deviceAssetId: normalizeMaintenanceDeviceReference(item.deviceAssetId!),
  }))
  if (items.length > take || total < skip + items.length || (skip < total && items.length === 0)) {
    throw new Error('维修工单分页信息不一致，请重试。')
  }
  return { items, total }
}

export function isAuthoritativeMaintenanceWorkOrderDetail(
  value: BusinessConsoleMaintenanceWorkOrderItem | null | undefined,
  requestedWorkOrderId: string,
  principalId: string,
): value is AuthoritativeMaintenanceWorkOrderDetail {
  const terminalStatus = Boolean(
    value &&
    isNonBlankString(value.status) &&
    ['closed', 'cancelled'].includes(value.status.trim().toLowerCase()),
  )
  const terminalBlock = Boolean(
    value &&
    Array.isArray(value.blockReasons) &&
    value.blockReasons.some(
      (reason) => isNonBlankString(reason) && reason.trim().toLowerCase() === 'terminal-status',
    ),
  )
  return Boolean(
    value &&
    typeof value === 'object' &&
    isCanonicalGuid(requestedWorkOrderId) &&
    isCanonicalGuid(value.workOrderId) &&
    normalizeCanonicalGuid(value.workOrderId) === normalizeCanonicalGuid(requestedWorkOrderId) &&
    isNullableString(value.sourceReferenceId) &&
    isNullableString(value.workOrderNo) &&
    isNonBlankString(value.deviceAssetId) &&
    isNonBlankString(value.priority) &&
    isNonBlankString(value.status) &&
    isStrictRfc3339DateTime(value.openedAtUtc) &&
    isValidVersion(value.version) &&
    Array.isArray(value.allowedActions) &&
    value.allowedActions.every(isNonBlankString) &&
    Array.isArray(value.blockReasons) &&
    value.blockReasons.every(isNonBlankString) &&
    terminalStatus === terminalBlock &&
    (!terminalStatus || value.allowedActions.length === 0) &&
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

export function collectIdentityReferences(
  workOrder: AuthoritativeMaintenanceWorkOrderDetail | undefined,
) {
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

export function parseExactWorker(envelope: unknown, requestedUserId: string) {
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

export function parseExactTeam(
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
