import { mount } from '@vue/test-utils'
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

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  routeState.route = reactive({ params: { deviceAssetId: 'DEV-OIL-01' } })

  return {
    ...actual,
    useRoute: () => routeState.route,
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
    alarms: computed(() => []),
    alarmsError: shallowRef(),
    alarmsPending: shallowRef(false),
    refreshAlarms: vi.fn(),
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

vi.mock('@/composables/useBusinessTelemetry', () => ({
  describeTelemetryOeeLimitations: () => '当前 OEE 只按设备运行状态计算可用率，性能与质量不作为真实测量值。',
  formatOeeRate: (value: number | null | undefined) => value == null ? '无数据' : `${(value * 100).toFixed(1)}%`,
  useBusinessTelemetryHistory: () => ({
    filters: { deviceAssetId: 'DEV-OIL-01', tagKey: '', windowStartUtc: '2026-07-01T00:00:00Z', windowEndUtc: '2026-07-01T08:00:00Z' },
    historyError: shallowRef(),
    historyItems: computed(() => []),
    historyPending: shallowRef(false),
    refreshHistory: vi.fn(),
    visibleHistoryItems: computed(() => [
      { eventType: 'alarm', tagKey: 'temperature', valueText: 'ALM-TEMP-HIGH', occurredAtUtc: '2026-07-01T01:20:00Z' },
      { eventType: 'state', tagKey: 'runtime', valueText: 'running', occurredAtUtc: '2026-07-01T02:00:00Z' },
    ]),
  }),
  useBusinessTelemetryOee: () => ({
    availabilityWindows: computed(() => []),
    filters: { deviceAssetId: 'DEV-OIL-01', tagKey: '', windowStartUtc: '2026-07-01T00:00:00Z', windowEndUtc: '2026-07-01T08:00:00Z' },
    oee: computed(() => ({
      availabilityRate: 0.82,
      loadingRate: 0.91,
      oeeRate: 0.82,
      performanceRate: 0,
      performanceRateEstimated: true,
      qualityRate: 0,
      qualityRateEstimated: true,
      stateSampleCount: 12,
    })),
    oeeError: shallowRef(),
    oeePending: shallowRef(false),
    refreshOee: vi.fn(),
    runtimeAvailabilityError: shallowRef(),
  }),
}))

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useMaintenanceAvailabilityWindows: () => ({
    availabilityError: shallowRef(),
    availabilityPending: shallowRef(false),
    availabilityWindows: computed(() => [
      { deviceAssetId: 'DEV-OIL-01', availabilityStatus: 'unavailable', reasonCode: 'maintenance.pm', startUtc: '2026-07-02T01:00:00Z' },
    ]),
    filters: { deviceAssetIds: 'DEV-OIL-01', windowStartUtc: '2026-06-01T00:00:00Z', windowEndUtc: '2026-07-01T00:00:00Z' },
    refreshAvailability: vi.fn(),
  }),
  useMaintenanceInspections: () => ({
    inspections: computed(() => [{ inspectionId: 'insp-1', deviceAssetId: 'DEV-OIL-01', result: 'passed', inspectedAtUtc: '2026-07-01T03:00:00Z' }]),
    inspectionsError: shallowRef(),
    inspectionsPending: shallowRef(false),
    inspectionsTotal: computed(() => 1),
  }),
  useMaintenancePlans: () => ({
    plans: computed(() => [{ maintenancePlanId: 'plan-1', deviceAssetId: 'DEV-OIL-01', planCode: 'PM-CNC-MONTHLY', planName: '主轴月度保养' }]),
    plansError: shallowRef(),
    plansPending: shallowRef(false),
    plansTotal: computed(() => 1),
  }),
  useMaintenanceReliability: () => ({
    filters: { deviceAssetId: 'DEV-OIL-01', windowStartUtc: '2026-06-01T00:00:00Z', windowEndUtc: '2026-07-01T00:00:00Z' },
    reliability: computed(() => ({ mtbfHours: 128, mtbfRuntimeHasSamples: true, mttrMinutes: 42, failureCount: 2, repairCount: 2 })),
    reliabilityError: shallowRef(),
    reliabilityPending: shallowRef(false),
    refreshReliability: vi.fn(),
  }),
  useMaintenanceSpareParts: () => ({
    spareParts: computed(() => [{ sparePartRequestId: 'sp-1', deviceAssetId: 'DEV-OIL-01', skuCode: 'BEARING-6205', requiredQuantity: 2 }]),
    sparePartsError: shallowRef(),
    sparePartsPending: shallowRef(false),
    sparePartsTotal: computed(() => 1),
  }),
  useMaintenanceWorkOrders: () => ({
    workOrders: computed(() => [{ workOrderId: 'mwo-1', deviceAssetId: 'DEV-OIL-01', workOrderNo: 'MWO-202607-001', status: 'open' }]),
    workOrdersError: shallowRef(),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: computed(() => 1),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

describe('equipment pages', () => {
  beforeEach(() => {
    if (routeState.route) {
      routeState.route.params.deviceAssetId = 'DEV-OIL-01'
    }
    equipmentComposableState.deviceFilters.deviceAssetId = 'DEV-OIL-01'
    equipmentComposableState.refreshDevice.mockClear()
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
    expect(wrapper.text()).toContain('当前 OEE 只按设备运行状态计算可用率')
    expect(wrapper.text()).toContain('82.0%')
    expect(wrapper.text()).toContain('temperature')
    expect(wrapper.text()).toContain('维护与可靠性上下文')
    expect(wrapper.text()).toContain('MWO-202607-001')
    expect(wrapper.text()).toContain('PM-CNC-MONTHLY')
    expect(wrapper.text()).toContain('BEARING-6205')
    expect(wrapper.text()).toContain('MTBF')
    expect(wrapper.text()).toContain('正式页面')
  })
})
