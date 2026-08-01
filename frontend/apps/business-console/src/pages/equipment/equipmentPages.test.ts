import { mount } from '@vue/test-utils'
import type {
  BusinessConsoleMaintenanceInspectionItem,
  BusinessConsoleMaintenancePlanItem,
  BusinessConsoleMaintenanceSparePartItem,
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleTelemetryHistoryItem,
} from '@nerv-iip/api-client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, reactive, ref, shallowRef, type Ref } from 'vue'

import EquipmentAlarmsPage from './alarms.vue'
import EquipmentDetailPage from './[deviceAssetId].vue'
import EquipmentIndexPage from './index.vue'

const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
const readFaceState = vi.hoisted(() => ({
  catalogResolved: true,
  activeAlarms: [] as Array<Record<string, unknown>>,
  availabilityWindows: [] as Array<Record<string, unknown>>,
  currentDeviceAssetId: 'DEV-OIL-01',
  workOrders: undefined as Array<Record<string, unknown>> | undefined,
  spareParts: undefined as Array<Record<string, unknown>> | undefined,
}))

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: (code?: string | null) =>
        code?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i) &&
        readFaceState.catalogResolved
          ? '五轴加工中心'
          : undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: (code?: string | null) =>
        code?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i) &&
        readFaceState.catalogResolved
          ? '精加工一线'
          : undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const routeState = vi.hoisted(() => ({
  route: undefined as { params: { deviceAssetId: string } } | undefined,
}))

const equipmentComposableState = vi.hoisted(() => ({
  deviceFilters: { deviceAssetId: 'DEV-OIL-01' },
  refreshDevice: vi.fn(),
}))

// 看板列表读面；按用例改写以驱动状态构成环的分段与四态（idle/loading/error/ready）。
const overviewState = vi.hoisted(() => ({
  activeBlocks: [] as Array<Record<string, unknown>>,
  devices: [] as Array<Record<string, unknown>>,
  effectiveCount: 2,
  overviewError: undefined as unknown,
  rosterError: undefined as unknown,
  rosterTotal: 2,
  state: 'ready' as 'idle' | 'loading' | 'error' | 'ready',
}))

