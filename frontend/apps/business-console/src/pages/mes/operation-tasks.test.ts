import { flushPromises, mount } from '@vue/test-utils'
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { computed, reactive, ref, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import OperationTasksPage from './operation-tasks.vue'

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
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
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

const state = vi.hoisted(() => ({
  filters: undefined as unknown as Record<string, unknown>,
  operationRows: [] as BusinessConsoleMesOperationTaskRow[],
  permissionCodes: [] as string[],
  scopeError: undefined as unknown,
  scopePending: false,
  tasksState: 'ready',
  refresh: vi.fn(),
}))

// 派工弹窗的技能筛选改取技能目录主数据（中文 skillName）；目录不是本用例被测对象。
vi.mock('@/composables/usePromotedCatalogs', async () => {
  const { computed } = await import('vue')
  return {
    useSkillCatalog: () => ({
      skills: computed(() => [{ skillCode: 'cnc-operation', skillName: 'CNC 操作' }]),
      skillsPending: computed(() => false),
    }),
  }
})

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return { permissionCodes: state.permissionCodes }
    },
  }),
}))

const routerPush = vi.fn()
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn(),
  useRouter: () => ({ push: routerPush }),
}))
vi.mock('@nerv-iip/business-core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/business-core')>()),
  openSopFileContent: vi.fn(async () => {}),
  statusActionGate: () => ({ executable: true, legalNoop: false }),
}))
vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref('20') }),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveWorkCenter: (v?: string | null) => v ?? '无',
    resolveSkuLabel: (v?: string | null) => v ?? '无',
  }),
}))
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({
    resources: computed(() => []),
    resourcesPending: shallowRef(false),
  }),
  useBusinessSkus: () => ({ skus: computed(() => []) }),
  useBusinessWorkers: () => ({
    workers: computed(() => []),
    workersPending: shallowRef(false),
    filters: reactive({}),
  }),
}))
vi.mock('@/composables/mes/useProductionReportSerialOptions', () => ({
  useProductionReportSerialOptions: () => ({
    serialPolicy: ref('none'),
    labelTemplates: ref([]),
    serialOptionsReady: ref(true),
    serialOptionsPending: ref(false),
    refreshSerialOptions: vi.fn(),
  }),
}))

vi.mock('@/composables/useBusinessMes', async () => {
  state.filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev' })
  return {
    makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
    // #1288 工具栏作业范围选择入口（MesWorkScopeSelect）
    useMesWorkScopeSelection: () => ({
      scopeOptions: ref([]),
      scopeSelectionValue: ref(undefined),
      scopeReady: ref(true),
      scopeMessage: ref(''),
      scopePending: computed(() => state.scopePending),
      scopeError: computed(() => state.scopeError),
      scopeUnavailable: ref(false),
      selectedScope: ref(undefined),
      principalIdentity: ref('principal-test'),
      requireSelectedScope: vi.fn(),
    }),
    useMesProductionReporting: () => ({
      recordProductionReport: vi.fn(),
      restoreProductionReport: vi.fn(),
      readProductionPrintStatus: vi.fn(),
      recordProductionReportError: shallowRef(undefined),
      recordProductionReportPending: shallowRef(false),
      reportScopeMessage: computed(() => ''),
      reportScopePending: shallowRef(false),
      reportScopeReady: computed(() => true),
      refreshProductionReportState: vi.fn(),
    }),
    useMesProductionMaterialLots: () => ({
      materialsReadPermission: shallowRef(false),
      materialLotsPending: shallowRef(false),
      materialLotsError: shallowRef(undefined),
      availableMaterialLots: shallowRef([]),
      refreshMaterialLots: vi.fn(),
    }),
    useMesScrapReasonCodes: () => ({
      qualityInspectionRecordsReadPermission: shallowRef(false),
      scrapReasonCodesPending: shallowRef(false),
      scrapReasonCodesError: shallowRef(undefined),
      scrapReasonCodes: shallowRef([]),
      refreshScrapReasonCodes: vi.fn(),
    }),
    useMesWorkOrderDetail: () => ({
      filters: reactive({ workOrderId: '' }),
      detail: ref({ workOrderId: 'WO-A', operationTasks: [] }),
      detailError: shallowRef(undefined),
      detailPending: ref(false),
    }),
    describeMesReadinessReasons: () => [],
    describeMesReadinessReason: (v: string) => ({ code: v, label: v, nextStep: '' }),
    useMesOperationTasks: () => ({
      filters: state.filters,
      operationTasks: computed(() => state.operationRows),
      operationTasksError: shallowRef(undefined),
      operationTasksPending: shallowRef(false),
      operationTasksTotal: computed(() => state.operationRows.length),
      operationListScope: computed(() => ({
        kind: 'work-center',
        id: 'WC-A',
        displayName: '精加工一线',
      })),
      operationListScopeError: computed(() => state.scopeError),
      operationListScopePending: computed(() => state.scopePending),
      operationTasksState: computed(() => state.tasksState),
      operationListScopeMessage: computed(() =>
        state.scopeError ? '作业范围核验失败，当前操作已禁用。请刷新后重试。' : '',
      ),
      operationListScopeReady: computed(() => true),
      operationScopeMessage: computed(() => ''),
      operationScopePending: shallowRef(false),
      operationScopeReady: computed(() => true),
      operationTasksLastUpdatedAt: computed(() => '2026-07-28T10:20:30.000Z'),
      operationTasksHasSuccessfulResponse: computed(() => true),
      operationTasksHasFailedResponse: computed(() => false),
      refreshOperationTasks: state.refresh,
      startOperationTask: vi.fn(),
      pauseOperationTask: vi.fn(),
      resumeOperationTask: vi.fn(),
      completeOperationTask: vi.fn(),
    }),
    useMesDispatchTasks: () => ({
      assignDispatchTask: vi.fn(),
      assignDispatchTaskPending: shallowRef(false),
    }),
    useMesCurrentOperationSops: () => ({
      filters: reactive({}),
      currentSops: computed(() => []),
      currentSopsError: shallowRef(undefined),
      currentSopsPending: shallowRef(false),
      refreshCurrentSops: vi.fn(),
    }),
  }
})

