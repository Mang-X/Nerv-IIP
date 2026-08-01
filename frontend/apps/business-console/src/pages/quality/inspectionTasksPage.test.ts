import { computed, nextTick, reactive, shallowRef } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import InspectionTasksPage from './inspection-tasks.vue'

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

const state = vi.hoisted(() => ({
  error: undefined as unknown,
  tasks: [
    {
      inspectionTaskId: 'TASK-LATE',
      status: 'pending',
      sourceType: 'receiving',
      sourceService: 'wms',
      sourceDocumentId: 'GR-001',
      skuCode: 'SKU-001',
      dueAtUtc: '2020-01-01T00:00:00Z',
      allowedActions: ['claim'],
      version: 1,
    },
    {
      inspectionTaskId: 'TASK-OP',
      status: 'pending',
      sourceType: 'operation',
      sourceService: 'mes',
      sourceDocumentId: 'WO-001',
      skuCode: 'SKU-002',
      dueAtUtc: '2030-01-01T00:00:00Z',
      allowedActions: ['claim'],
      version: 1,
    },
    {
      inspectionTaskId: 'TASK-ERP',
      status: 'pending',
      sourceType: 'receiving',
      sourceService: 'erp',
      sourceDocumentId: 'PR-001',
      skuCode: 'SKU-003',
      dueAtUtc: '2030-01-02T00:00:00Z',
      allowedActions: ['claim'],
      version: 1,
    },
    {
      inspectionTaskId: 'TASK-FINAL',
      status: 'pending',
      sourceType: 'final',
      sourceService: 'mes',
      sourceDocumentId: 'FGR-001',
      sourceDocumentLineId: 'WO-002',
      skuCode: 'SKU-004',
      dueAtUtc: '2030-01-03T00:00:00Z',
      allowedActions: ['claim'],
      version: 1,
    },
  ],
  push: vi.fn(),
  claimInspectionTask: vi.fn(async () => undefined),
  assignInspectionTask: vi.fn(async () => undefined),
  refreshTasks: vi.fn(),
  workers: [] as Array<{ userId: string; displayName?: string; employeeNo?: string }>,
  claimedRow: false,
  query: {} as Record<string, string>,
  hasLocator: false,
  initialFilters: undefined as Record<string, unknown> | undefined,
  pagedListOptions: undefined as { initialPageSize?: string } | undefined,
}))

vi.mock('@/composables/useQualityInspectionTasks', () => ({
  isInspectionTaskOverdue: (task: { status?: string; dueAtUtc?: string }) =>
    task.status === 'pending' && !!task.dueAtUtc && new Date(task.dueAtUtc).getTime() < Date.now(),
  useQualityInspectionTasks: (
    initial: { status?: string; sourceDocumentNo?: string; inspectionTaskId?: string } = {},
  ) => {
    state.initialFilters = initial
    const filters = reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      sourceType: 'all',
      status: initial.status,
      skuCode: '',
      skip: 0,
      take: 200,
      sourceDocumentNo: initial.sourceDocumentNo,
      inspectionTaskId: initial.inspectionTaskId,
    })
    const tasks = computed(() =>
      initial.status === 'completed'
        ? []
        : state.tasks
            .filter(
              (task) => task.sourceType === filters.sourceType || filters.sourceType === 'all',
            )
            .map((task, index) =>
              state.claimedRow && index === 2
                ? {
                    ...task,
                    allowedActions: [],
                    blockReasons: ['task-already-claimed'],
                    assignedInspectorUserId: 'user-emp-036',
                    version: 2,
                  }
                : task,
            ),
    )
    return {
      filters,
      hasLocator: computed(() => state.hasLocator),
      tasks,
      total: computed(() => (initial.status === 'completed' ? 0 : state.tasks.length)),
      pending: shallowRef(false),
      error: computed(() => state.error),
      lastUpdatedAt: shallowRef('2026-07-28T10:20:30Z'),
      refreshTasks: state.refreshTasks,
      claimInspectionTask: state.claimInspectionTask,
      assignInspectionTask: state.assignInspectionTask,
    }
  },
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessWorkers: () => ({
    workers: computed(() => state.workers),
    workersPending: shallowRef(false),
  }),
}))

