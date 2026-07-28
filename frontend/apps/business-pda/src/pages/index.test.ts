import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'

// 真实 router.push 返回 Promise（index.vue 的导航会 `.catch`）；mock 同此契约。
const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

// 报警角标数据源：mock composable，避免拉起 pinia/colada；用 ref 驱动角标可见性。
const unacknowledgedCount = ref(0)
vi.mock('@/composables/useBusinessEquipmentAlarms', () => ({
  useUnacknowledgedAlarmCount: () => ({ unacknowledgedCount }),
}))

// 工作台各板块数据源：mock 掉网络层，页面只消费 refs。
const permissions = ref(new Set<string>())
const worker = ref<
  | {
      displayName?: string
      employeeNo?: string
      jobTitle?: string
      teams?: Array<{ teamName?: string }>
    }
  | undefined
>(undefined)
const openTasks = ref<
  Array<{
    operationTaskId?: string
    workOrderNo?: string
    workOrderId?: string
    status?: string
    operationCode?: string | null
    workCenterName?: string | null
    workCenterCode?: string | null
    workCenterId?: string
    deviceAssetName?: string | null
    deviceAssetCode?: string | null
  }>
>([])
const myTasksPending = ref(false)
const warehouseEntries = ref<Array<{ key: string; label: string; route: string; count: number }>>(
  [],
)
const inspectionTasks = ref<
  Array<{
    inspectionTaskId?: string
    skuCode?: string
    batchNo?: string | null
    quantity?: number
    uomCode?: string
  }>
>([])
const organizationId = ref('org-001')
const environmentId = ref('env-dev')
const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
const inspectionPending = ref(false)
const inspectionError = ref<unknown>(null)
const refreshInspection = vi.fn(async () => {})

vi.mock('@/composables/useWorkbenchHome', () => {
  const HOME_PERMISSIONS = {
    myTasks: 'business.mes.dispatch.read',
    workerProfile: 'business.masterdata.resources.read',
    wmsReceipts: 'business.wms.receipts.read',
    wmsShipments: 'business.wms.shipments.read',
    quality: 'business.quality.inspection-records.read',
    alarms: 'business.iiot.alarms.read',
  }
  return {
    HOME_PERMISSIONS,
    usePdaIdentity: () => ({
      principalId: ref('user-emp-010'),
      loginName: ref('emp010'),
      organizationId,
      environmentId,
      hasScope,
      can: (code: string) => permissions.value.has(code),
      worker,
      displayName: computed(() => worker.value?.displayName || 'emp010'),
    }),
    useMyDispatchTasks: () => ({
      enabled: computed(() => permissions.value.has(HOME_PERMISSIONS.myTasks)),
      openTasks,
      queuedCount: computed(() => openTasks.value.filter((t) => t.status === 'Queued').length),
      inProgressCount: computed(
        () => openTasks.value.filter((t) => t.status === 'InProgress').length,
      ),
      pending: myTasksPending,
      error: ref(null),
      refresh: vi.fn(),
    }),
    useWarehouseSummary: () => ({
      enabled: computed(
        () =>
          permissions.value.has(HOME_PERMISSIONS.wmsReceipts) ||
          permissions.value.has(HOME_PERMISSIONS.wmsShipments),
      ),
      entries: warehouseEntries,
      pending: ref(false),
      lastUpdatedAt: ref('2026-07-28T10:20:30.000Z'),
    }),
    usePendingInspectionSummary: () => ({
      visible: computed(() => permissions.value.has(HOME_PERMISSIONS.quality)),
      scopeReady: hasScope,
      enabled: computed(() => permissions.value.has(HOME_PERMISSIONS.quality) && hasScope.value),
      tasks: inspectionTasks,
      total: computed(() => inspectionTasks.value.length),
      pending: inspectionPending,
      error: inspectionError,
      refresh: refreshInspection,
      lastUpdatedAt: ref('2026-07-28T10:20:30.000Z'),
    }),
  }
})

import HomePage from './index.vue'

const ALL_PERMISSIONS = [
  'business.mes.dispatch.read',
  'business.masterdata.resources.read',
  'business.wms.receipts.read',
  'business.wms.shipments.read',
  'business.quality.inspection-records.read',
  'business.iiot.alarms.read',
  'business.mes.reporting.read',
  'business.mes.materials.read',
  'business.mes.receipts.read',
  'business.mes.operations.read',
  'business.maintenance.work-orders.read',
  'business.maintenance.plans.read',
]

/** Find an app-wall grid tile by its visible label. */
function tileByLabel(wrapper: ReturnType<typeof mount>, label: string) {
  const btn = wrapper.findAll('button').find((b) => b.text().includes(label))
  if (!btn) throw new Error(`app-wall tile "${label}" not found`)
  return btn
}