const passthrough = { template: '<div><slot /></div>' }

function mountPage(realQuickView = false) {
  return mount(OperationTasksPage, {
    global: {
      stubs: {
        BusinessLayout: passthrough,
        WorkOrderQuickView: realQuickView ? false : true,
        NvPageHeader: {
          props: ['count'],
          template: '<header>{{ count }}<slot name="actions" /></header>',
        },
        NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
        NvDataTable: {
          props: [
            'rows',
            'columns',
            'rowKey',
            'page',
            'pageSize',
            'totalItems',
            'loading',
            'sort',
            'error',
            'errorMessage',
            'emptyMessage',
          ],
          template: `
            <div data-testid="table">
              <p v-if="error" role="alert">{{ errorMessage }}<button @click="$emit('retry')">重新加载</button></p>
              <p v-else-if="!loading && !rows.length">{{ emptyMessage }}</p>
              <span v-for="column in columns" :key="column.key">{{ column.header }}</span>
              <div v-for="row in rows" :key="row.operationTaskId" data-testid="operation-row">
                <slot name="cell-operationTaskId" :row="row" />
                <slot name="cell-workOrderId" :row="row" />
                <slot name="cell-actualHours" :row="row" />
                <slot name="cell-actions" :row="row" />
              </div>
            </div>
          `,
        },
        // inheritAttrs (default) lets :aria-pressed / @click fall through to the real <button>.
        NvButton: { template: '<button><slot /></button>' },
        // Render nothing for the status/work-center/shift selects so their reka Select internals never
        // mount (they need SelectRoot context). The 排程已失效 button is a sibling, not inside NvSelect.
        NvSelect: { template: '<div />' },
      },
    },
  })
}