// 物料筛选改成只选：目录 composable 内部走 pinia + colada，测试整体打桩给定候选。
vi.mock('@/composables/useQualityPickerCatalog', () => ({
  useQualitySkuCatalog: () => ({
    skuOptions: shallowRef([{ value: 'SKU-001', label: '示例物料' }]),
    skusPending: shallowRef(false),
    skuNameByCode: shallowRef(new Map([['SKU-001', '示例物料']])),
  }),
}))

vi.mock('@/composables/usePagedList', () => ({
  usePagedList: (_filters: unknown, options: { initialPageSize?: string }) => {
    state.pagedListOptions = options
    return { page: shallowRef(1), pageSize: shallowRef(200) }
  },
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { principalId: 'qa-user-001' } }),
}))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a :data-to="to"><slot /></a>' },
  useRoute: () => ({ query: state.query }),
  useRouter: () => ({ push: state.push }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvButton: {
    props: ['disabled', 'variant'],
    template: '<button :disabled="disabled"><slot /></button>',
  },
  NvDataTable: {
    props: {
      rows: { type: Array, default: () => [] },
      manual: { type: Boolean, default: false },
    },
    template:
      '<div data-testid="task-table" :data-manual="String(manual)"><div v-for="row in rows" :key="row.inspectionTaskId"><slot name="cell-sourceDocumentId" :row="row" /> {{ row.skuCode }}<slot name="cell-assignedInspectorUserId" :row="row" /><slot name="cell-dueAtUtc" :row="row" /><slot name="cell-actions" :row="row" /></div></div>',
  },
  NvField: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: { props: ['modelValue'], template: '<input :value="modelValue" />' },
  NvDialog: {
    props: ['open'],
    template: '<div v-if="open"><slot /></div>',
  },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  WorkerSelect: { template: '<select />' },
  NvPageHeader: { template: '<header><slot /></header>' },
  NvSectionCard: {
    props: ['description', 'value'],
    template: '<div>{{ description }} {{ value }}</div>',
  },
  NvSectionCards: { template: '<section><slot /></section>' },
}

