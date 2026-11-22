import {
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
} from './equipmentPages.fixture'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { reactive } from 'vue'
import EquipmentAlarmsPage from './alarms.vue'
import EquipmentDetailPage from './[deviceAssetId].vue'
import EquipmentIndexPage from './index.vue'

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
    expect(idle.text()).toContain('尚未确定当前组织，暂未查询设备运行数据。')
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
})
