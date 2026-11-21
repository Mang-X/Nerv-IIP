import {
  getBusinessConsoleMasterDataResourceDetail,
  getBusinessConsoleMaintenanceWorkOrder,
  listBusinessConsoleWorkers,
  type BusinessConsoleMasterDataResourceDetail,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  normalizeCanonicalGuid,
  normalizeMaintenanceDeviceReference,
  serializeMaintenanceKey,
} from './maintenancePublicIds'
import {
  nextMaintenanceQueryInstanceNonce,
  type FreshGenerationValue,
  useFreshGenerationProjection,
  useMaintenanceQueryGeneration,
} from './maintenanceQueryGuard'
import { useMaintenanceSelfScope } from './useMaintenanceSelfScope'
import {
  collectIdentityReferences,
  isAuthoritativeMaintenanceWorkOrderDetail,
  isCanonicalGuid,
  isNonBlankString,
  isNullableString,
  parseExactTeam,
  parseExactWorker,
  type DeviceAssetDetailEnvelope,
  type WorkOrderDetailEnvelope,
} from './maintenanceParsers'

export interface MaintenanceWorkOrderIdentityDirectory {
  users: Readonly<Record<string, string>>
  teams: Readonly<Record<string, string>>
}

export function useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId: MaybeRefOrGetter<string>) {
  const instanceNonce = nextMaintenanceQueryInstanceNonce()
  const scope = useMaintenanceSelfScope()
  const workOrderId = computed(() => normalizeCanonicalGuid(toValue(requestedWorkOrderId)) ?? '')
  const enabled = computed(() => scope.scopeReady.value && Boolean(workOrderId.value))
  const detailIdentity = computed(() =>
    enabled.value
      ? serializeMaintenanceKey({
          scope: [
            scope.organizationId.value,
            scope.environmentId.value,
            'self',
            scope.principalId.value,
          ],
          workOrderId: workOrderId.value,
          instanceNonce,
        })
      : '',
  )
  const detailGeneration = useMaintenanceQueryGeneration(detailIdentity, enabled)
  const detailQuery = useQuery(() => {
    const requestedGeneration = detailGeneration.value
    const requestedIdentity = detailIdentity.value
    const request = {
      path: { workOrderId: workOrderId.value },
      query: scope.queryScope(),
    }
    return {
      key: [
        {
          _id: 'maintenance-detail',
          request,
          generation: requestedGeneration,
          identity: requestedIdentity,
        },
      ],
      enabled: enabled.value,
      query: async () => {
        const { data } = await getBusinessConsoleMaintenanceWorkOrder({
          ...request,
          throwOnError: true,
        })
        return {
          generation: requestedGeneration,
          identity: requestedIdentity,
          value: data as WorkOrderDetailEnvelope,
        }
      },
    }
  })
  const detailEnvelope = useFreshGenerationProjection(
    () => detailQuery.data.value,
    detailIdentity,
    detailGeneration,
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
          deviceAssetId: normalizeMaintenanceDeviceReference(item.deviceAssetId),
        }
      : undefined
  })
  const authoritativePending = computed(
    () =>
      enabled.value &&
      (detailQuery.isLoading.value ||
        (!detailQuery.error.value && detailEnvelope.value === undefined)),
  )
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
      enabled.value &&
      Boolean(validatedWorkOrder.value) &&
      identityReferences.value.bounded &&
      identityReferences.value.userIds.length + identityReferences.value.teamIds.length > 0,
  )
  const identityKey = computed(() =>
    identityEnabled.value
      ? serializeMaintenanceKey({
          detail: [detailIdentity.value, detailGeneration.value],
          users: identityReferences.value.userIds,
          teams: identityReferences.value.teamIds,
        })
      : '',
  )
  const identityQuery = useQuery(() => {
    const requestedGeneration = detailGeneration.value
    const requestedIdentity = identityKey.value
    const userIds = [...identityReferences.value.userIds]
    const teamIds = [...identityReferences.value.teamIds]
    const organizationId = scope.organizationId.value
    const environmentId = scope.environmentId.value
    return {
      key: [
        {
          _id: 'maintenance-identities',
          detail: [detailIdentity.value, requestedGeneration],
          users: userIds,
          teams: teamIds,
          generation: requestedGeneration,
          identity: requestedIdentity,
        },
      ],
      enabled: identityEnabled.value,
      query: async (): Promise<FreshGenerationValue<MaintenanceWorkOrderIdentityDirectory>> => {
        const userEntries = await Promise.all(
          userIds.map(async (userId) => {
            const { data } = await listBusinessConsoleWorkers({
              query: {
                organizationId,
                environmentId,
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
          teamIds.map(async (teamId) => {
            const { data } = await getBusinessConsoleMasterDataResourceDetail({
              path: { resourceType: 'team', code: teamId },
              query: {
                organizationId,
                environmentId,
              },
              throwOnError: true,
            })
            const displayName = parseExactTeam(data, teamId, organizationId, environmentId)
            if (!displayName) throw new Error('身份资料暂不可用')
            return [teamId, displayName] as const
          }),
        )
        return {
          generation: requestedGeneration,
          identity: requestedIdentity,
          value: {
            users: Object.fromEntries(userEntries),
            teams: Object.fromEntries(teamEntries),
          },
        }
      },
    }
  })
  const identityResponse = useFreshGenerationProjection(
    () => identityQuery.data.value,
    identityKey,
    detailGeneration,
    identityEnabled,
  )
  const identities = computed(() =>
    authoritativeHasSuccessfulResponse.value &&
    identityEnabled.value &&
    !identityQuery.isLoading.value &&
    !identityQuery.error.value
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
      ? normalizeMaintenanceDeviceReference(validatedWorkOrder.value.deviceAssetId)
      : '',
  )
  const deviceEnabled = computed(
    () => enabled.value && Boolean(validatedWorkOrder.value) && Boolean(deviceAssetId.value),
  )
  const deviceIdentity = computed(() =>
    deviceEnabled.value
      ? serializeMaintenanceKey({
          detail: [detailIdentity.value, detailGeneration.value],
          deviceReference: deviceAssetId.value,
        })
      : '',
  )
  const deviceQuery = useQuery(() => {
    const requestedGeneration = detailGeneration.value
    const requestedIdentity = deviceIdentity.value
    const request = {
      path: {
        resourceType: 'device-asset',
        code: deviceAssetId.value,
      },
      query: {
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
      },
    }
    return {
      key: [
        {
          _id: 'device-detail',
          request,
          generation: requestedGeneration,
          identity: requestedIdentity,
        },
      ],
      enabled: deviceEnabled.value,
      query: async () => {
        const { data } = await getBusinessConsoleMasterDataResourceDetail({
          ...request,
          throwOnError: true,
        })
        return {
          generation: requestedGeneration,
          identity: requestedIdentity,
          value: data as DeviceAssetDetailEnvelope,
        }
      },
    }
  })
  const deviceResponse = useFreshGenerationProjection(
    () => deviceQuery.data.value,
    deviceIdentity,
    detailGeneration,
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
      item.active !== true ||
      item.retired === true ||
      (item.retired !== undefined && item.retired !== null && typeof item.retired !== 'boolean') ||
      (item.retiredOn !== undefined && item.retiredOn !== null) ||
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
          authoritativeHasSuccessfulResponse.value &&
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
    const requestedIdentity = detailIdentity.value
    const requestedGeneration = detailGeneration.value
    const requestedDeviceIdentity = deviceIdentity.value
    const requestedIdentityKey = identityKey.value
    await detailQuery.refetch()
    if (
      !enabled.value ||
      detailIdentity.value !== requestedIdentity ||
      detailGeneration.value !== requestedGeneration
    ) {
      return
    }
    if (validatedWorkOrder.value && !detailQuery.error.value) {
      const enrichmentRefreshes: Promise<unknown>[] = []
      if (deviceEnabled.value && deviceIdentity.value === requestedDeviceIdentity) {
        enrichmentRefreshes.push(deviceQuery.refetch())
      }
      if (identityEnabled.value && identityKey.value === requestedIdentityKey) {
        enrichmentRefreshes.push(identityQuery.refetch())
      }
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
