import { describe, expect, it } from 'vitest'

import {
  maintenanceDeviceLocation,
  maintenanceDeviceTitle,
  maintenanceWorkOrderTitle,
} from './maintenanceWorkOrderPresentation'

describe('maintenance work-order presentation', () => {
  it.each([
    [undefined, '维修工单'],
    [null, '维修工单'],
    ['', '维修工单'],
    ['019f0000-0000-7000-8000-000000000101', '维修工单'],
    ['PM-RUNTIME-HTTP:runtime:2.5:1', '维修工单'],
    ['MWO-2026-0042', 'MWO-2026-0042'],
  ])('uses only a readable source reference as the title: %j', (reference, expected) => {
    expect(maintenanceWorkOrderTitle(reference)).toBe(expected)
  })

  it('does not throw when untrusted device presentation fields have malformed shapes', () => {
    const workOrder = { deviceAssetId: 'DEV-CNC-01' }
    const device = {
      displayName: 42,
      code: {},
      siteCode: {},
      plantCode: 42,
      workshopCode: [],
      lineCode: false,
      workCenterCode: {},
      stationCode: 42,
    }

    expect(() => maintenanceDeviceTitle(workOrder as never, device as never)).not.toThrow()
    expect(() => maintenanceDeviceLocation(device as never)).not.toThrow()
    expect(maintenanceDeviceTitle(workOrder as never, device as never)).toBe('设备资料不可用')
    expect(maintenanceDeviceLocation(device as never)).toBe('位置未登记')
  })
})
