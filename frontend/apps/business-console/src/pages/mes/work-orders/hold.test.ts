import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrderDetailPage from './[workOrderId].vue'
import WorkOrdersListPage from './index.vue'

const routeState = vi.hoisted(() => ({ params: { workOrderId: 'WO-1' }, query: {} as Record<string, string> }))
const routerState = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn() }))
vi.mock('vue-router', () => ({
  useRoute: () => routeState,
  useRouter: () => routerState,
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/composables/useScheduleInvalidation', () => ({
  resolveScheduleStatus: () => ({ key: 'scheduled', label: '已排程', tone: 'info' }),
  scheduleInvalidationHint: () => '',
}))

vi.mock('@/composables/mes/useMesReferenceLabels', async (orig) => ({
  ...(await orig()),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({ resolveSku: (v?: string | null) => v ?? '无' }),
}))

const detailState = vi.hoisted(() => ({
  qualityHolds: [] as Array<Record<string, unknown>>,
  workOrders: [] as Array<Record<string, unknown>>,
}))

vi.mock('@/composables/useBusinessMes', () => ({
  describeMesReadinessReason: (code: string) => ({ code, label: code, nextStep: '' }),
  useMesWorkOrderDetail: () => ({
    activateCancelPreview: vi.fn(),
    cancelPreviewError: ref(undefined),
    cancelPreviewPending: ref(false),
    cancelPreviewReady: ref(false),
    cancelWorkOrder: vi.fn(),
    cancelWorkOrderPending: ref(false),
    detail: ref({
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: detailState.qualityHolds,
    }),
    detailError: ref(undefined),
    detailPending: ref(false),
    filters: reactive({ organizationId: 'org', environmentId: 'dev', workOrderId: 'WO-1' }),
    finishedGoodsReceiptRequests: ref([]),
    materialIssueRequests: ref([]),
    materialReadiness: ref({ items: [], readinessStatus: 'Ready', blockingReasons: [] }),
    materialReadinessError: ref(undefined),
    materialReadinessPending: ref(false),
    refreshDetail: vi.fn(),
    refreshMaterialReadiness: vi.fn(),
    retryCancelPreview: vi.fn(),
  }),
  useMesWorkOrders: () => ({
    createRushWorkOrder: vi.fn(),
    createRushWorkOrderError: ref(undefined),
    createRushWorkOrderPending: ref(false),
    filters: reactive({ organizationId: 'org', environmentId: 'dev', status: undefined, skip: 0, take: 20 }),
    recordProductionReport: vi.fn(),
    recordProductionReportError: ref(undefined),
    recordProductionReportPending: ref(false),
    refreshWorkOrders: vi.fn(),
    workOrders: ref(detailState.workOrders),
    workOrdersError: ref(undefined),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(detailState.workOrders.length),
  }),
}))

function patchPermissions(codes: string[]) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'u1',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'op',
      permissionCodes: codes,
    },
  })
  return pinia
}

const holdPanelStub = {
  props: ['sourceService', 'sourceDocumentId', 'scope', 'isActive', 'canManage'],
  template:
    '<div data-testid="hold-panel" :data-source="sourceDocumentId" :data-active="String(isActive)" :data-manage="String(canManage)" />',
}

