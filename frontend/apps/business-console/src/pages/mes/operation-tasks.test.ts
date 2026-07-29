import { flushPromises, mount } from '@vue/test-utils'
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

const state = vi.hoisted(() => ({ filters: undefined as unknown as Record<string, unknown> }))

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

vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn(),
  useRouter: () => ({ push: vi.fn() }),
}))
vi.mock('@nerv-iip/business-core', () => ({
  openDownloadGrantBlob: vi.fn(),
  statusActionGate: () => ({ executable: true, legalNoop: false }),
}))
vi.mock('@/composables/usePagedList', () => ({
  usePagedList: () => ({ page: ref(1), pageSize: ref('20') }),
}))
vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({ resolveWorkCenter: (v?: string | null) => v ?? '无' }),
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
vi.mock('@/composables/useBusinessMes', async () => {
  state.filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev' })
  return {
    makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
    useMesProductionReporting: () => ({
      recordProductionReport: vi.fn(),
      recordProductionReportError: shallowRef(undefined),
      recordProductionReportPending: shallowRef(false),
      reportScopeMessage: computed(() => ''),
      reportScopePending: shallowRef(false),
      reportScopeReady: computed(() => true),
      refreshProductionReportState: vi.fn(),
    }),
    describeMesReadinessReason: (v: string) => ({ code: v, label: v, nextStep: '' }),
    useMesOperationTasks: () => ({
      filters: state.filters,
      operationTasks: computed(() => []),
      operationTasksError: shallowRef(undefined),
      operationTasksPending: shallowRef(false),
      operationTasksTotal: computed(() => 0),
      operationScopeMessage: computed(() => ''),
      operationScopePending: shallowRef(false),
      operationScopeReady: computed(() => true),
      refreshOperationTasks: vi.fn(),
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
