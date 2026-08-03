export const MAINTENANCE_READ_MODEL_PERMISSIONS = {
  workOrders: 'business.maintenance.work-orders.read',
  masterDataResources: 'business.masterdata.resources.read',
} as const

type PermissionCodes = readonly string[] | ReadonlySet<string>

export function canAccessMaintenanceWorkOrderReadModel(permissionCodes?: PermissionCodes) {
  const codes = permissionCodes instanceof Set ? permissionCodes : new Set(permissionCodes ?? [])
  return (
    codes.has(MAINTENANCE_READ_MODEL_PERMISSIONS.workOrders) &&
    codes.has(MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources)
  )
}
