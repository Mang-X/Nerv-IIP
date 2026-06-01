import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  getBusinessConsoleEquipmentAvailabilityQueryOptions,
  getBusinessConsoleEquipmentDeviceQueryOptions,
  getBusinessConsoleEquipmentOverviewQueryOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
} from '@nerv-iip/api-client'
import {
  describeEquipmentReason,
  equipmentStatusTone,
  useBusinessEquipmentAlarms,
  useBusinessEquipmentAvailability,
  useBusinessEquipmentDevice,
  useBusinessEquipmentOverview,
} from './useBusinessEquipment'
import { useBusinessContextStore } from '@/stores/businessContext'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleEquipmentAvailabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleEquipmentAvailability' }],
    query: vi.fn(),
  })),
  getBusinessConsoleEquipmentDeviceQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleEquipmentDevice' }],
    query: vi.fn(),
  })),
  getBusinessConsoleEquipmentOverviewQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleEquipmentOverview' }],
    query: vi.fn(),
  })),
  listBusinessConsoleEquipmentAlarmsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleEquipmentAlarms' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('business equipment composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
  })

  it('maps shared equipment reason codes to Chinese labels and next steps', () => {
    expect(describeEquipmentReason('equipment.activeAlarm')).toMatchObject({
      code: 'equipment.activeAlarm',
      label: '设备报警未解除',
      nextStep: '处理并解除设备报警后重新检查',
    })
    expect(describeEquipmentReason('equipment.stateUnavailable')).toMatchObject({
      label: '设备状态不可运行',
      nextStep: '确认设备恢复运行后重新检查',
    })
    expect(describeEquipmentReason('equipment.downtime')).toMatchObject({
      label: '设备停机中',
      nextStep: '关闭停机事件或改派可用设备',
    })
    expect(describeEquipmentReason('equipment.maintenanceWindow')).toMatchObject({
      label: '维修保养占用',
      nextStep: '调整维修窗口、等待释放或选择替代设备',
    })
    expect(describeEquipmentReason('equipment.inspectionRequired')).toMatchObject({
      label: '点检未通过',
      nextStep: '完成点检并确认结果后重新检查',
    })
    expect(describeEquipmentReason('equipment.sourceStale')).toMatchObject({
      label: '采集数据过期',
      nextStep: '检查采集连接并刷新设备状态',
    })
    expect(describeEquipmentReason('equipment.tagMappingMissing')).toMatchObject({
      label: '采集点未配置',
      nextStep: '补齐设备采集点映射',
    })
    expect(describeEquipmentReason('equipment.noEligibleSubstitute')).toMatchObject({
      label: '无可替代设备',
      nextStep: '调整排程或维护设备能力配置',
    })
    expect(describeEquipmentReason('vendor.custom')).toMatchObject({
      code: 'vendor.custom',
      label: 'vendor.custom',
      nextStep: '查看设备详情并处理来源业务单据',
    })
  })

  it('maps equipment statuses to business tones', () => {
    expect(equipmentStatusTone('running')).toBe('success')
    expect(equipmentStatusTone('ready')).toBe('success')
    expect(equipmentStatusTone('idle')).toBe('success')
    expect(equipmentStatusTone('faulted')).toBe('danger')
    expect(equipmentStatusTone('stopped')).toBe('danger')
    expect(equipmentStatusTone('offline')).toBe('danger')
    expect(equipmentStatusTone('down')).toBe('danger')
    expect(equipmentStatusTone(undefined)).toBe('muted')
    expect(equipmentStatusTone('calibrating')).toBe('muted')
  })

  it('loads equipment overview with default context and safe arrays', () => {
    coladaState.queryDataById.set('getBusinessConsoleEquipmentOverview', {
      success: true,
      data: {
        devices: [{ deviceAssetId: 'DEV-OIL-01', currentState: 'running' }],
        activeBlocks: [{ deviceAssetId: 'DEV-OIL-01', reasonCode: 'equipment.activeAlarm' }],
      },
    })

    const { activeBlocks, devices } = useBusinessEquipmentOverview()

    expect(getBusinessConsoleEquipmentOverviewQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetIds: 'DEV-OIL-01,DEV-PACK-01',
      },
    })
    expect(devices.value).toEqual([{ deviceAssetId: 'DEV-OIL-01', currentState: 'running' }])
    expect(activeBlocks.value).toEqual([
      { deviceAssetId: 'DEV-OIL-01', reasonCode: 'equipment.activeAlarm' },
    ])
  })

  it('uses the patched business context for equipment query options', () => {
    const businessContext = useBusinessContextStore()
    businessContext.patchContext({
      organizationId: 'org-review',
      environmentId: 'env-review',
    })

    useBusinessEquipmentOverview()
    useBusinessEquipmentAvailability()
    useBusinessEquipmentDevice('DEV-PATCHED-01')
    useBusinessEquipmentAlarms()

    expect(getBusinessConsoleEquipmentOverviewQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-review',
        environmentId: 'env-review',
      }),
    })
    expect(getBusinessConsoleEquipmentAvailabilityQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-review',
        environmentId: 'env-review',
      }),
    })
    expect(getBusinessConsoleEquipmentDeviceQueryOptions).toHaveBeenCalledWith({
      path: { deviceAssetId: 'DEV-PATCHED-01' },
      query: {
        organizationId: 'org-review',
        environmentId: 'env-review',
      },
    })
    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-review',
        environmentId: 'env-review',
      },
    })
  })

  it('loads availability windows for a default work window', () => {
    coladaState.queryDataById.set('getBusinessConsoleEquipmentAvailability', {
      success: true,
      data: {
        items: [{ deviceAssetId: 'DEV-OIL-01', reasonCode: 'equipment.noEligibleSubstitute' }],
      },
    })

    const { availabilityWindows } = useBusinessEquipmentAvailability()

    expect(getBusinessConsoleEquipmentAvailabilityQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetIds: 'DEV-OIL-01,DEV-PACK-01',
      }),
    })
    expect(availabilityWindows.value).toEqual([
      { deviceAssetId: 'DEV-OIL-01', reasonCode: 'equipment.noEligibleSubstitute' },
    ])
  })

  it('loads a device detail and exposes active alarms and availability windows', () => {
    coladaState.queryDataById.set('getBusinessConsoleEquipmentDevice', {
      success: true,
      data: {
        currentState: {
          deviceAssetId: 'DEV-OIL-01',
          currentState: 'faulted',
          activeAlarms: [{ alarmEventId: 'alarm-1', alarmCode: 'TEMP_HIGH' }],
        },
        availability: {
          items: [{ deviceAssetId: 'DEV-OIL-01', reasonCode: 'equipment.activeAlarm' }],
        },
      },
    })

    const { activeAlarms, availabilityWindows, device, filters } = useBusinessEquipmentDevice()

    expect(getBusinessConsoleEquipmentDeviceQueryOptions).toHaveBeenCalledWith({
      path: { deviceAssetId: filters.deviceAssetId },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
    expect(device.value?.currentState?.currentState).toBe('faulted')
    expect(activeAlarms.value).toHaveLength(1)
    expect(availabilityWindows.value).toHaveLength(1)
  })

  it('loads alarm rows and defaults unsuccessful envelopes to an empty array', () => {
    coladaState.queryDataById.set('listBusinessConsoleEquipmentAlarms', {
      success: true,
      data: {
        items: [{ alarmEventId: 'alarm-1', deviceAssetId: 'DEV-OIL-01', severity: 'critical' }],
      },
    })

    const active = useBusinessEquipmentAlarms()
    expect(listBusinessConsoleEquipmentAlarmsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
    expect(active.alarms.value).toHaveLength(1)

    coladaState.queryDataById.set('listBusinessConsoleEquipmentAlarms', { success: false })
    const failed = useBusinessEquipmentAlarms()
    expect(failed.alarms.value).toEqual([])
  })
})
