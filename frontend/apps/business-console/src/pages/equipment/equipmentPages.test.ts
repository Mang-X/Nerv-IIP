import { mount } from '@vue/test-utils'
import type {
  BusinessConsoleMaintenanceInspectionItem,
  BusinessConsoleMaintenancePlanItem,
  BusinessConsoleMaintenanceSparePartItem,
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleTelemetryHistoryItem,
} from '@nerv-iip/api-client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'

import EquipmentAlarmsPage from './alarms.vue'
import EquipmentDetailPage from './[deviceAssetId].vue'
import EquipmentIndexPage from './index.vue'

const routeState = vi.hoisted(() => ({
  route: undefined as { params: { deviceAssetId: string } } | undefined,
}))

const equipmentComposableState = vi.hoisted(() => ({
  deviceFilters: { deviceAssetId: 'DEV-OIL-01' },
  refreshDevice: vi.fn(),
}))

const authState = vi.hoisted(() => ({
  permissionCodes: [
    'business.iiot.alarms.read',
    'business.iiot.alarms.write',
    'business.iiot.device-control.read',
    'business.iiot.device-control.write',
  ] as string[],
}))

const deviceControlState = vi.hoisted(() => ({
  commands: [
    {
      commandId: 'cmd-1',
      operationTaskId: 'op-1',
      deviceAssetId: 'DEV-OIL-01',
      commandType: 'write-tag',
      tagKey: 'spindle.speed',
      value: '80',
      requestedBy: 'operator-a',
      status: 'completed',
      approvalStatus: 'approved',
      correlationId: 'corr-1',
      requestedAtUtc: '2026-07-01T06:00:00Z',
    },
  ],
}))

// Client-derived per-plan remaining runtime hours; configurable per test to drive mixed-status cases.
const runtimeRemainingState = vi.hoisted(() => ({
  map: {} as Record<string, { status: string; hours?: number }>,
}))

// Cumulative runtime-hours read; configurable so a no-samples device can be exercised.
const runtimeHoursState = vi.hoisted(() => ({ total: 720, hasSamples: true }))

const reviewFixture = vi.hoisted(() => {
  const historyItems = [
    {
      itemType: 'alarm',
      tagKey: 'temperature',
      value: 'ALM-TEMP-HIGH',
      occurredAtUtc: '2026-07-01T01:20:00Z',
    },
    {
      itemType: 'state',
      tagKey: 'runtime',
      value: 'running',
      occurredAtUtc: '2026-07-01T02:00:00Z',
    },
    {
      itemType: 'sample',
      tagKey: 'pressure',
      value: '0.62MPa',
      occurredAtUtc: '2026-07-01T03:00:00Z',
    },
    {
      itemType: 'sample',
      tagKey: 'vibration',
      value: '2.4mm/s',
      occurredAtUtc: '2026-07-01T04:00:00Z',
    },
    { itemType: 'state', tagKey: 'runtime', value: 'idle', occurredAtUtc: '2026-07-01T05:00:00Z' },
    {
      itemType: 'sample',
      tagKey: 'temperature',
      value: '72.3C',
      occurredAtUtc: '2026-07-01T06:00:00Z',
    },
  ] satisfies BusinessConsoleTelemetryHistoryItem[]

  const workOrders = [
    {
      workOrderId: 'mwo-1',
      deviceAssetId: 'DEV-OIL-01',
      status: 'open',
      openedAtUtc: '2026-07-01T01:00:00Z',
    },
    {
      workOrderId: 'mwo-2',
      deviceAssetId: 'DEV-OIL-01',
      status: 'open',
      openedAtUtc: '2026-07-01T02:00:00Z',
    },
    {
      workOrderId: 'mwo-3',
      deviceAssetId: 'DEV-OIL-01',
      status: 'open',
      openedAtUtc: '2026-07-01T03:00:00Z',
    },
    {
      workOrderId: 'mwo-4',
      deviceAssetId: 'DEV-OIL-01',
      status: 'open',
      openedAtUtc: '2026-07-01T04:00:00Z',
    },
    {
      workOrderId: 'mwo-5',
      deviceAssetId: 'DEV-OIL-01',
      status: 'open',
      openedAtUtc: '2026-07-01T05:00:00Z',
    },
    {
      workOrderId: 'mwo-6',
      deviceAssetId: 'DEV-OIL-01',
      status: 'open',
      openedAtUtc: '2026-07-01T06:00:00Z',
    },
  ] satisfies BusinessConsoleMaintenanceWorkOrderItem[]

  const plans = [
    {
      planId: 'plan-1',
      deviceAssetId: 'DEV-OIL-01',
      planCode: 'PM-CNC-MONTHLY',
      interval: 'P30D',
      startsOn: '2026-07-01',
      nextDueOn: '2026-07-31',
      lastGeneratedRuntimeHours: 0,
    },
    {
      planId: 'plan-2',
      deviceAssetId: 'DEV-OIL-01',
      planCode: 'PM-CNC-RUNTIME',
      interval: null, // runtime-only: no calendar trigger
      startsOn: '2026-06-01',
      nextDueOn: null,
      runtimeHourInterval: 1000,
      nextDueRuntimeHours: 1000,
      lastGeneratedRuntimeHours: 0,
    },
    {
      // A second runtime plan on the same device — drives the mixed-status aggregation cases.
      planId: 'plan-3',
      deviceAssetId: 'DEV-OIL-01',
      planCode: 'PM-CNC-RUNTIME-2',
      interval: null,
      startsOn: '2026-06-01',
      nextDueOn: null,
      runtimeHourInterval: 2000,
      nextDueRuntimeHours: 2000,
      lastGeneratedRuntimeHours: 0,
    },
  ] satisfies BusinessConsoleMaintenancePlanItem[]

  const inspections = [
    {
      inspectionId: 'insp-6',
      workOrderId: 'mwo-6',
      inspector: '设备保全班',
      result: 'passed',
      inspectedAtUtc: '2026-07-01T07:00:00Z',
    },
  ] satisfies BusinessConsoleMaintenanceInspectionItem[]

  const spareParts = [
    {
      sparePartLineId: 'sp-1',
      workOrderId: 'mwo-1',
      deviceAssetId: 'DEV-OIL-01',
      skuCode: 'BEARING-6205',
      quantity: 2,
      uomCode: 'EA',
    },
  ] satisfies BusinessConsoleMaintenanceSparePartItem[]

  return { historyItems, inspections, plans, spareParts, workOrders }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  routeState.route = reactive({ params: { deviceAssetId: 'DEV-OIL-01' }, query: {} })

  return {
    ...actual,
    useRoute: () => routeState.route,
    useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  }
})

