import { describe, expect, it } from 'vitest'

import {
  MAINTENANCE_READ_MODEL_PERMISSIONS,
  canAccessMaintenanceWorkOrderReadModel,
} from './maintenanceReadModelAccess'

describe('maintenance work-order read-model permissions', () => {
  it('requires both work-order and master-data resource permissions', () => {
    expect(canAccessMaintenanceWorkOrderReadModel()).toBe(false)
    expect(
      canAccessMaintenanceWorkOrderReadModel([MAINTENANCE_READ_MODEL_PERMISSIONS.workOrders]),
    ).toBe(false)
    expect(
      canAccessMaintenanceWorkOrderReadModel(
        new Set([MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources]),
      ),
    ).toBe(false)
    expect(
      canAccessMaintenanceWorkOrderReadModel([
        MAINTENANCE_READ_MODEL_PERMISSIONS.workOrders,
        MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources,
      ]),
    ).toBe(true)
  })
})
