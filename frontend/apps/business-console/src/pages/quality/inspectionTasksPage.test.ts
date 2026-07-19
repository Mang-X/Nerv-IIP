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
      sourceDocumentId: 'GR-001',
      skuCode: 'SKU-001',
      dueAtUtc: '2020-01-01T00:00:00Z',
    },
  ],
  push: vi.fn(),
}))

vi.mock('@/composables/useQualityInspectionTasks', () => ({
  isInspectionTaskOverdue: (task: { status?: string; dueAtUtc?: string }) =>
    task.status === 'pending' && !!task.dueAtUtc && new Date(task.dueAtUtc).getTime() < Date.now(),
  useQualityInspectionTasks: (initial: { status?: string } = {}) => {
    const filters = reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      sourceType: 'all',
      status: initial.status,
      skuCode: '',
      skip: 0,
      take: 200,
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
  usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef(200) }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { principalId: 'qa-user-001' } }),
}))

vi.mock('vue-router', () => ({
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
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
      '<div><div v-for="row in rows" :key="row.inspectionTaskId">{{ row.sourceDocumentId }} {{ row.skuCode }}<slot name="cell-dueAtUtc" :row="row" /><slot name="cell-actions" :row="row" /></div></div>',
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
  })

  it('renders real task context and an explicit overdue label', () => {
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('GR-001')
    expect(wrapper.text()).toContain('已超期')
    expect(wrapper.text()).toContain('待检总量')
    expect(wrapper.text()).toContain('当前业务范围的待检任务总数')
  })

  it('shows a retryable failure state instead of an empty success state', () => {
    state.error = new Error('503')
    const wrapper = mount(InspectionTasksPage, { global: { stubs } })
    expect(wrapper.text()).toContain('待检任务加载失败，请稍后重试。')
    expect(wrapper.text()).toContain('重试')
    expect(wrapper.text()).not.toContain('当前没有待检任务')
  })
})