describe('PDA home', () => {
  beforeEach(() => {
    push.mockReset()
    unacknowledgedCount.value = 0
    permissions.value = new Set(ALL_PERMISSIONS)
    worker.value = undefined
    openTasks.value = []
    myTasksPending.value = false
    warehouseEntries.value = []
    inspectionTasks.value = []
    organizationId.value = 'org-001'
    environmentId.value = 'env-dev'
    inspectionPending.value = false
    inspectionError.value = null
    refreshInspection.mockClear()
  })

  it('shows the unacknowledged-alarm count badge on the 查看报警 tile, and hides it at zero', async () => {
    const wrapper = mount(HomePage)
    expect(wrapper.find('.nv-m-grid-badge').exists()).toBe(false)

    unacknowledgedCount.value = 3
    await wrapper.vm.$nextTick()
    const alarmTile = tileByLabel(wrapper, '查看报警')
    const badge = alarmTile.find('.nv-m-grid-badge')
    expect(badge.exists()).toBe(true)
    expect(badge.text()).toContain('3')
  })

  it('renders the scan bar and the app wall from the task dictionary', () => {
    const wrapper = mount(HomePage)
    // 扫码条：以 placeholder 做稳健断言（不依赖 SFC 组件名推断）
    expect(wrapper.find('input[placeholder^="扫描"]').exists()).toBe(true)
    // 应用墙渲染字典中的任务标签（WMS / MES / 设备运维 三域）
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('报工')
    expect(wrapper.text()).toContain('报修')
    expect(wrapper.text()).toContain('点检')
    expect(wrapper.text()).toContain('查看报警')
  })

  it('tailors the app wall and sections to the principal permissions（仓储角色不见 MES 入口）', () => {
    permissions.value = new Set(['business.wms.receipts.read', 'business.wms.shipments.read'])
    warehouseEntries.value = [{ key: 'putaway', label: '待上架', route: '/wms/putaway', count: 4 }]
    const wrapper = mount(HomePage)

    // 仓储板块可见，「我的任务」「待检任务」按权限隐藏
    expect(wrapper.find('[data-testid="home-warehouse"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="home-my-tasks"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="home-inspection"]').exists()).toBe(false)

    // 应用墙只留 WMS 入口
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).not.toContain('报工')
    expect(wrapper.text()).not.toContain('查看报警')
  })

  it('shows the inspection source and missing-scope explanation when permitted without scope', () => {
    organizationId.value = ''
    environmentId.value = ''

    const wrapper = mount(HomePage)

    expect(wrapper.find('[data-testid="home-inspection"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('组织/环境范围未就绪')
    expect(wrapper.text()).toContain('质检待检任务服务（组织/环境范围，状态：待检）')
    expect(wrapper.text()).toContain('缺少组织或环境范围，未发起查询')
    expect(wrapper.text()).not.toContain('当前组织/环境范围暂无待检任务')
  })

  it('shows a retryable inspection error without presenting a business empty set', async () => {
    inspectionError.value = new Error('待检任务加载失败')
    const wrapper = mount(HomePage)

    expect(wrapper.find('[role="alert"]').text()).toContain('待检任务加载失败')
    expect(wrapper.text()).not.toContain('当前组织/环境范围暂无待检任务')

    await wrapper
      .get('[data-testid="home-inspection-error"]')
      .get('[data-testid="retry-list"]')
      .trigger('click')
    expect(refreshInspection).toHaveBeenCalledTimes(1)
  })

  it('shows the inspection business empty state only after a successful scoped response', () => {
    const wrapper = mount(HomePage)

    expect(wrapper.text()).toContain('当前组织/环境范围暂无待检任务')
  })

  it('renders my dispatch tasks with status tags, and an empty state without tasks', async () => {
    openTasks.value = [
      {
        operationTaskId: 'OT-001',
        workOrderNo: 'WO-2026-00001',
        status: 'InProgress',
        operationCode: 'OP-30',
        workCenterName: '装配一线',
      },
      { operationTaskId: 'OT-002', workOrderNo: 'WO-2026-00002', status: 'Queued' },
    ]
    const wrapper = mount(HomePage)
    expect(wrapper.text()).toContain('WO-2026-00001')
    expect(wrapper.text()).toContain('进行中')
    expect(wrapper.text()).toContain('工序 OP-30')

    openTasks.value = []
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toContain('暂无派给我的任务')
  })

  it('shows the worker identity in the header when the directory profile is available', () => {
    worker.value = {
      displayName: '吴桂芳',
      employeeNo: 'EMP-010',
      jobTitle: '操作工',
      teams: [{ teamName: '机加车间早班组' }],
    }
    const wrapper = mount(HomePage)
    expect(wrapper.get('[data-testid="home-name"]').text()).toBe('吴桂芳')
    expect(wrapper.text()).toContain('EMP-010')
    expect(wrapper.text()).toContain('操作工 · 机加车间早班组')
  })

  const ENTRIES: Array<[label: string, route: string]> = [
    // WMS
    ['收货入库', '/wms/inbound'],
    ['复核发货', '/wms/review'],
    ['拣货', '/wms/pick'],
    ['上架', '/wms/putaway'],
    ['盘点', '/wms/count'],
    // MES
    ['报工', '/mes/report'],
    ['领料', '/mes/issue'],
    ['完工入库', '/mes/receipt'],
    ['工序执行', '/mes/operation'],
    // 设备运维
    ['报修', '/equipment/repair'],
    ['点检', '/equipment/inspect'],
    ['查看报警', '/equipment/alarms'],
  ]

  it.each(ENTRIES)('navigates to %s → %s on tile click', async (label, route) => {
    const wrapper = mount(HomePage)
    const btn = tileByLabel(wrapper, label)
    push.mockClear()
    await btn.trigger('click')
    expect(push).toHaveBeenCalledWith(route)
  })

  it('echoes the scanned value in-page and does NOT navigate (scan-resolve is M5)', async () => {
    const wrapper = mount(HomePage)
    const input = wrapper.get('input[placeholder^="扫描"]')
    await input.setValue('WO-2026-0001')
    await input.trigger('keydown.enter')

    // 诚实的页内反馈：回显扫码内容，不做假跳转到尚不存在的 /scan。
    expect(wrapper.text()).toContain('已扫码：WO-2026-0001')
    expect(push).not.toHaveBeenCalled()
  })
})
