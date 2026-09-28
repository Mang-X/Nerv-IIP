import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import EquipmentDetailPage from './[deviceAssetId].vue'

// 设备详情页：设备身份（编码 / 公开 ID）与「从未接入采集 vs 采集中断」的上屏口径。
// 原因文案走真实的原因字典（describeEquipmentReason），不桩掉。

const PUBLIC_ID = '019fbb41-5555-7555-8555-555555555555'
const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i

const state = vi.hoisted(() => ({
  route: { params: { deviceAssetId: '' } },
  filters: { deviceAssetId: '' },
  currentState: undefined as Record<string, unknown> | undefined,
  availabilityWindows: [] as Array<Record<string, unknown>>,
}))

// 主数据名录桩：按（编码、公开 ID、名称）小表查，与真实实现同口径——编码和公开 ID 都能解析。
vi.mock('@/composables/useMasterDataDisplayNames', () => {
  const devices = [
    { code: 'EQ00001', publicId: '019fbb41-5555-7555-8555-555555555555', name: '五轴加工中心' },
  ]
  const find = (reference?: string | null) =>
    devices.find((d) => d.code === reference || d.publicId === reference)
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: (reference?: string | null) => find(reference)?.name,
      resolveDeviceCode: (reference?: string | null) => find(reference)?.code,
      resolveWorkCenter: () => undefined,
      resolveUom: () => undefined,
      resolveUser: () => undefined,
    }),
  }
})

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => state.route,
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

vi.mock('@/composables/useBusinessEquipment', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/useBusinessEquipment')>()),
  useBusinessEquipmentDevice: () => ({
    activeAlarms: computed(() => []),
    availabilityWindows: computed(() => state.availabilityWindows),
    device: computed(() => ({ currentState: state.currentState })),
    deviceError: shallowRef(),
    devicePending: shallowRef(false),
    filters: state.filters,
    refreshDevice: vi.fn(),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: [] } }),
}))

vi.mock('@/composables/useBusinessDeviceControl', () => ({
  deviceControlApprovalLabel: () => '',
  deviceControlCommandTypeLabel: () => '',
  deviceControlStatusLabel: () => '',
  deviceControlStatusTone: () => 'neutral',
  deviceReceiptLabel: () => '—',
  useBusinessDeviceControlCommands: () => ({
    commands: computed(() => []),
    commandsError: shallowRef(),
    commandsPending: shallowRef(false),
    commandsTotal: computed(() => 0),
    historyFilters: { deviceAssetId: '', status: '', skip: 0, take: 20 },
  }),
}))

vi.mock('@/composables/useBusinessTelemetry', () => {
  const windowFilters = () => ({ deviceAssetId: '', windowStartUtc: '', windowEndUtc: '' })
  return {
    describeTelemetryOeeDegradation: (reason: string) => reason,
    describeTelemetryOeeLimitations: () => '',
    formatOeeQuantity: () => '无数据',
    formatOeeRate: () => '无数据',
    useBusinessTelemetryHistory: () => ({
      filters: windowFilters(),
      historyError: shallowRef(),
      historyPending: shallowRef(false),
      visibleHistoryItems: computed(() => []),
    }),
    useBusinessEquipmentHealth: () => ({
      health: computed(() => undefined),
      healthError: shallowRef(),
      healthPending: shallowRef(false),
      refreshHealth: vi.fn(),
    }),
    useBusinessTelemetryOee: () => ({
      filters: windowFilters(),
      oee: computed(() => undefined),
      oeeError: shallowRef(),
      oeePending: shallowRef(false),
      runtimeAvailabilityError: shallowRef(),
    }),
    useBusinessTelemetryRuntimeHours: () => ({
      runtimeHours: computed(() => undefined),
      totalRuntimeHours: computed(() => undefined),
      hasRuntimeSamples: computed(() => false),
      runtimeHoursError: shallowRef(),
      runtimeHoursPending: shallowRef(false),
      runtimeHoursEnabled: computed(() => true),
      refreshRuntimeHours: vi.fn(),
    }),
    useMaintenancePlanRuntimeRemaining: () => ({
      remainingByPlanId: computed(() => ({})),
      remainingPending: shallowRef(false),
      refreshRemaining: vi.fn(),
    }),
  }
})

vi.mock('@/composables/useBusinessMaintenance', () => {
  const list = <K extends string>(key: K) => ({
    [key]: computed(() => []),
    [`${key}Error`]: shallowRef(),
    [`${key}Pending`]: shallowRef(false),
  })
  return {
    useMaintenanceAvailabilityWindows: () => ({
      availabilityError: shallowRef(),
      availabilityPending: shallowRef(false),
      availabilityWindows: computed(() => []),
      filters: { deviceAssetIds: '' },
    }),
    useMaintenanceReliability: () => ({
      filters: { deviceAssetId: '' },
      reliability: computed(() => undefined),
      reliabilityError: shallowRef(),
      reliabilityPending: shallowRef(false),
    }),
    useMaintenanceWorkOrders: () => list('workOrders'),
    useMaintenancePlans: () => ({
      ...list('plans'),
      filters: { deviceAssetId: '' },
      refreshPlans: vi.fn(),
    }),
    useMaintenanceInspections: () => list('inspections'),
    useMaintenanceSpareParts: () => list('spareParts'),
  }
})

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  DeviceControlSheet: { template: '<div />' },
  DeviceTelemetryPointsSheet: { template: '<div />' },
  EquipmentHealthCard: { template: '<section />' },
}