describe('operation-tasks 排程已失效 quick filter', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    state.operationRows = []
  })

  it('执行列表在公开身份为 UUID 时展示真实序号并保留完整身份查看', () => {
    const id = '019fbb41-aaaa-7aaa-8aaa-aaaaaaaaaaaa'
    state.operationRows = [
      { operationTaskId: id, operationTaskNo: null, operationSequence: 20, workOrderId: 'WO-1' },
    ]
    const wrapper = mountPage()
    const identity = wrapper.get(`[title="${id}"]`)
    expect(identity.text()).toBe('工序 20')
    expect(identity.text()).not.toContain(id)
    wrapper.unmount()
  })

  it.each([null, '', '   '])('执行列表在业务号为 %j 时显示完整任务身份', (operationTaskNo) => {
    const id = 'WO-宁波精密制造长工单-20261017-000001-OP-20'
    state.operationRows = [{ operationTaskId: id, operationTaskNo, workOrderId: 'WO-1' }]
    const wrapper = mountPage()
    const identity = wrapper.get(`[title="${id}"]`)
    expect(identity.text()).toBe(id)
    wrapper.unmount()
  })

  it('binds aria-pressed to the active state and toggles it on click', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const button = wrapper.findAll('button').find((b) => b.text().includes('排程已失效'))!
    expect(button).toBeTruthy()
    expect(button.attributes('aria-pressed')).toBe('false')

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('true')
    expect(state.filters.status).toBe('ScheduleInvalidated')

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('false')
    expect(state.filters.status).toBeUndefined()
  })

  it('shows completed cumulative labor and machine hours while keeping zero distinct from no actuals', async () => {
    state.operationRows = [
      {
        operationTaskId: 'OP-ACTUAL',
        workOrderId: 'WO-ACTUAL',
        operationSequence: 10,
        actualLaborHours: 1.25,
        actualMachineHours: 0.5,
      },
      {
        operationTaskId: 'OP-ZERO',
        workOrderId: 'WO-ZERO',
        operationSequence: 20,
        actualLaborHours: 0,
        actualMachineHours: 0,
      },
      {
        operationTaskId: 'OP-PENDING',
        workOrderId: 'WO-PENDING',
        operationSequence: 30,
        actualLaborHours: null,
        actualMachineHours: null,
      },
      {
        operationTaskId: 'OP-TINY',
        workOrderId: 'WO-TINY',
        operationSequence: 40,
        actualLaborHours: 0.00004,
        actualMachineHours: 0.00004,
      },
    ]

    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('实际工时')
    const actualHourCells = wrapper.findAll('[data-testid="actual-hours"]')
    expect(actualHourCells).toHaveLength(4)
    expect(actualHourCells[0].text()).toMatch(/人工\s*1\.25 小时/)
    expect(actualHourCells[0].text()).toMatch(/机器\s*0\.5 小时/)
    expect(actualHourCells[1].text()).toMatch(/人工\s*0 小时/)
    expect(actualHourCells[1].text()).toMatch(/机器\s*0 小时/)
    expect(actualHourCells[2].text()).toMatch(/人工\s*暂无实绩/)
    expect(actualHourCells[2].text()).toMatch(/机器\s*暂无实绩/)
    expect(actualHourCells[3].text()).toMatch(/人工\s*小于 0\.0001 小时/)
    expect(actualHourCells[3].text()).toMatch(/机器\s*小于 0\.0001 小时/)
  })
})

describe('operation-tasks 首件检验记录入口', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    state.operationRows = [
      {
        operationTaskId: 'OP-10',
        workOrderId: 'WO-A',
        workCenterId: 'WC-FILL',
        operationSequence: 10,
      },
    ]
  })

  // 服务端在首件未判合格时拒绝批量报工（#2780），被拦下的人要能就地走到首件检验记录。
  it('opens the first-article records view without carrying any document context', async () => {
    const wrapper = mountPage()
    await flushPromises()

    // 行操作是真菜单：先展开再点，避免对着没挂载的幽灵弹层断言。
    await wrapper.get('[data-slot="nv-dropdown-menu-trigger"]').trigger('keydown', { key: 'Enter' })
    await flushPromises()
    const entry = [...document.body.querySelectorAll('[role="menuitem"]')].find((node) =>
      node.textContent?.includes('首件检验记录'),
    ) as HTMLElement | undefined
    expect(entry).toBeTruthy()
    entry!.click()
    await flushPromises()

    // 不带任何单据上下文：落点的 query watch 见到 workOrderId 就会自动弹开「创建检验记录」抽屉，
    // 把来看结论的人丢进新建表单（真机走查实证）。
    expect(routerPush).toHaveBeenCalledWith({
      path: '/quality/inspections',
      query: { view: 'first-article-records' },
    })
  })
})

describe('工序页开工 readiness', () => {
  it.each([
    {
      blockReasons: ['WORK_ORDER_NOT_RELEASED'],
      allowedActions: [],
      disabled: true,
      reason: '先在工单列表下达',
    },
    {
      blockReasons: ['QUALITY_HOLD_ACTIVE'],
      allowedActions: [],
      disabled: true,
      reason: '处理质量冻结',
    },
    { blockReasons: [], allowedActions: ['start'], disabled: false, reason: undefined },
  ])(
    '反映权威开工检查 $blockReasons',
    async ({ blockReasons, allowedActions, disabled, reason }) => {
      state.operationRows = [
        { operationTaskId: 'OP-CNC-10', status: 'Queued', blockReasons, allowedActions },
      ]
      const wrapper = mountPage()
      await flushPromises()
      await wrapper
        .get('[data-slot="nv-dropdown-menu-trigger"]')
        .trigger('keydown', { key: 'Enter' })
      await flushPromises()
      const start = [...document.body.querySelectorAll('[role="menuitem"]')].find(
        (item) => item.textContent?.trim() === '开工',
      )!
      expect(start).toBeTruthy()
      expect(start.getAttribute('data-disabled') !== null).toBe(disabled)
      if (reason) expect(start.getAttribute('title')).toContain(reason)
      else expect(start.getAttribute('title')).toBeNull()
      wrapper.unmount()
    },
  )
})