describe('quality inspection task workbench page', () => {
  beforeEach(() => {
    state.error = undefined
    state.push.mockReset()
    state.query = {}
    state.hasLocator = false
    state.initialFilters = undefined
    state.pagedListOptions = undefined
    state.claimedRow = false
    state.workers = []
    state.claimInspectionTask.mockReset()
    state.claimInspectionTask.mockResolvedValue(undefined)
    state.assignInspectionTask.mockReset()
    state.assignInspectionTask.mockResolvedValue(undefined)
    state.refreshTasks.mockReset()
  })

  it('renders real task context and an explicit overdue label', () => {
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('GR-001')
    expect(wrapper.text()).toContain('已超期')
    expect(wrapper.text()).toContain('待检任务')
    expect(wrapper.text()).toContain('时限内完成率')
    expect(state.pagedListOptions?.initialPageSize).toBe('200')
    expect(wrapper.find('[data-to="/wms/inbound"]').exists()).toBe(true)
    expect(wrapper.find('[data-to="/mes/work-orders/WO-001"]').exists()).toBe(true)
    expect(wrapper.find('[data-to="/mes/work-orders/WO-002"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-to="/wms/inbound"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('PR-001')
    expect(wrapper.get('[data-testid="task-table"]').attributes('data-manual')).toBe('true')
    const actionColumn = (
      wrapper.vm as unknown as {
        columns: Array<{ key: string; headerClass?: string; cellClass?: string }>
      }
    ).columns.find((column) => column.key === 'actions')
    expect(actionColumn?.headerClass).toContain('sticky')
    expect(actionColumn?.cellClass).toContain('sticky')
    const columnKeys = (
      wrapper.vm as unknown as {
        columns: Array<{ key: string }>
      }
    ).columns.map((column) => column.key)
    expect(columnKeys).not.toContain('inspectionTaskId')
    expect(columnKeys).not.toContain('inspectionPlanId')
  })

  it('uses the composable locator state as the pagination mode source of truth', () => {
    state.hasLocator = true
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })

    expect(state.initialFilters).toEqual({ status: 'pending' })
    expect(wrapper.get('[data-testid="task-table"]').attributes('data-manual')).toBe('false')
  })

  it('keeps source-type empty states honest about current-page filtering and service total', async () => {
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    const filters = (wrapper.vm as unknown as { filters: { sourceType: string } }).filters
    filters.sourceType = 'operation'
    await nextTick()

    expect(wrapper.text()).toContain('本页匹配 1 个 / 服务总数 4 个')
    expect(wrapper.text()).toContain('筛选仅按当前页匹配')
  })

  it('claims before opening the existing inspection form without inventing a source document number', async () => {
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    const action = wrapper
      .findAll('button')
      .find((button) => button.text().includes('认领并开始检验'))
    await action?.trigger('click')
    await flushPromises()

    expect(state.claimInspectionTask).toHaveBeenCalledWith('TASK-LATE', 1)
    expect(state.push).toHaveBeenCalledWith({
      path: '/quality/inspections',
      query: expect.objectContaining({
        inspectionTaskId: 'TASK-LATE',
        sourceDocumentId: 'GR-001',
      }),
    })
    expect(state.push.mock.calls[0]?.[0]?.query).not.toHaveProperty('sourceDocumentNo')
  })

  it('offers reassignment for a claimed task and never renders its account id', () => {
    state.claimedRow = true
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })

    expect(wrapper.get('[data-testid="inspection-task-assignee-TASK-ERP"]').text()).toBe('—')
    expect(wrapper.text()).toContain('改派')
    expect(wrapper.text()).not.toContain('user-emp-036')
  })

  it('renders a directory-backed holder as name and employee number', () => {
    state.claimedRow = true
    state.workers = [{ userId: 'user-emp-036', displayName: '张三', employeeNo: 'EMP-036' }]
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })

    expect(wrapper.get('[data-testid="inspection-task-assignee-TASK-ERP"]').text()).toBe(
      '张三 · EMP-036',
    )
  })

  it('shows a retryable failure state instead of an empty success state', () => {
    // 5xx 走分层透传映射成人话（不把 503 原文甩给用户），仍是可重试的失败态。
    state.error = new Error('503')
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('服务暂时不可用')
    expect(wrapper.text()).not.toContain('503')
    expect(wrapper.text()).toContain('重试')
    expect(wrapper.text()).not.toContain('当前没有待检任务')
    expect(wrapper.find('[data-testid="task-table"]').exists()).toBe(false)
  })

  // MAN-698 批次 A（#1298 规格轴）：这里原来靠 `error instanceof Error` 判 403，
  // 而 generated client 在 throwOnError 下抛的是**解析后的响应体对象**——判定永远不成立，
  // 真实 403 退化成普通失败态。现在按状态码判，恢复「无权限」语义。
  it('真实 403（响应体对象 + 拦截器挂的 response）走「无权限」文案，不是通用失败', () => {
    state.error = { title: 'Forbidden', response: { status: 403 } }
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('没有查看质检待检任务的权限')
    expect(wrapper.text()).not.toContain('待检任务加载失败')
    expect(wrapper.text()).not.toContain('Forbidden')
  })

  it('RFC7807 直接带 status 的 403 同样走「无权限」文案', () => {
    state.error = { status: 403, detail: 'forbidden' }
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('没有查看质检待检任务的权限')
  })

  it('后端明确给了中文拒绝理由时原样上屏，不被兜底吞掉', () => {
    state.error = { message: '当前业务范围内没有质量模块的读取授权。', response: { status: 400 } }
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('当前业务范围内没有质量模块的读取授权。')
  })

  it('consumes the stable source document locator contract from WMS', () => {
    state.query = { sourceDocumentNo: ' ASN-20260718-0087 ' }
    state.hasLocator = true
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })

    expect(state.initialFilters).toEqual({
      status: 'pending',
      sourceDocumentNo: 'ASN-20260718-0087',
    })
    expect(wrapper.text()).toContain('正在定位收货单 ASN-20260718-0087 的待检任务')
    expect(wrapper.get('[data-testid="task-table"]').attributes('data-manual')).toBe('false')
  })

  it('uses client pagination when locating an exact inspection task', () => {
    state.query = { inspectionTaskId: ' TASK-LATE ' }
    state.hasLocator = true
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })

    expect(state.initialFilters).toEqual({
      status: 'pending',
      inspectionTaskId: 'TASK-LATE',
    })
    expect(wrapper.text()).toContain('正在定位待检任务 TASK-LATE')
    expect(wrapper.get('[data-testid="task-table"]').attributes('data-manual')).toBe('false')
  })
})
