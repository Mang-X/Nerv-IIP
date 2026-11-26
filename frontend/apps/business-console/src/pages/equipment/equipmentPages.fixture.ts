import type {
  BusinessConsoleMaintenanceInspectionItem,
  BusinessConsoleMaintenancePlanItem,
  BusinessConsoleMaintenanceSparePartItem,
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleTelemetryHistoryItem,
} from '@nerv-iip/api-client'
import { vi } from 'vitest'
import { computed, ref, shallowRef, type Ref } from 'vue'

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
vi.mock('@/composables/master-data/useSkuNames', async () => {
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
vi.mock('@/composables/master-data/useBusinessPartnerNames', async () => {
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
vi.mock('@/composables/master-data/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: (code?: string | null) =>
        code?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i) &&
        readFaceState.catalogResolved
          ? '五轴加工中心'
          : undefined,
      resolveDeviceCode: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: (code?: string | null) =>
        code?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i) &&
        readFaceState.catalogResolved
          ? '精加工一线'
          : undefined,
      resolveTeam: () => undefined,
      resolveUom: (code?: string | null) => (code === 'pcs' ? '件' : undefined),
      // 员工名录里只有 operator-a；user-admin 是系统管理员账号，不在名录里。
      resolveUser: (userId?: string | null) => (userId === 'operator-a' ? '王建国' : undefined),
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
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
    'business.maintenance.work-orders.read',
    'business.maintenance.work-orders.manage',
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
      deviceReceiptCode: 'BadOutOfRange',
    },
    {
      commandId: 'cmd-2',
      operationTaskId: 'op-2',
      deviceAssetId: 'DEV-OIL-01',
      commandType: 'write-tag',
      tagKey: 'spindle.speed',
      value: '90',
      requestedBy: 'user-admin',
      status: 'failed',
      approvalStatus: 'approved',
      correlationId: 'corr-2',
      requestedAtUtc: '2026-07-01T07:00:00Z',
      failureCode: 'opcua.write.rejected',
    },
  ],
}))

// Client-derived per-plan remaining runtime hours; configurable per test to drive mixed-status cases.
const runtimeRemainingState = vi.hoisted(() => ({
  map: {} as Record<string, { status: string; hours?: number }>,
}))

// Cumulative runtime-hours read; configurable so a no-samples device can be exercised.
const runtimeHoursState = vi.hoisted(() => ({ total: 720, hasSamples: true }))
const reliabilityState = vi.hoisted(() => ({ mtbfHours: 128 as number | null, failureCount: 2 }))

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
      uomCode: 'pcs',
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

vi.mock('@/composables/equipment/useBusinessEquipment', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/equipment/useBusinessEquipment')>()),
  equipmentStatusTone: (state?: string | null) =>
    state === 'faulted' || state === 'down' ? 'danger' : state === 'idle' ? 'neutral' : 'success',
  useBusinessEquipmentAlarms: () => ({
    acknowledgeAlarm: vi.fn(),
    alarms: computed(() => []),
    alarmsTotal: computed(() => 0),
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
vi.mock('@/composables/equipment/useEquipmentScopeSelection', () => ({
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

vi.mock('@/composables/equipment/useBusinessDeviceControl', async (importOriginal) => ({
  deviceReceiptLabel: (
    await importOriginal<typeof import('@/composables/equipment/useBusinessDeviceControl')>()
  ).deviceReceiptLabel,
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

vi.mock('@/composables/equipment/useBusinessTelemetry', () => ({
  describeTelemetryOeeDegradation: (reason: string) => reason,
  describeTelemetryOeeLimitations: () => 'OEE = 可用率 × 性能率 × 质量率。',
  formatOeeQuantity: (value: number | null | undefined, uom?: string | null) =>
    value == null ? '无数据' : `${value}${uom ? ` ${uom}` : ''}`,
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
      expectedOutputQuantity: 120,
      outputUomCode: 'pcs',
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

vi.mock('@/composables/maintenance/useBusinessMaintenance', () => ({
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
      mtbfHours: reliabilityState.mtbfHours,
      mtbfRuntimeHasSamples: true,
      mttrMinutes: 42,
      failureCount: reliabilityState.failureCount,
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
  DeviceTelemetryPointsSheet: {
    props: ['open', 'deviceAssetId', 'deviceTitle', 'canManage'],
    template:
      '<div data-testid="telemetry-points-sheet" :data-open="String(open)" :data-can-manage="String(canManage)" :data-device="deviceAssetId" />',
  },
  EquipmentHealthCard: {
    name: 'EquipmentHealthCard',
    props: ['health', 'pending', 'error'],
    template:
      '<section data-testid="equipment-health-card">{{ health?.healthScore }} {{ pending }} {{ error }}</section>',
  },
}

export {
  readFaceState,
  routeState,
  equipmentComposableState,
  overviewState,
  overviewMocks,
  authState,
  runtimeRemainingState,
  runtimeHoursState,
  equipmentHealthState,
  stubs,
  UUID_PATTERN,
  reliabilityState,
}
