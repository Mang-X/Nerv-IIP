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
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { reactive } from 'vue'
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

  it('renders the device control action and command history when the user can control the device', () => {
    const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

    expect(wrapper.text()).toContain('设备控制')
    expect(wrapper.text()).toContain('控制命令记录')
    expect(wrapper.text()).toContain('spindle.speed')
    expect(wrapper.find('[data-testid="device-control-sheet"]').exists()).toBe(true)
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

  it('shows operators by name and device receipts in business words, never raw accounts or codes', () => {
    const text = mount(EquipmentDetailPage, { global: { stubs } }).text()

    expect(text).toContain('王建国')
    // 备件数量的单位同样按主数据名称显示。
    expect(text).toContain('数量 2 件')
    expect(text).toContain('设备拒绝执行')
    for (const raw of ['operator-a', 'user-admin', 'BadOutOfRange', 'opcua.write.rejected']) {
      expect(text).not.toContain(raw)
    }
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
  it('opens the in-place telemetry point sheet from the device detail, with management gated by tags.manage', async () => {
    authState.permissionCodes = [...authState.permissionCodes, 'business.iiot.tags.manage']
    const manager = mount(EquipmentDetailPage, { global: { stubs } })
    const sheet = () => manager.get('[data-testid="telemetry-points-sheet"]')
    expect(sheet().attributes('data-open')).toBe('false')
    expect(sheet().attributes('data-can-manage')).toBe('true')

    const button = manager.findAll('button').find((b) => b.text().includes('采集点位'))
    expect(button).toBeDefined()
    await button!.trigger('click')
    expect(sheet().attributes('data-open')).toBe('true')
    expect(sheet().attributes('data-device')).toBeTruthy()

    authState.permissionCodes = authState.permissionCodes.filter(
      (code) => code !== 'business.iiot.tags.manage',
    )
    const reader = mount(EquipmentDetailPage, { global: { stubs } })
    expect(reader.get('[data-testid="telemetry-points-sheet"]').attributes('data-can-manage')).toBe(
      'false',
    )
    expect(reader.findAll('button').some((b) => b.text().includes('采集点位'))).toBe(true)
  })

  describe('「创建维修工单」只给有维护工单管理权限的人', () => {
    function signInWithoutWorkOrderManage() {
      authState.permissionCodes = authState.permissionCodes.filter(
        (code) => code !== 'business.maintenance.work-orders.manage',
      )
    }

    it('设备看板：只读角色的阻塞卡片与设备行菜单里都没有这一项', async () => {
      signInWithoutWorkOrderManage()
      overviewState.devices = [{ deviceAssetId: 'DEV-A', currentState: 'running' }]
      overviewState.activeBlocks = [
        { deviceAssetId: 'DEV-A', reasonCode: 'maintenance', startUtc: '2026-07-12T01:00:00Z' },
      ]
      const wrapper = mount(EquipmentIndexPage, { global: { stubs }, attachTo: document.body })
      await wrapper.get('[aria-label="设备操作 DEV-A"]').trigger('click')
      await flushPromises()

      // 行菜单确实展开了（同菜单里的「查看详情」在），阻塞卡片和行菜单里都没有建单入口。
      expect(document.body.textContent).toContain('查看详情')
      expect(document.body.textContent).not.toContain('创建维修工单')
      wrapper.unmount()
    })

    it('设备看板：有管理权限时阻塞卡片与设备行菜单里都有', async () => {
      overviewState.devices = [{ deviceAssetId: 'DEV-A', currentState: 'running' }]
      overviewState.activeBlocks = [
        { deviceAssetId: 'DEV-A', reasonCode: 'maintenance', startUtc: '2026-07-12T01:00:00Z' },
      ]
      const wrapper = mount(EquipmentIndexPage, { global: { stubs }, attachTo: document.body })
      expect(wrapper.text()).toContain('创建维修工单')
      await wrapper.get('[aria-label="设备操作 DEV-A"]').trigger('click')
      await flushPromises()

      const menuItems = [...document.body.querySelectorAll('[role="menuitem"]')]
      expect(menuItems.some((item) => item.textContent?.includes('创建维修工单'))).toBe(true)
      wrapper.unmount()
    })

    it('设备详情：有管理权限时页头有「创建维修工单」', () => {
      const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

      expect(wrapper.text()).toContain('创建维修工单')
    })

    it('设备详情：只读角色看不到', () => {
      signInWithoutWorkOrderManage()
      const wrapper = mount(EquipmentDetailPage, { global: { stubs } })

      expect(wrapper.text()).toContain('记录停机')
      expect(wrapper.text()).not.toContain('创建维修工单')
    })
  })
})
