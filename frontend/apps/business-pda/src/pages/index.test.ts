import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'

// 真实 router.push 返回 Promise（index.vue 的导航会 `.catch`）；mock 同此契约。
const push = vi.fn(() => Promise.resolve())
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  RouterView: { template: '<div />' },
}))

const resolveBarcode = vi.hoisted(() => vi.fn())
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  resolveBusinessConsoleBarcode: resolveBarcode,
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
const warehouseEntries = ref<
  Array<{
    key: string
    label: string
    route: string
    count: number | null
    state: 'counted' | 'loading' | 'denied' | 'failed'
  }>
>([])
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
const inspectionHasSuccessfulResponse = ref(true)
const inspectionHasFailedResponse = ref(false)

vi.mock('@/composables/useWorkbenchHome', () => {
  const HOME_PERMISSIONS = {
    workerProfile: 'business.masterdata.resources.read',
    wmsReceipts: 'business.wms.receipts.read',
    wmsShipments: 'business.wms.shipments.read',
    wmsCounts: 'business.wms.counts.read',
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
    useWarehouseSummary: () => ({
      enabled: computed(
        () =>
          permissions.value.has(HOME_PERMISSIONS.wmsReceipts) ||
          permissions.value.has(HOME_PERMISSIONS.wmsShipments) ||
          permissions.value.has(HOME_PERMISSIONS.wmsCounts),
      ),
      entries: warehouseEntries,
      scopeDenied: computed(
        () =>
          warehouseEntries.value.length > 0 &&
          warehouseEntries.value.every((entry) => entry.state === 'denied'),
      ),
      hasDeniedEntry: computed(() =>
        warehouseEntries.value.some((entry) => entry.state === 'denied'),
      ),
      hasFailedEntry: computed(() =>
        warehouseEntries.value.some((entry) => entry.state === 'failed'),
      ),
      pending: ref(false),
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
      hasSuccessfulResponse: inspectionHasSuccessfulResponse,
      hasFailedResponse: inspectionHasFailedResponse,
    }),
  }
})

import HomePage from './index.vue'