const overviewMocks = vi.hoisted(() => ({
  refreshDeviceRoster: vi.fn(),
  refreshOverview: vi.fn(),
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

const equipmentHealthState = vi.hoisted(() => ({
  deviceAssetId: undefined as Ref<string> | undefined,
  refreshHealth: vi.fn(),
  health: {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    deviceAssetId: 'DEV-OIL-01',
    healthScore: 85,
    level: 'watch' as const,
    calculatedAtUtc: '2026-07-24T01:02:03Z',
    dataFreshness: { status: 'fresh' as const },
    riskFactors: [],
    ruleEvaluations: [],
  },
}))

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
  equipmentStatusTone: (state?: string | null) =>
    state === 'faulted' || state === 'down' ? 'danger' : state === 'idle' ? 'neutral' : 'success',
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
    activeAlarms: computed(() => readFaceState.activeAlarms),
    availabilityWindows: computed(() => readFaceState.availabilityWindows),
    device: computed(() => ({
      currentState: {
        deviceAssetId: readFaceState.currentDeviceAssetId,
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
    activeBlocks: computed(() => overviewState.activeBlocks),
    contextReady: computed(() => overviewState.state !== 'idle'),
    deviceRosterError: computed(() => overviewState.rosterError),
    deviceRosterTotal: computed(() => overviewState.rosterTotal),
    devices: computed(() => overviewState.devices),
    effectiveDeviceAssetIdCount: computed(() => overviewState.effectiveCount),
    filters: {
      deviceAssetIds: 'DEV-OIL-01,DEV-PACK-01',
    },
    overviewError: computed(() => overviewState.overviewError),
    overviewPending: computed(() => overviewState.state === 'loading'),
    overviewState: computed(() => overviewState.state),
    refreshDeviceRoster: overviewMocks.refreshDeviceRoster,
    refreshOverview: overviewMocks.refreshOverview,
  }),
}))

// 级联范围选择 composable：真实实现依赖主数据 facade（pinia + query），页面测试给可控桩。
vi.mock('@/composables/useEquipmentScopeSelection', () => ({
  useEquipmentScopeSelection: (initial?: { workshop?: string; line?: string; device?: string }) => {
    const scope = ref({
      workshop: initial?.workshop ?? '',
      line: initial?.line ?? '',
      device: initial?.device ?? '',
    })
    return {
      scope,
      levels: computed(() => []),
      devicesInScope: computed(() => []),
      scopeLabel: computed(() => '全厂'),
      scopePending: shallowRef(false),
      selectedDevice: computed(() => undefined),
    }
  },
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
  useBusinessEquipmentHealth: (deviceAssetId: Ref<string>) => {
    equipmentHealthState.deviceAssetId = deviceAssetId
    return {
      health: computed(() => equipmentHealthState.health),
      healthError: shallowRef(),
      healthPending: shallowRef(false),
      refreshHealth: equipmentHealthState.refreshHealth,
    }
  },
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
    refreshPlans: vi.fn(),
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
    spareParts: computed(() => readFaceState.spareParts ?? reviewFixture.spareParts),
    sparePartsError: shallowRef(),
    sparePartsPending: shallowRef(false),
    sparePartsTotal: computed(() => 1),
  }),
  useMaintenanceWorkOrders: () => ({
    workOrders: computed(() => readFaceState.workOrders ?? reviewFixture.workOrders),
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
  EquipmentHealthCard: {
    name: 'EquipmentHealthCard',
    props: ['health', 'pending', 'error'],
    template:
      '<section data-testid="equipment-health-card">{{ health?.healthScore }} {{ pending }} {{ error }}</section>',
  },
}

describe('equipment pages', () => {
  beforeEach(() => {
    if (routeState.route) {
      routeState.route.params.deviceAssetId = 'DEV-OIL-01'
    }
    equipmentComposableState.deviceFilters = reactive({ deviceAssetId: 'DEV-OIL-01' })
    equipmentComposableState.refreshDevice.mockClear()
    equipmentHealthState.deviceAssetId = undefined
    equipmentHealthState.refreshHealth.mockClear()
    // Default: both runtime plans known; plan-2 (280h) is the most urgent, no incomplete flag.
    runtimeRemainingState.map = {
      'plan-2': { status: 'ok', hours: 280 },
      'plan-3': { status: 'ok', hours: 900 },
    }
    runtimeHoursState.total = 720
    runtimeHoursState.hasSamples = true
    overviewState.devices = []
    overviewState.activeBlocks = []
    overviewState.effectiveCount = 2
    overviewState.overviewError = undefined
    overviewState.rosterError = undefined
    overviewState.rosterTotal = 2
    overviewState.state = 'ready'
    overviewMocks.refreshDeviceRoster.mockClear()
    overviewMocks.refreshOverview.mockClear()
    authState.permissionCodes = [
      'business.iiot.alarms.read',
      'business.iiot.alarms.write',
      'business.iiot.device-control.read',
      'business.iiot.device-control.write',
    ]
    readFaceState.catalogResolved = true
    readFaceState.activeAlarms = []
    readFaceState.availabilityWindows = []
    readFaceState.currentDeviceAssetId = 'DEV-OIL-01'
    readFaceState.workOrders = undefined
    readFaceState.spareParts = undefined
  })

  it('does not expose internal organization or environment identifiers on equipment pages', () => {
    for (const page of [EquipmentIndexPage, EquipmentAlarmsPage, EquipmentDetailPage]) {
      const wrapper = mount(page, { global: { stubs } })

      expect(wrapper.html()).not.toContain('organizationId')
      expect(wrapper.html()).not.toContain('environmentId')
    }
  })

  it('draws the device-state ring as a true partition of the fleet and keeps alarms off it', () => {
    overviewState.devices = [
      { deviceAssetId: 'DEV-A', currentState: 'running' },
      { deviceAssetId: 'DEV-B', currentState: 'running' },
      { deviceAssetId: 'DEV-C', currentState: 'faulted' },
      { deviceAssetId: 'DEV-D', currentState: 'idle', activeAlarmCount: 3 },
    ]

    const ring = mount(EquipmentIndexPage, { global: { stubs } }).find('.nv-ring-card')
    expect(ring.exists()).toBe(true)

    // 图例三段（2 运行 / 1 停机 / 1 其他）之和 = 环心的 4 台设备。
    const legend = ring.findAll('li').map((row) => row.text())
    expect(legend).toHaveLength(3)
    expect(legend[0]).toContain('运行就绪')
    expect(legend[0]).toContain('2')
    expect(legend[1]).toContain('异常停机')
    expect(legend[2]).toContain('其他状态')
    // 报警数与设备台数不同量纲，绝不能混进这个环。
    expect(ring.text()).not.toContain('报警')
  })

  it('surfaces unresolved alarms as an actionable alert card, not a plain count', () => {
    overviewState.devices = [{ deviceAssetId: 'DEV-D', currentState: 'idle', activeAlarmCount: 3 }]

    const wrapper = mount(EquipmentIndexPage, { global: { stubs } })
    const alertCards = wrapper.findAll('[data-variant="alert"]')
    const alarmCard = alertCards.find((card) => card.text().includes('未解除报警'))

    expect(alarmCard).toBeDefined()
    expect(alarmCard!.text()).toContain('3')
    expect(alarmCard!.text()).toContain('需处理')
    expect(alarmCard!.text()).toContain('查看报警')
  })

  it('renders a read failure as an error block with retry, never as 0 devices', async () => {
    overviewState.state = 'error'
    overviewState.rosterError = new Error('roster boom')

    const wrapper = mount(EquipmentIndexPage, { global: { stubs } })

    expect(wrapper.text()).toContain('设备台账取不到，当前无法判断在册设备与运行情况。')
    expect(wrapper.text()).toContain('设备清单取不到，无法判断有哪些设备、各自什么状态。')
    expect(wrapper.text()).toContain('阻塞窗口取不到，无法判断当前是否有设备被阻塞。')
    expect(wrapper.text()).not.toContain('0 台设备')
    expect(wrapper.text()).not.toContain('暂无设备运行记录')
    expect(wrapper.text()).not.toContain('当前没有设备阻塞窗口')
    expect(wrapper.find('.nv-ring-card').exists()).toBe(false)

    const retry = wrapper.findAll('button').find((button) => button.text().trim() === '重试')
    expect(retry).toBeDefined()
    await retry!.trigger('click')
    expect(overviewMocks.refreshDeviceRoster).toHaveBeenCalled()
    expect(overviewMocks.refreshOverview).toHaveBeenCalled()
  })

  it('separates not-yet-queried and loading from a genuine zero-device fleet', () => {
    overviewState.state = 'idle'
    const idle = mount(EquipmentIndexPage, { global: { stubs } })
    expect(idle.text()).toContain('业务上下文未就绪，设备运行数据尚未查询。')
    expect(idle.text()).not.toContain('暂无设备运行记录')
    expect(idle.text()).not.toContain('0 台设备')

    overviewState.state = 'loading'
    const loading = mount(EquipmentIndexPage, { global: { stubs } })
    expect(loading.text()).toContain('正在读取设备台账与运行状态…')
    expect(loading.text()).not.toContain('0 台设备')

    overviewState.state = 'ready'
    const ready = mount(EquipmentIndexPage, { global: { stubs } })
    expect(ready.text()).toContain('0 台设备')
    expect(ready.text()).toContain('暂无设备运行记录')
  })

  it('states the 50-device query cap instead of silently truncating the fleet', () => {
    overviewState.rosterTotal = 71
    overviewState.effectiveCount = 50

    const wrapper = mount(EquipmentIndexPage, { global: { stubs } })

    expect(wrapper.text()).toContain('范围内共 71 台设备，当前看板展示前 50 台。')
  })

  it('updates the device filter and refreshes when route device id changes', async () => {
    mount(EquipmentDetailPage, { global: { stubs } })

    routeState.route!.params.deviceAssetId = 'DEV-PACK-02'
    await nextTick()

    expect(equipmentComposableState.deviceFilters.deviceAssetId).toBe('DEV-PACK-02')
    expect(equipmentHealthState.deviceAssetId?.value).toBe('DEV-PACK-02')
    expect(equipmentComposableState.refreshDevice).toHaveBeenCalledTimes(1)
  })

  it('wires equipment health data into the detail card and refreshes it manually', async () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.get('[data-testid="equipment-health-card"]').text()).toContain('85')

    const refresh = wrapper.findAll('button').find((button) => button.text().trim() === '刷新')
    expect(refresh).toBeDefined()
    await refresh!.trigger('click')
    expect(equipmentHealthState.refreshHealth).toHaveBeenCalledTimes(1)
  })

  it('renders equipment indicators and maintenance facts without implementation-stage copy', () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('设备运行指标')
    expect(wrapper.text()).toContain('OEE = 可用率 × 性能率 × 质量率')
    expect(wrapper.text()).toContain('82.0%')
    expect(wrapper.text()).toContain('历史事件6')
    expect(wrapper.text()).toContain('temperature')
    expect(wrapper.text()).toContain('维护与可靠性')
    expect(wrapper.text()).toContain('维修工单')
    expect(wrapper.text()).toContain('PM-CNC-MONTHLY')
    expect(wrapper.text()).toContain('insp-6')
    expect(wrapper.text()).toContain('BEARING-6205')
    expect(wrapper.text()).toContain('MTBF')
    expect(wrapper.text()).not.toContain('正式页面')
    expect(wrapper.text()).not.toContain('Ops')
  })

  it('equipment detail read-face guard：目录可解析时显示设备、来源与报警编号', () => {
    const deviceId = '019fbb41-1111-7111-8111-111111111111'
    const alarmId = '019fbb41-2222-7222-8222-222222222222'
    const workOrderId = '019fbb41-3333-7333-8333-333333333333'
    routeState.route!.params.deviceAssetId = deviceId
    equipmentComposableState.deviceFilters = reactive({ deviceAssetId: deviceId })
    readFaceState.currentDeviceAssetId = deviceId
    readFaceState.activeAlarms = [{ alarmEventId: alarmId, alarmCode: 'ALM-CNC-008' }]
    readFaceState.availabilityWindows = [
      {
        availabilityStatus: 'unavailable',
        reasonCode: 'maintenance.pm',
        workCenterId: '019fbb41-4444-7444-8444-444444444444',
        sourceReferenceId: workOrderId,
        sourceReferenceLabel: '计划保养 · PM-CNC-008',
      },
    ]
    readFaceState.workOrders = [
      {
        workOrderId,
        deviceAssetId: deviceId,
        sourceAlarmId: alarmId,
        status: 'open',
        openedAtUtc: '2026-08-01T01:00:00Z',
      },
    ]
    readFaceState.spareParts = [
      {
        sparePartLineId: 'sp-readable',
        workOrderId,
        deviceAssetId: deviceId,
        skuCode: 'BEARING-1',
      },
    ]

    const visibleText = mount(EquipmentDetailPage, { global: { stubs } }).text()
    expect(visibleText).toContain('五轴加工中心')
    expect(visibleText).toContain('精加工一线')
    expect(visibleText).toContain('计划保养 · PM-CNC-008')
    expect(visibleText).toContain('ALM-CNC-008')
    expect(visibleText).toContain('维修工单')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  it('equipment detail read-face guard：目录与关联解析失败时显示占位符且不泄露 ID', () => {
    const deviceId = '019fbb41-aaaa-7aaa-8aaa-aaaaaaaaaaaa'
    const alarmId = '019fbb41-bbbb-7bbb-8bbb-bbbbbbbbbbbb'
    const workOrderId = '019fbb41-cccc-7ccc-8ccc-cccccccccccc'
    routeState.route!.params.deviceAssetId = deviceId
    equipmentComposableState.deviceFilters = reactive({ deviceAssetId: deviceId })
    readFaceState.catalogResolved = false
    readFaceState.currentDeviceAssetId = deviceId
    readFaceState.activeAlarms = []
    readFaceState.availabilityWindows = [
      {
        availabilityStatus: 'unavailable',
        reasonCode: 'maintenance.pm',
        workCenterId: deviceId,
        sourceReferenceId: workOrderId,
      },
    ]
    readFaceState.workOrders = [
      {
        workOrderId,
        deviceAssetId: deviceId,
        sourceAlarmId: alarmId,
        status: 'open',
        openedAtUtc: '2026-08-01T01:00:00Z',
      },
    ]
    readFaceState.spareParts = [
      { sparePartLineId: 'sp-hidden', workOrderId, deviceAssetId: deviceId, skuCode: 'BEARING-1' },
    ]

    const visibleText = mount(EquipmentDetailPage, { global: { stubs } }).text()
    expect(visibleText).toContain('—')
    expect(visibleText).toContain('维修工单')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  it('renders each OEE factor as a gap-to-target bar and OEE itself as multiplied facets', () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // 各率是「离 100% 还差多少」→ target 进度条。
    const availability = wrapper
      .findAll('[data-variant="target"]')
      .find((card) => card.text().includes('可用率'))
    expect(availability).toBeDefined()
    expect(availability!.find('[role="progressbar"]').exists()).toBe(true)

    // OEE = A×P×Q 是相乘率，不是构成：必须是 facets，绝不能画成环。
    const oeeCard = wrapper
      .findAll('[data-variant="facets"]')
      .find((card) => card.text().includes('OEE'))
    expect(oeeCard).toBeDefined()
    expect(oeeCard!.text()).toContain('性能率')
    expect(oeeCard!.text()).toContain('质量率')
    expect(wrapper.find('.nv-ring-card').exists()).toBe(false)
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
    expect(wrapper.text()).toContain('控制命令记录')
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
    expect(wrapper.text()).toContain('控制命令记录')
    expect(wrapper.findAll('button').some((b) => b.text().includes('设备控制'))).toBe(false)
  })
})