vi.mock('@/composables/useBusinessEquipment', () => ({
  describeEquipmentReason: (code: string) => ({
    code,
    label: code || '未知',
    nextStep: '查看设备详情并处理来源业务单据',
  }),
  equipmentStatusTone: () => 'success',
  useBusinessEquipmentAlarms: () => ({
    acknowledgeAlarm: vi.fn(),
    alarms: computed(() => []),
    alarmsError: shallowRef(),
    alarmsPending: shallowRef(false),
    refreshAlarms: vi.fn(),
    shelveAlarm: vi.fn(),
    unshelveAlarm: vi.fn(),
  }),
  useBusinessEquipmentDevice: () => ({
    activeAlarms: computed(() => []),
    availabilityWindows: computed(() => []),
    device: computed(() => ({
      currentState: {
        deviceAssetId: 'DEV-OIL-01',
        currentState: 'running',
        isSourceFresh: true,
      },
    })),
    deviceError: shallowRef(),
    devicePending: shallowRef(false),
    filters: equipmentComposableState.deviceFilters,
    refreshDevice: equipmentComposableState.refreshDevice,
  }),
  useBusinessEquipmentOverview: () => ({
    activeBlocks: computed(() => []),
    devices: computed(() => []),
    filters: {
      deviceAssetIds: 'DEV-OIL-01,DEV-PACK-01',
    },
    overviewError: shallowRef(),
    overviewPending: shallowRef(false),
    refreshOverview: vi.fn(),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      loginName: 'operator-a',
      permissionCodes: authState.permissionCodes,
    },
  }),
}))

vi.mock('@/composables/useBusinessDeviceControl', () => ({
  deviceControlApprovalLabel: (value?: string | null) => value ?? '未知',
  deviceControlCommandTypeLabel: (value?: string | null) =>
    value === 'write-tag' ? '写值' : (value ?? '未知命令'),
  deviceControlStatusLabel: (value?: string | null) =>
    value === 'completed' ? '成功' : (value ?? '未知'),
  deviceControlStatusTone: () => 'success',
  isTerminalDeviceControlStatus: () => true,
  useBusinessDeviceControlCommands: () => ({
    commands: computed(() => deviceControlState.commands),
    commandsError: shallowRef(),
    commandsPending: shallowRef(false),
    commandsTotal: computed(() => deviceControlState.commands.length),
    historyFilters: { deviceAssetId: 'DEV-OIL-01', status: '', skip: 0, take: 20 },
    dispatchCommand: vi.fn(),
    dispatchError: shallowRef(),
    dispatchPending: shallowRef(false),
    trackedCommandId: shallowRef(null),
    trackedResult: computed(() => undefined),
    trackedPending: shallowRef(false),
    startTracking: vi.fn(),
    resetTracking: vi.fn(),
  }),
}))