const ALL_PERMISSIONS = [
  'business.masterdata.resources.read',
  'business.wms.receipts.read',
  'business.wms.shipments.read',
  'business.wms.counts.read',
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
    warehouseEntries.value = []
    inspectionTasks.value = []
    organizationId.value = 'org-001'
    environmentId.value = 'env-dev'
    inspectionPending.value = false
    inspectionError.value = null
    refreshInspection.mockClear()
    inspectionHasSuccessfulResponse.value = true
    inspectionHasFailedResponse.value = false
    resolveBarcode.mockReset()
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
    expect(wrapper.text()).not.toContain('我的任务')
    expect(wrapper.text()).not.toContain('暂无派给我的任务')
  })

  it('tailors the app wall and sections to the principal permissions（仓储角色不见 MES 入口）', () => {
    permissions.value = new Set(['business.wms.receipts.read', 'business.wms.shipments.read'])
    warehouseEntries.value = [
      { key: 'putaway', label: '待上架', route: '/wms/putaway', count: 4, state: 'counted' },
    ]
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

  it('shows count work without receipt entries for a counts-read-only principal', () => {
    permissions.value = new Set(['business.wms.counts.read'])
    warehouseEntries.value = [
      { key: 'count', label: '待盘点', route: '/wms/count', count: 7, state: 'counted' },
    ]

    const wrapper = mount(HomePage)

    expect(wrapper.find('[data-testid="home-warehouse"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('待盘点')
    expect(wrapper.text()).toContain('盘点')
    expect(wrapper.text()).not.toContain('收货入库')
  })

  it('does not expose count work to a receipts-read-only principal', () => {
    permissions.value = new Set(['business.wms.receipts.read'])
    warehouseEntries.value = [
      { key: 'inbound', label: '待收货', route: '/wms/inbound', count: 4, state: 'counted' },
    ]

    const wrapper = mount(HomePage)

    expect(wrapper.find('[data-testid="home-warehouse"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('待收货')
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).not.toContain('盘点')
  })

  /**
   * #3474：admin 有 WMS 权限码但没有仓储数据范围，四条请求全 403。旧实现把 403 回落成
   * `0`，四个卡片写着「待收货 0」——屏上断言「一件都没有」，而真相是「看不到任何数据」。
   *
   * 下面几条一律读**值格**（`home-warehouse-<key>-value`）并用 `toBe` 比**整串**，不用
   * `toContain`：整块 tile 的文本里还有标签（「待收货」等），子串断言对文案漂移
   * （`'无范围'` → `'无范围XX'`）零鉴别力——这正是 console 侧被抓到过一次的同一形状，
   * 按形状而不是按位置收掉。板块说明同理，按整句 `toBe`。
   */
  const NOTE = '[data-testid="home-warehouse-scope-note"]'
  const valueOf = (wrapper: ReturnType<typeof mount>, key: string) =>
    wrapper.get(`[data-testid="home-warehouse-${key}-value"]`).text()

  it('shows 无范围 — never a number — when every warehouse count is forbidden', () => {
    permissions.value = new Set(['business.wms.receipts.read', 'business.wms.counts.read'])
    warehouseEntries.value = [
      { key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'denied' },
      { key: 'putaway', label: '待上架', route: '/wms/putaway', count: null, state: 'denied' },
      { key: 'count', label: '待盘点', route: '/wms/count', count: null, state: 'denied' },
    ]

    const wrapper = mount(HomePage)

    for (const key of ['inbound', 'putaway', 'count']) {
      expect(valueOf(wrapper, key)).toBe('无范围')
      // 「不得是假读数」这条与文案分开钉：任何数字出现在值格里都是回归。
      expect(valueOf(wrapper, key)).not.toMatch(/\d/)
    }
    expect(wrapper.get(NOTE).text()).toBe(
      '当前账号没有仓储数据范围，看不到任何仓储单据——这不是「0 件」。请联系管理员分配仓库范围。',
    )
  })

  it('keeps a real zero rendered as 0 and shows no scope note', () => {
    permissions.value = new Set(['business.wms.counts.read'])
    warehouseEntries.value = [
      { key: 'count', label: '待盘点', route: '/wms/count', count: 0, state: 'counted' },
    ]

    const wrapper = mount(HomePage)

    expect(valueOf(wrapper, 'count')).toBe('0')
    expect(wrapper.find(NOTE).exists()).toBe(false)
  })

  it('marks only the denied tile when part of the warehouse scope is granted', () => {
    permissions.value = new Set(['business.wms.receipts.read'])
    warehouseEntries.value = [
      { key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'denied' },
      { key: 'putaway', label: '待上架', route: '/wms/putaway', count: 4, state: 'counted' },
    ]

    const wrapper = mount(HomePage)

    expect(valueOf(wrapper, 'inbound')).toBe('无范围')
    expect(valueOf(wrapper, 'putaway')).toBe('4')
    // 部分被拒时不得改口成「整块都看不到」——那会把真读到的 4 也谎报掉。
    expect(wrapper.get(NOTE).text()).toBe(
      '标「无范围」的项目没有分配给当前账号，其数量不可知，不是 0。需要这几项请联系管理员分配仓库范围。',
    )
  })

  it('separates a read failure from both a denied scope and a real zero', () => {
    permissions.value = new Set(['business.wms.receipts.read'])
    warehouseEntries.value = [
      { key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'failed' },
    ]

    const wrapper = mount(HomePage)

    expect(valueOf(wrapper, 'inbound')).toBe('加载失败')
    expect(valueOf(wrapper, 'inbound')).not.toMatch(/\d/)
    expect(wrapper.get(NOTE).text()).toBe(
      '标「加载失败」的项目本次没读到，数量不可知，不是 0。请下拉刷新或稍后重试。',
    )
  })

  // 三句说明必须互不相同、且每句都给出下一步动作。缺了这条，把三个分支返回同一句、
  // 或把动作句删成纯解释，都仍然全绿。
  it('gives each warehouse scope note its own sentence with a next action', () => {
    permissions.value = new Set(['business.wms.receipts.read'])
    const notes = new Set<string>()
    for (const entries of [
      [{ key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'denied' }],
      [
        { key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'denied' },
        { key: 'putaway', label: '待上架', route: '/wms/putaway', count: 4, state: 'counted' },
      ],
      [{ key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'failed' }],
    ] as (typeof warehouseEntries)['value'][]) {
      warehouseEntries.value = entries
      notes.add(mount(HomePage).get(NOTE).text())
    }
    expect(notes.size).toBe(3)
    for (const note of notes) expect(note).toMatch(/请联系管理员|请下拉刷新/)
  })

  it('keeps the inspection card without a business empty state when permitted without scope', () => {
    organizationId.value = ''
    environmentId.value = ''

    const wrapper = mount(HomePage)

    expect(wrapper.find('[data-testid="home-inspection"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('当前组织/环境范围暂无待检任务')
  })

  it('does not render cached inspection rows after the organization scope is lost', async () => {
    inspectionTasks.value = [
      {
        inspectionTaskId: 'OLD-INSPECTION',
        skuCode: 'OLD-SKU',
        batchNo: 'OLD-BATCH',
        quantity: 12,
        uomCode: 'PCS',
      },
    ]
    const wrapper = mount(HomePage)
    expect(wrapper.text()).toContain('OLD-SKU')

    organizationId.value = ''
    environmentId.value = ''
    await wrapper.vm.$nextTick()

    expect(wrapper.text()).not.toContain('OLD-SKU')
    expect(wrapper.text()).not.toContain('OLD-BATCH')
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

  it('shows a retryable inspection failure for success:false instead of a business empty state', async () => {
    inspectionHasSuccessfulResponse.value = false
    inspectionHasFailedResponse.value = true
    const wrapper = mount(HomePage)

    expect(wrapper.find('[role="alert"]').text()).toContain('待检任务加载失败')
    expect(wrapper.text()).not.toContain('当前组织/环境范围暂无待检任务')
    await wrapper.get('[data-testid="retry-list"]').trigger('click')
    expect(refreshInspection).toHaveBeenCalledTimes(1)
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

  it('uses the shared resolver to navigate a unique strong-ID result', async () => {
    resolveBarcode.mockResolvedValue({
      data: {
        success: true,
        data: {
          status: 'resolved',
          candidates: [{ objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } }],
        },
      },
    })
    const wrapper = mount(HomePage)
    const input = wrapper.get('input[placeholder^="扫描"]')
    await input.setValue('WO-2026-0001')
    await input.trigger('keydown.enter')

    await vi.waitFor(() =>
      expect(push).toHaveBeenCalledWith({ path: '/mes/report', query: { workOrderId: 'WO-1' } }),
    )
  })
})
