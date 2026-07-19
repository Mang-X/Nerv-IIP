import { computed, reactive, shallowRef } from 'vue'
import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import InspectionTasksPage from './inspection-tasks.vue'

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
    },
    {
      inspectionTaskId: 'TASK-OP',
      status: 'pending',
      sourceType: 'operation',
      sourceService: 'mes',
      sourceDocumentId: 'WO-001',
      skuCode: 'SKU-002',
      dueAtUtc: '2030-01-01T00:00:00Z',
    },
    {
      inspectionTaskId: 'TASK-ERP',
      status: 'pending',
      sourceType: 'receiving',
      sourceService: 'erp',
      sourceDocumentId: 'PR-001',
      skuCode: 'SKU-003',
      dueAtUtc: '2030-01-02T00:00:00Z',
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
    },
  ],
  push: vi.fn(),
  query: {} as Record<string, string>,
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
        : state.tasks.filter(
            (task) => task.sourceType === filters.sourceType || filters.sourceType === 'all',
          ),
    )
    return {
      filters,
      tasks,
      total: computed(() => (initial.status === 'completed' ? 0 : state.tasks.length)),
      pending: shallowRef(false),
      error: computed(() => state.error),
      refreshTasks: vi.fn(),
    }
  },
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
    props: ['rows'],
    template:
      '<div data-testid="task-table"><div v-for="row in rows" :key="row.inspectionTaskId"><slot name="cell-sourceDocumentId" :row="row" /> {{ row.skuCode }}<slot name="cell-dueAtUtc" :row="row" /><slot name="cell-actions" :row="row" /></div></div>',
  },
  NvField: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: { props: ['modelValue'], template: '<input :value="modelValue" />' },
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
    state.initialFilters = undefined
    state.pagedListOptions = undefined
  })

  it('renders real task context and an explicit overdue label', () => {
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('GR-001')
    expect(wrapper.text()).toContain('已超期')
    expect(wrapper.text()).toContain('待检总量')
    expect(wrapper.text()).toContain('当前业务范围的待检任务总数')
    expect(state.pagedListOptions?.initialPageSize).toBe('200')
    expect(wrapper.find('[data-to="/wms/inbound"]').exists()).toBe(true)
    expect(wrapper.find('[data-to="/mes/work-orders/WO-001"]').exists()).toBe(true)
    expect(wrapper.find('[data-to="/mes/work-orders/WO-002"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-to="/wms/inbound"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('PR-001')
    const actionColumn = (
      wrapper.vm as unknown as {
        columns: Array<{ key: string; headerClass?: string; cellClass?: string }>
      }
    ).columns.find((column) => column.key === 'actions')
    expect(actionColumn?.headerClass).toContain('sticky')
    expect(actionColumn?.cellClass).toContain('sticky')
  })

  it('opens the existing inspection form without inventing a source document number', async () => {
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    const action = wrapper.findAll('button').find((button) => button.text().includes('开始检验'))
    await action?.trigger('click')

    expect(state.push).toHaveBeenCalledWith({
      path: '/quality/inspections',
      query: expect.objectContaining({
        inspectionTaskId: 'TASK-LATE',
        sourceDocumentId: 'GR-001',
      }),
    })
    expect(state.push.mock.calls[0]?.[0]?.query).not.toHaveProperty('sourceDocumentNo')
  })

  it('shows a retryable failure state instead of an empty success state', () => {
    state.error = new Error('503')
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('待检任务加载失败，请稍后重试。')
    expect(wrapper.text()).toContain('重试')
    expect(wrapper.text()).not.toContain('当前没有待检任务')
    expect(wrapper.find('[data-testid="task-table"]').exists()).toBe(false)
  })

  it('consumes the stable source document locator contract from WMS', () => {
    state.query = { sourceDocumentNo: ' ASN-20260718-0087 ' }
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })

    expect(state.initialFilters).toEqual({
      status: 'pending',
      sourceDocumentNo: 'ASN-20260718-0087',
    })
    expect(wrapper.text()).toContain('正在定位收货单 ASN-20260718-0087 的待检任务')
  })
})