vi.mock('@/composables/useBusinessTelemetry', () => ({
  describeTelemetryOeeDegradation: (reason: string) => reason,
  describeTelemetryOeeLimitations: () => 'OEE = 可用率 × 性能率 × 质量率。',
  formatOeeQuantity: (value: number | null | undefined) => (value == null ? '无数据' : `${value}`),
  formatOeeRate: (value: number | null | undefined) =>
    value == null ? '无数据' : `${(value * 100).toFixed(1)}%`,
  useBusinessTelemetryHistory: () => ({
    filters: {
      deviceAssetId: 'DEV-OIL-01',
      tagKey: '',
      windowStartUtc: '2026-07-01T00:00:00Z',
      windowEndUtc: '2026-07-01T08:00:00Z',
    },
    historyError: shallowRef(),
    historyItems: computed(() => []),
    historyPending: shallowRef(false),
    refreshHistory: vi.fn(),
    visibleHistoryItems: computed(() => reviewFixture.historyItems),
  }),
  useBusinessTelemetryOee: () => ({
    availabilityWindows: computed(() => []),
    filters: {
      deviceAssetId: 'DEV-OIL-01',
      tagKey: '',
      windowStartUtc: '2026-07-01T00:00:00Z',
      windowEndUtc: '2026-07-01T08:00:00Z',
    },
    oee: computed(() => ({
      availabilityRate: 0.82,
      loadingRate: 0.91,
      oeeRate: 0.82,
      performanceRate: 0.9,
      qualityRate: 0.95,
      isDegraded: false,
      stateSampleCount: 12,
    })),
    oeeError: shallowRef(),
    oeePending: shallowRef(false),
    refreshOee: vi.fn(),
    runtimeAvailabilityError: shallowRef(),
  }),
  useBusinessTelemetryRuntimeHours: () => ({
    runtimeHours: computed(() => ({
      totalRuntimeHours: runtimeHoursState.total,
      hasRuntimeSamples: runtimeHoursState.hasSamples,
    })),
    totalRuntimeHours: computed(() => runtimeHoursState.total),
    hasRuntimeSamples: computed(() => runtimeHoursState.hasSamples),
    runtimeHoursError: shallowRef(),
    runtimeHoursPending: shallowRef(false),
    runtimeHoursEnabled: computed(() => true),
    refreshRuntimeHours: vi.fn(),
  }),
  // Client-derived per-plan remaining runtime hours; configurable per test (see runtimeRemainingState).
  useMaintenancePlanRuntimeRemaining: () => ({
    remainingByPlanId: computed<Record<string, { status: string; hours?: number }>>(
      () => runtimeRemainingState.map,
    ),
    remainingPending: shallowRef(false),
    refreshRemaining: vi.fn(),
  }),
}))

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useMaintenanceAvailabilityWindows: () => ({
    availabilityError: shallowRef(),
    availabilityPending: shallowRef(false),
    availabilityWindows: computed(() => [
      {
        deviceAssetId: 'DEV-OIL-01',
        availabilityStatus: 'unavailable',
        reasonCode: 'maintenance.pm',
        startUtc: '2026-07-02T01:00:00Z',
      },
    ]),
    filters: {
      deviceAssetIds: 'DEV-OIL-01',
      windowStartUtc: '2026-06-01T00:00:00Z',
      windowEndUtc: '2026-07-01T00:00:00Z',
    },
    refreshAvailability: vi.fn(),
  }),
  useMaintenanceInspections: () => ({
    inspections: computed(() => reviewFixture.inspections),
    inspectionsError: shallowRef(),
    inspectionsPending: shallowRef(false),
    inspectionsTotal: computed(() => 1),
  }),
  useMaintenancePlans: () => ({
    plans: computed(() => reviewFixture.plans),
    plansError: shallowRef(),
    plansPending: shallowRef(false),
    plansTotal: computed(() => 1),
    filters: { organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 200 },
  }),
  useMaintenanceReliability: () => ({
    filters: {
      deviceAssetId: 'DEV-OIL-01',
      windowStartUtc: '2026-06-01T00:00:00Z',
      windowEndUtc: '2026-07-01T00:00:00Z',
    },
    reliability: computed(() => ({
      mtbfHours: 128,
      mtbfRuntimeHasSamples: true,
      mttrMinutes: 42,
      failureCount: 2,
      repairCount: 2,
    })),
    reliabilityError: shallowRef(),
    reliabilityPending: shallowRef(false),
    refreshReliability: vi.fn(),
  }),
  useMaintenanceSpareParts: () => ({
    spareParts: computed(() => reviewFixture.spareParts),
    sparePartsError: shallowRef(),
    sparePartsPending: shallowRef(false),
    sparePartsTotal: computed(() => 1),
  }),
  useMaintenanceWorkOrders: () => ({
    workOrders: computed(() => reviewFixture.workOrders),
    workOrdersError: shallowRef(),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: computed(() => 1),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  DeviceControlSheet: {
    props: ['open', 'deviceAssetId'],
    template: '<div data-testid="device-control-sheet" />',
  },
}

describe('equipment pages', () => {
  beforeEach(() => {
    if (routeState.route) {
      routeState.route.params.deviceAssetId = 'DEV-OIL-01'
    }
    equipmentComposableState.deviceFilters.deviceAssetId = 'DEV-OIL-01'
    equipmentComposableState.refreshDevice.mockClear()
    // Default: both runtime plans known; plan-2 (280h) is the most urgent, no incomplete flag.
    runtimeRemainingState.map = {
      'plan-2': { status: 'ok', hours: 280 },
      'plan-3': { status: 'ok', hours: 900 },
    }
    runtimeHoursState.total = 720
    runtimeHoursState.hasSamples = true
    authState.permissionCodes = [
      'business.iiot.alarms.read',
      'business.iiot.alarms.write',
      'business.iiot.device-control.read',
      'business.iiot.device-control.write',
    ]
  })

  it('does not expose organization or environment context on equipment pages', () => {
    for (const page of [EquipmentIndexPage, EquipmentAlarmsPage, EquipmentDetailPage]) {
      const wrapper = mount(page, { global: { stubs } })

      expect(wrapper.text()).not.toContain('组织')
      expect(wrapper.text()).not.toContain('环境')
      expect(wrapper.html()).not.toContain('organizationId')
      expect(wrapper.html()).not.toContain('environmentId')
    }
  })

  it('updates the device filter and refreshes when route device id changes', async () => {
    mount(EquipmentDetailPage, { global: { stubs } })

    routeState.route!.params.deviceAssetId = 'DEV-PACK-02'
    await nextTick()

    expect(equipmentComposableState.deviceFilters.deviceAssetId).toBe('DEV-PACK-02')
    expect(equipmentComposableState.refreshDevice).toHaveBeenCalledTimes(1)
  })

  it('renders telemetry and maintenance context with source wording on equipment detail', () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('遥测深层上下文')
    expect(wrapper.text()).toContain('OEE = 可用率 × 性能率 × 质量率')
    expect(wrapper.text()).toContain('82.0%')
    expect(wrapper.text()).toContain('历史事件6')
    expect(wrapper.text()).toContain('temperature')
    expect(wrapper.text()).toContain('维护与可靠性上下文')
    expect(wrapper.text()).toContain('mwo-1')
    expect(wrapper.text()).toContain('PM-CNC-MONTHLY')
    expect(wrapper.text()).toContain('insp-6')
    expect(wrapper.text()).toContain('BEARING-6205')
    expect(wrapper.text()).toContain('MTBF')
    expect(wrapper.text()).toContain('正式页面')
  })

  it('renders cumulative runtime hours and hours-until-next-maintenance on equipment detail', () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('累计运行小时')
    expect(wrapper.text()).toContain('720.0 小时')
    expect(wrapper.text()).toContain('距下次保养还需')
    // plan-2 剩余 280h 是已知计划中最小；plan-3 亦已知(900h),无未知候选 → 正常阈值口径,不标不完整。
    expect(wrapper.text()).toContain('280.0 小时')
    expect(wrapper.text()).toContain('PM-CNC-RUNTIME')
    expect(wrapper.text()).not.toContain('可能更紧迫')
  })

  it('shows 无样本 (not 0.0 小时) for cumulative runtime hours when the device has no real samples', () => {
    runtimeHoursState.total = 0
    runtimeHoursState.hasSamples = false
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // NvSectionCard renders description immediately followed by its value — assert the cumulative card
    // value is the honest "无样本", never a fabricated definitive "0.0 小时".
    expect(wrapper.text()).toContain('累计运行小时无样本')
    expect(wrapper.text()).not.toContain('累计运行小时0.0')
  })

  it('flags an incomplete result when a candidate runtime plan read failed / has no samples', () => {
    // plan-2 known (280h min), plan-3 read failed -> its true remaining is unknown and could be smaller.
    runtimeRemainingState.map = {
      'plan-2': { status: 'ok', hours: 280 },
      'plan-3': { status: 'error' },
    }
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // Still surfaces the known minimum value, but the primary label itself says it is only the known
    // minimum — never a deterministic "距下次保养还需" assertion — and the hint flags it may be incomplete.
    expect(wrapper.text()).toContain('280.0 小时')
    expect(wrapper.text()).toContain('已知计划最少还需')
    expect(wrapper.text()).not.toContain('距下次保养还需')
    expect(wrapper.text()).toContain('可能更紧迫')
    // Reason names the actual status (读取失败) and does not enumerate absent causes.
    expect(wrapper.text()).toContain('另 1 个计划读取失败')
    expect(wrapper.text()).not.toContain('阈值缺失')
    expect(wrapper.text()).not.toContain('暂无样本')
  })

  it('shows read-failed for the hours-until-next card when every candidate runtime plan read failed', () => {
    runtimeRemainingState.map = {
      'plan-2': { status: 'error' },
      'plan-3': { status: 'error' },
    }
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('距下次保养还需')
    expect(wrapper.text()).toContain('读取失败')
    // No known remaining -> must not fabricate an "X 小时" value.
    expect(wrapper.text()).not.toContain('280.0 小时')
  })

  it('does not misattribute a read failure to a no-samples candidate when there is no known value', () => {
    // First candidate (plan-2) has no samples; another (plan-3) read failed. Value is read-failed, but the
    // hint must be an aggregate — never claim the no-samples plan itself "读取失败".
    runtimeRemainingState.map = {
      'plan-2': { status: 'no-samples' },
      'plan-3': { status: 'error' },
    }
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('读取失败')
    // Aggregate hint, not attributed to a specific (wrong) plan.
    expect(wrapper.text()).toContain('运行小时读面读取失败，请稍后重试')
    expect(wrapper.text()).not.toContain('运行小时型计划 PM-CNC-RUNTIME · 运行小时读面读取失败')
  })

  it('surfaces 阈值缺失 (consistent with the list, not 无样本) when all candidates are invalid', () => {
    runtimeRemainingState.map = {
      'plan-2': { status: 'invalid' },
      'plan-3': { status: 'invalid' },
    }
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // Detail card must use the same data-truth wording as the list — invalid is not "无样本".
    expect(wrapper.text()).toContain('阈值缺失')
    expect(wrapper.text()).not.toContain('距下次保养还需无样本')
  })

  it('flags incompleteness including invalid candidates alongside a known value', () => {
    // plan-2 known (280h min), plan-3 invalid -> still show the known minimum but mark it incomplete.
    runtimeRemainingState.map = {
      'plan-2': { status: 'ok', hours: 280 },
      'plan-3': { status: 'invalid' },
    }
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('280.0 小时')
    expect(wrapper.text()).toContain('已知计划最少还需')
    expect(wrapper.text()).toContain('可能更紧迫')
    // The incomplete-reason must name the ACTUAL status of the other candidate — only 阈值缺失 here.
    expect(wrapper.text()).toContain('另 1 个计划阈值缺失')
    // Must NOT enumerate reasons that do not apply — otherwise the operator would think it might also be
    // a telemetry read failure or no-samples, when the only real cause is a missing threshold.
    expect(wrapper.text()).not.toContain('读取失败')
    expect(wrapper.text()).not.toContain('暂无样本')
  })

  it('renders the device control action and command history when the user can control the device', () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('设备控制')
    expect(wrapper.text()).toContain('控制命令历史')
    expect(wrapper.text()).toContain('spindle.speed')
    expect(wrapper.find('[data-testid="device-control-sheet"]').exists()).toBe(true)
  })

  it('hides the device control dispatch action without the device-control write permission', () => {
    // Command dispatch is gated by device-control.write; read + manage (binding maintenance) is not enough.
    authState.permissionCodes = [
      'business.iiot.telemetry.read',
      'business.iiot.device-control.read',
      'business.iiot.device-control.manage',
    ]
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // The control-command history section still renders (read-scoped), but the dispatch action does not.
    expect(wrapper.text()).toContain('控制命令历史')
    expect(wrapper.findAll('button').some((b) => b.text().includes('设备控制'))).toBe(false)
  })
})