describe('工序范围读取状态', () => {
  beforeEach(() => {
    state.operationRows = []
    state.scopeError = undefined
    state.scopePending = false
    state.tasksState = 'ready'
    state.refresh.mockClear()
  })

  it('范围失败显示任务尚未读取，表格恢复动作与页头刷新均接入重读', async () => {
    state.scopeError = new Error('范围传输失败')
    state.tasksState = 'idle'
    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('范围读取失败')
    expect(wrapper.text()).toContain('尚未读取任务')
    expect(wrapper.text()).not.toContain('0 个工序任务')
    expect(wrapper.text()).not.toContain('当前没有工序任务')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '刷新')!
      .trigger('click')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '重新加载')!
      .trigger('click')
    expect(state.refresh).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('范围读取中不展示零任务或真实空列表文案', async () => {
    state.scopePending = true
    state.tasksState = 'idle'
    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('尚未读取任务')
    expect(wrapper.text()).not.toContain('0 个工序任务')
    expect(wrapper.text()).not.toContain('当前没有工序任务')
    wrapper.unmount()
  })

  it('任务成功返回空列表时仍显示真实零任务', async () => {
    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).toContain('0 个工序任务')
    expect(wrapper.text()).toContain('当前没有工序任务')
    wrapper.unmount()
  })
})

describe('工序执行的授权质检与工单来源接力', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    state.permissionCodes = []
    state.scopeError = undefined
    state.scopePending = false
    state.tasksState = 'ready'
    state.operationRows = [{ operationTaskId: 'OP-20', workOrderId: 'WO-A', workCenterId: 'WC-A' }]
  })

  async function openMenu(wrapper: ReturnType<typeof mountPage>) {
    await wrapper.get('[data-slot="nv-dropdown-menu-trigger"]').trigger('keydown', { key: 'Enter' })
    await flushPromises()
    return [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
  }

  it.each([
    { permissions: [] },
    { permissions: ['business.quality.inspection-records.read'] },
    { permissions: ['business.quality.inspection-records.create'] },
  ])('缺目标读取或创建权限时不提供呼叫质检死路 $permissions', async ({ permissions }) => {
    state.permissionCodes = permissions
    const wrapper = mountPage()
    const items = await openMenu(wrapper)
    expect(items.some((item) => item.textContent?.includes('呼叫质检'))).toBe(false)
    expect(routerPush).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('有目标读取及创建权限时呼叫质检保留同工单工序与工作中心', async () => {
    state.permissionCodes = [
      'business.quality.inspection-records.read',
      'business.quality.inspection-records.create',
    ]
    const wrapper = mountPage()
    const items = await openMenu(wrapper)
    const entry = items.find((item) => item.textContent?.includes('呼叫质检'))!
    expect(entry).toBeTruthy()
    entry.click()
    await flushPromises()
    expect(routerPush).toHaveBeenCalledWith({
      path: '/quality/inspections',
      query: {
        workOrderId: 'WO-A',
        operationTaskId: 'OP-20',
        workCenterId: 'WC-A',
      },
    })
    wrapper.unmount()
  })

  it.each(['工单编号', '查看工单'])('%s经速览打开完整详情保留原工序', async (entry) => {
    const wrapper = mountPage(true)
    await flushPromises()
    if (entry === '工单编号') {
      await wrapper.get('[data-testid="operation-row"] button').trigger('click')
    } else {
      const items = await openMenu(wrapper)
      items.find((item) => item.textContent?.includes('查看工单'))!.click()
    }
    await flushPromises()
    const openFull = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.includes('打开完整详情'),
    )!
    expect(openFull).toBeTruthy()
    openFull.click()
    await flushPromises()
    expect(routerPush).toHaveBeenCalledWith({
      path: '/mes/work-orders/WO-A',
      query: { operationTaskId: 'OP-20' },
    })
    wrapper.unmount()
  })

  it('再次从工单级行打开速览不沿用上一行的工序', async () => {
    state.operationRows.push({ workOrderId: 'WO-A' })
    const wrapper = mountPage(true)
    await flushPromises()
    await wrapper.findAll('[data-testid="operation-row"]')[0].get('button').trigger('click')
    await flushPromises()
    const close = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '关闭',
    )!
    close.click()
    await flushPromises()
    await wrapper.findAll('[data-testid="operation-row"]')[1].get('button').trigger('click')
    await flushPromises()
    const openFull = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.includes('打开完整详情'),
    )!
    openFull.click()
    await flushPromises()
    expect(routerPush).toHaveBeenCalledWith('/mes/work-orders/WO-A')
    wrapper.unmount()
  })
})
