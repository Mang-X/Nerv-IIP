import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions,
  getBusinessConsoleEquipmentAvailabilityQueryOptions,
  getBusinessConsoleEquipmentDeviceQueryOptions,
  getBusinessConsoleEquipmentOverviewQueryOptions,
  listBusinessConsoleEquipmentAlarmsQueryOptions,
  shelveBusinessConsoleEquipmentAlarmMutationOptions,
  unshelveBusinessConsoleEquipmentAlarmMutationOptions,
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
  mutations: [] as Array<ReturnType<typeof vi.fn>>,
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  acknowledgeBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
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
  shelveBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  unshelveBusinessConsoleEquipmentAlarmMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  // --- pulled in transitively via useBusinessMasterData (设备列表) ---
  addBusinessConsoleTeamMemberMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  assignBusinessConsolePersonnelSkillMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleDepartmentMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleProductionLineMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleShiftMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleSiteMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleBusinessPartnerMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleReferenceDataCodeMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleSkuMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleUnitOfMeasureMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleUomConversionMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleTeamMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleWorkCalendarMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleWorkCenterMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleWorkshopMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  disableBusinessConsoleMasterDataResourceMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  enableBusinessConsoleMasterDataResourceMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  getBusinessConsoleMasterDataResourceDetail: vi.fn(),
  listBusinessConsoleMasterDataResourcesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMasterDataResources' }],
    query: vi.fn(),
  })),
  listBusinessConsolePersonnelSkillMatrixQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
  listBusinessConsoleSkusQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
  listBusinessConsoleTeamMembersQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
  listBusinessConsoleWorkersQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
  listBusinessConsoleWorkshopsQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
  registerBusinessConsoleDeviceAssetMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  removeBusinessConsoleTeamMemberMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  updateBusinessConsoleMasterDataResourceMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn(() => {
    const mutateAsync = vi.fn().mockResolvedValue({ success: true, data: { alarmEventId: 'alarm-1' } })
    coladaState.mutations.push(mutateAsync)
    return {
      mutateAsync,
    }
  }),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

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
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.clearAllMocks()
    coladaState.mutations.length = 0
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
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

  it('keeps equipment overview empty and disabled without hard-coded seed device defaults', () => {
    const { activeBlocks, devices } = useBusinessEquipmentOverview()

    expect(getBusinessConsoleEquipmentOverviewQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetIds: '',
      },
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsoleEquipmentOverview')?.enabled).toBe(false)
    expect(devices.value).toEqual([])
    expect(activeBlocks.value).toEqual([])
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

  it('keeps availability query disabled until a device scope is entered', () => {
    const { availabilityWindows } = useBusinessEquipmentAvailability()

    expect(getBusinessConsoleEquipmentAvailabilityQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetIds: '',
      }),
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsoleEquipmentAvailability')?.enabled).toBe(false)
    expect(availabilityWindows.value).toEqual([])
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

  it('posts alarm lifecycle actions with current business context', async () => {
    const active = useBusinessEquipmentAlarms()

    await active.acknowledgeAlarm('alarm-1', 'operator-a')
    await active.shelveAlarm('alarm-1', 'operator-a', 45, 'maintenance window')
    await active.unshelveAlarm('alarm-1')

    expect(acknowledgeBusinessConsoleEquipmentAlarmMutationOptions).toHaveBeenCalled()
    expect(shelveBusinessConsoleEquipmentAlarmMutationOptions).toHaveBeenCalled()
    expect(unshelveBusinessConsoleEquipmentAlarmMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      path: { alarmEventId: 'alarm-1' },
      body: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        acknowledgedBy: 'operator-a',
      }),
    })
    expect(coladaState.mutations[1]).toHaveBeenCalledWith({
      path: { alarmEventId: 'alarm-1' },
      body: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        durationMinutes: 45,
        reason: 'maintenance window',
        shelvedBy: 'operator-a',
      }),
    })
    expect(coladaState.mutations[2]).toHaveBeenCalledWith({
      path: { alarmEventId: 'alarm-1' },
      body: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
      }),
    })
  })
})
