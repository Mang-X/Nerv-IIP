import {
  UUID_PATTERN,
  readFaceState,
  deviceControlState,
  routeState,
  equipmentComposableState,
  overviewState,
  overviewMocks,
  authState,
  runtimeRemainingState,
  runtimeHoursState,
  reliabilityState,
  equipmentHealthState,
  stubs,
} from './equipmentPages.fixture'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { nextTick, reactive } from 'vue'
import EquipmentDetailPage from './[deviceAssetId].vue'

describe('equipment pages', () => {
  beforeEach(() => {
    deviceControlState.error = undefined
    deviceControlState.refreshCommands.mockClear()
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
      'business.maintenance.work-orders.read',
      'business.maintenance.work-orders.manage',
    ]
    readFaceState.catalogResolved = true
    readFaceState.activeAlarms = []
    readFaceState.availabilityWindows = []
    readFaceState.currentDeviceAssetId = 'DEV-OIL-01'
    readFaceState.workOrders = undefined
    readFaceState.spareParts = undefined
  })

  it('shows a retryable history error instead of the command table', async () => {
    deviceControlState.error = new Error('控制命令记录暂时不可用')
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.findAll('[role="alert"]').map((alert) => alert.text())).toContain(
      '控制命令记录暂时不可用',
    )
    expect(wrapper.text()).not.toContain('该设备还没有控制命令记录')
    expect(wrapper.text()).not.toContain('spindle.speed')
    const retry = wrapper.findAll('button').find((button) => button.text() === '重试')
    await retry!.trigger('click')
    expect(deviceControlState.refreshCommands).toHaveBeenCalledOnce()
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
    // 理论产出的单位按主数据名称显示，不直接显示单位编码。
    expect(wrapper.text()).toContain('理论产出120 件')
    expect(wrapper.text()).toContain('历史事件6')
    expect(wrapper.text()).toContain('实时测点与曲线')
    expect(wrapper.text()).not.toContain('历史趋势摘录')
    expect(wrapper.text()).toContain('维护与可靠性')
    expect(wrapper.text()).toContain('维修工单')
    expect(wrapper.text()).toContain('PM-CNC-MONTHLY')
    // 点检记录没有人读单号（inspectionId 是主键），按点检时间 + 中文结果上屏。
    expect(wrapper.text()).toContain('结果 通过')
    expect(wrapper.text()).not.toContain('insp-6')
    expect(wrapper.text()).not.toContain('passed')
    // 设备状态使用业务文案。
    expect(wrapper.text()).not.toContain('running')
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

  it('shows 暂无记录 (not 0.0 小时) for cumulative runtime hours when the device has no real samples', () => {
    runtimeHoursState.total = 0
    runtimeHoursState.hasSamples = false
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // NvSectionCard renders description immediately followed by its value — assert the cumulative card
    // value is the honest "暂无记录", never a fabricated definitive "0.0 小时".
    expect(wrapper.text()).toContain('累计运行小时暂无记录')
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
    expect(wrapper.text()).not.toContain('暂无运行记录')
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
    expect(wrapper.text()).toContain('运行小时读取失败，请稍后重试')
    expect(wrapper.text()).not.toContain('运行小时型计划 PM-CNC-RUNTIME · 运行小时读取失败')
  })

  it('surfaces 阈值缺失 (consistent with the list, not 暂无记录) when all candidates are invalid', () => {
    runtimeRemainingState.map = {
      'plan-2': { status: 'invalid' },
      'plan-3': { status: 'invalid' },
    }
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    // Detail card must use the same data-truth wording as the list — invalid is not "暂无记录".
    expect(wrapper.text()).toContain('阈值缺失')
    expect(wrapper.text()).not.toContain('距下次保养还需暂无记录')
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
    expect(wrapper.text()).not.toContain('暂无运行记录')
  })
})

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
      'business.maintenance.work-orders.read',
      'business.maintenance.work-orders.manage',
    ]
    readFaceState.catalogResolved = true
    readFaceState.activeAlarms = []
    readFaceState.availabilityWindows = []
    readFaceState.currentDeviceAssetId = 'DEV-OIL-01'
    readFaceState.workOrders = undefined
    readFaceState.spareParts = undefined
  })

  it('states MTBF with its real 30-day range, and says 无故障 when there is runtime but no failure', () => {
    // 可靠性取数范围是近 30 天（mock 为 06-01 至 07-01），MTBF = 运行时长 ÷ 故障次数。
    const withFailures = mount(EquipmentDetailPage, { global: { stubs } }).text()
    expect(withFailures).toContain('运行时长 ÷ 故障次数')
    expect(withFailures).toContain('近 30 天维护事件')

    reliabilityState.mtbfHours = null
    reliabilityState.failureCount = 0
    try {
      const noFailure = mount(EquipmentDetailPage, { global: { stubs } }).text()
      expect(noFailure).toContain('MTBF无故障')
      expect(noFailure).toContain('近 30 天没有故障记录')
      expect(noFailure).not.toContain('运行时长 ÷ 故障次数')
    } finally {
      reliabilityState.mtbfHours = 128
      reliabilityState.failureCount = 2
    }
  })
})
