import { flushPromises, mount } from '@vue/test-utils'
import { computed, reactive, ref, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import OperationTasksPage from './operation-tasks.vue'

const state = vi.hoisted(() => ({ filters: undefined as unknown as Record<string, unknown> }))

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@nerv-iip/business-core', () => ({ openDownloadGrantBlob: vi.fn() }))
vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref('20') }),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({ resolveWorkCenter: (v?: string | null) => v ?? '无' }),
}))
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({ resources: computed(() => []) }),
}))
vi.mock('@/composables/useBusinessMes', async () => {
  state.filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev' })
  return {
    describeMesReadinessReason: (v: string) => ({ code: v, label: v, nextStep: '' }),
    useMesOperationTasks: () => ({
      filters: state.filters,
      operationTasks: computed(() => []),
      operationTasksError: shallowRef(undefined),
      operationTasksPending: shallowRef(false),
      operationTasksTotal: computed(() => 0),
      refreshOperationTasks: vi.fn(),
    }),
    useMesCurrentOperationSops: () => ({
      filters: reactive({}),
      currentSops: computed(() => []),
      currentSopsError: shallowRef(undefined),
      currentSopsPending: shallowRef(false),
      refreshCurrentSops: vi.fn(),
      createSopFileDownloadGrant: vi.fn(),
    }),
  }
})

const passthrough = { template: '<div><slot /></div>' }

function mountPage() {
  return mount(OperationTasksPage, {
    global: {
      stubs: {
        BusinessLayout: passthrough,
        WorkOrderQuickView: true,
        NvPageHeader: { template: '<header><slot name="actions" /></header>' },
        NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
        NvDataTable: {
          props: ['rows', 'columns', 'rowKey', 'page', 'pageSize', 'totalItems', 'loading', 'sort'],
          template: '<div data-testid="table" />',
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
  })

  it('binds aria-pressed to the active state and toggles it on click', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const button = wrapper.findAll('button').find((b) => b.text().includes('排程已失效'))!
    expect(button).toBeTruthy()
    expect(button.attributes('aria-pressed')).toBe('false')

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('true')
    expect(state.filters.status).toBe('scheduleInvalidated')

    await button.trigger('click')
    expect(button.attributes('aria-pressed')).toBe('false')
    expect(state.filters.status).toBeUndefined()
  })
})