describe('work-order detail — quality hold block', () => {
  beforeEach(() => {
    detailState.qualityHolds = []
  })

  function mountDetail(codes: string[]) {
    return mount(WorkOrderDetailPage, {
      global: {
        plugins: [patchPermissions(codes)],
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          QualityHoldPanel: holdPanelStub,
          NvPageHeader: { template: '<header><slot name="actions" /></header>' },
          NvSectionCards: { template: '<div><slot /></div>' },
          NvSectionCard: { template: '<div />' },
          NvDataTable: { props: ['rows'], template: '<div />' },
          NvButton: { template: '<button><slot /></button>' },
          NvStatusBadge: { props: ['label', 'value'], template: '<span>{{ label ?? value }}</span>' },
          NvTooltip: { template: '<div><slot /></div>' },
          NvTooltipProvider: { template: '<div><slot /></div>' },
          NvTooltipTrigger: { template: '<div><slot /></div>' },
          NvTooltipContent: { template: '<div><slot /></div>' },
          NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
        },
      },
    })
  }

  it('renders a hold panel per quality hold', () => {
    detailState.qualityHolds = [
      { sourceService: 'business-mes', sourceDocumentId: 'WO-1', scope: 'work-order', isActive: true },
      {
        sourceService: 'business-mes',
        sourceDocumentId: 'WO-1-OP-20',
        scope: 'operation-task',
        isActive: true,
      },
    ]
    const wrapper = mountDetail(['business.mes.work-orders.read'])
    const panels = wrapper.findAll('[data-testid="hold-panel"]')
    expect(panels).toHaveLength(2)
    expect(panels.map((p) => p.attributes('data-source'))).toEqual(['WO-1', 'WO-1-OP-20'])
  })

  it('keeps rendering a released hold so its release audit + timeline stay visible', () => {
    detailState.qualityHolds = [
      { sourceService: 'business-mes', sourceDocumentId: 'WO-1', scope: 'work-order', isActive: false },
    ]
    const wrapper = mountDetail(['business.mes.work-orders.read'])
    const panel = wrapper.get('[data-testid="hold-panel"]')
    expect(panel.attributes('data-active')).toBe('false')
    // 已释放但仍渲染面板——满足「hold 自动消失（列表锁定）且时间线完整（详情可见）」。
    expect(wrapper.text()).not.toContain('需处理后才能放行或开工')
  })

  it('grants force-release only with quality write permission', () => {
    detailState.qualityHolds = [
      { sourceService: 'business-mes', sourceDocumentId: 'WO-1', scope: 'work-order', isActive: true },
    ]
    const readOnly = mountDetail(['business.mes.work-orders.read'])
    expect(readOnly.get('[data-testid="hold-panel"]').attributes('data-manage')).toBe('false')

    const manager = mountDetail(['business.mes.work-orders.read', 'business.mes.quality.write'])
    expect(manager.get('[data-testid="hold-panel"]').attributes('data-manage')).toBe('true')
  })

  it('renders no hold block when there are no active holds', () => {
    const wrapper = mountDetail(['business.mes.work-orders.read'])
    expect(wrapper.find('[data-testid="hold-panel"]').exists()).toBe(false)
  })
})

describe('work-order list — quality hold lock icon', () => {
  beforeEach(() => {
    detailState.workOrders = []
  })

  function mountList() {
    return mount(WorkOrdersListPage, {
      global: {
        plugins: [patchPermissions(['business.mes.work-orders.read'])],
        stubs: {
          BusinessLayout: { template: '<main><slot /></main>' },
          NvPageHeader: { template: '<header><slot name="actions" /></header>' },
          NvToolbar: { template: '<div><slot name="filters" /></div>' },
          NvDataTable: {
            props: ['rows'],
            template:
              '<div><template v-for="(row, i) in rows" :key="i"><slot name="cell-status" :row="row" /></template></div>',
          },
          NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
          NvButton: { template: '<button><slot /></button>' },
          NvSelect: { template: '<div><slot /></div>' },
          NvSelectTrigger: { template: '<button><slot /></button>' },
          NvSelectContent: { template: '<div><slot /></div>' },
          NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
          SelectValue: { template: '<span />' },
          NvInput: { template: '<input />' },
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
  }

  it('shows a lock marker only for work orders with an active quality hold', () => {
    detailState.workOrders = [
      { workOrderId: 'WO-1', skuId: 'FG-1', status: 'released', operationTasks: [], hasActiveQualityHold: true },
      { workOrderId: 'WO-2', skuId: 'FG-2', status: 'released', operationTasks: [], hasActiveQualityHold: false },
    ]
    const wrapper = mountList()
    const locks = wrapper.findAll('[aria-label="存在有效质量保留"]')
    expect(locks).toHaveLength(1)
  })
})
