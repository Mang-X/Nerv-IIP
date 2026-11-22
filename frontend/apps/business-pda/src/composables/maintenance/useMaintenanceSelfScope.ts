import { computed } from 'vue'
import {
  MAINTENANCE_READ_MODEL_PERMISSIONS,
  canAccessMaintenanceWorkOrderReadModel,
} from '@/permissions/maintenanceReadModelAccess'
import { useAuthStore } from '@/stores/auth'
import { serializeMaintenanceKey } from './maintenancePublicIds'

export function useMaintenanceSelfScope() {
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
      ? serializeMaintenanceKey([
          organizationId.value,
          environmentId.value,
          'self',
          principalId.value,
        ])
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