// 可用性窗口表的一行：状态、原因、工作中心、开始、结束、关联业务、替代设备。
function availabilityRow(wrapper: ReturnType<typeof mount>, reasonText: string) {
  const row = wrapper.findAll('tr.nv-dt-row').find((tr) => tr.text().includes(reasonText))
  expect(row, `可用性窗口表里没有「${reasonText}」这一行`).toBeDefined()
  const cells = row!.findAll('td').map((td) => td.text())
  return { reason: cells[1], relatedBusiness: cells[5], substitutes: cells[6] }
}

function openDevice(reference: string) {
  state.route = reactive({ params: { deviceAssetId: reference } })
  state.filters = reactive({ deviceAssetId: '' })
  return mount(EquipmentDetailPage, { global: { stubs } })
}

describe('设备详情：设备身份与采集接入状态', () => {
  beforeEach(() => {
    state.currentState = undefined
    state.availabilityWindows = []
  })

  it('新注册、从未接入采集的设备按公开 ID 打开：标题是名称与编码，窗口原因是尚未接入采集', () => {
    state.currentState = {
      deviceAssetId: PUBLIC_ID,
      currentState: null,
      stateOccurredAtUtc: null,
      isSourceFresh: false,
    }
    state.availabilityWindows = [
      {
        deviceAssetId: PUBLIC_ID,
        availabilityStatus: 'unknown',
        reasonCode: 'equipment.sourceNotConnected',
        sourceType: 'stale-source',
        sourceReferenceId: PUBLIC_ID,
        sourceReferenceLabel: PUBLIC_ID,
      },
    ]

    const wrapper = openDevice(PUBLIC_ID)
    const text = wrapper.text()

    expect(text).toContain('设备详情：五轴加工中心（EQ00001）')
    const row = availabilityRow(wrapper, '尚未接入采集')
    expect(row.reason).toContain('为设备配置采集连接后即可看到运行状态')
    expect(row.relatedBusiness).toBe('EQ00001')
    expect(text).not.toContain('采集数据过期')
    expect(text).not.toMatch(UUID_PATTERN)
  })

  it('有过状态、采集中断的设备：窗口原因仍是采集数据过期，不说成尚未接入', () => {
    state.currentState = {
      deviceAssetId: 'EQ00001',
      currentState: 'running',
      stateOccurredAtUtc: '2026-09-26T08:00:00Z',
      isSourceFresh: false,
    }
    state.availabilityWindows = [
      {
        deviceAssetId: 'EQ00001',
        availabilityStatus: 'unknown',
        reasonCode: 'equipment.sourceStale',
        sourceType: 'stale-source',
        sourceReferenceId: '019fbb41-6666-7666-8666-666666666666',
        sourceReferenceLabel: 'EQ00001',
      },
    ]

    const wrapper = openDevice('EQ00001')

    const row = availabilityRow(wrapper, '采集数据过期')
    expect(row.relatedBusiness).toBe('EQ00001')
    expect(wrapper.text()).toContain('采集过期')
    expect(wrapper.text()).not.toContain('尚未接入')
  })

  it('只上报样本、不带状态的设备（没有状态快照）采集中断时：显示采集过期，不说成尚未接入', () => {
    state.currentState = {
      deviceAssetId: 'EQ00001',
      currentState: null,
      stateOccurredAtUtc: null,
      isSourceFresh: false,
    }
    state.availabilityWindows = [
      {
        deviceAssetId: 'EQ00001',
        availabilityStatus: 'unknown',
        reasonCode: 'equipment.sourceStale',
        sourceType: 'stale-source',
        sourceReferenceId: 'EQ00001',
        sourceReferenceLabel: 'EQ00001',
      },
    ]

    const wrapper = openDevice('EQ00001')

    availabilityRow(wrapper, '采集数据过期')
    expect(wrapper.text()).toContain('采集过期')
    expect(wrapper.text()).not.toContain('尚未接入')
  })

  it('只上报样本、不带状态的设备正在采集时：显示采集正常，不说成尚未接入', () => {
    state.currentState = {
      deviceAssetId: 'EQ00001',
      currentState: null,
      stateOccurredAtUtc: null,
      isSourceFresh: true,
    }
    state.availabilityWindows = []

    const text = openDevice('EQ00001').text()

    expect(text).toContain('采集正常')
    expect(text).not.toContain('尚未接入')
  })

  it('替代设备解析不出来时显示「—」，不显示「无设备」也不显示原始 ID', () => {
    state.currentState = { deviceAssetId: 'EQ00001', currentState: 'running', isSourceFresh: true }
    state.availabilityWindows = [
      {
        deviceAssetId: 'EQ00001',
        availabilityStatus: 'unavailable',
        reasonCode: 'equipment.maintenanceWindow',
        sourceType: 'maintenance-window',
        sourceReferenceLabel: 'MWO000001',
        substituteDeviceAssetIds: ['019fbb41-7777-7777-8777-777777777777', PUBLIC_ID],
      },
    ]

    const row = availabilityRow(openDevice('EQ00001'), '维修保养占用')

    expect(row.relatedBusiness).toBe('MWO000001')
    expect(row.substitutes).toBe('—、五轴加工中心')
  })
})
