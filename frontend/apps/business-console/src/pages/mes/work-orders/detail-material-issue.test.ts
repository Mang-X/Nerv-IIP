import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrderDetailPage from './[workOrderId].vue'

/**
 * #1324 PC 端领料链路入口。
 *
 * 齐套页的「下一步动作」以前指向一个 PC 上并不存在的动作（只有 PDA 能发起领料/收料）。
 * 本文件守住：有领料管理权限时 PC 能真发起领料与线边收料；WMS 未回写出库单时说「仓库尚未接单」，
 * 不用空白冒充已发料。
 */

const routeState = vi.hoisted(() => ({
  params: { workOrderId: 'WO-1' },
  query: {} as Record<string, string>,
}))
vi.mock('vue-router', () => ({
  useRoute: () => routeState,
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/composables/useScheduleInvalidation', () => ({
  resolveScheduleStatus: () => ({ key: 'scheduled', label: '已排程', tone: 'info' }),
  scheduleInvalidationHint: () => '',
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSku: (v?: string | null) => v ?? '无',
    resolveSkuLabel: (v?: string | null) => v ?? '未指定物料',
    resolveShiftLabel: (v?: string | null) => v ?? undefined,
    resolveWorkCenter: (v?: string | null) => v ?? undefined,
  }),
}))

const state = vi.hoisted(() => ({
  createMaterialIssueRequest: vi.fn(),
  confirmLineSideReceipt: vi.fn(),
  materialIssueRequests: [] as Record<string, unknown>[],
}))

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
  describeMesReadinessReason: (code: string) => ({ code, label: code, nextStep: '' }),
  useMesWorkScopeSelection: () => ({
    scopeOptions: ref([]),
    scopeSelectionValue: ref(undefined),
    scopeReady: ref(true),
    scopeMessage: ref(''),
    scopePending: ref(false),
    scopeUnavailable: ref(false),
    selectedScope: ref({ kind: 'work-center', id: 'WC-1' }),
    principalIdentity: ref('principal-test'),
    requireSelectedScope: vi.fn(),
  }),
  useMesWorkOrderDetail: () => ({
    activateCancelPreview: vi.fn(),
    cancelPreviewError: ref(undefined),
    cancelPreviewPending: ref(false),
    cancelPreviewReady: ref(true),
    cancelWorkOrder: vi.fn(),
    cancelWorkOrderPending: ref(false),
    confirmLineSideReceipt: state.confirmLineSideReceipt,
    confirmLineSideReceiptPending: ref(false),
    createMaterialIssueRequest: state.createMaterialIssueRequest,
    createMaterialIssueRequestPending: ref(false),
    detail: ref({
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }),
    detailError: ref(undefined),
    detailPending: ref(false),
    filters: reactive({ organizationId: 'org', environmentId: 'dev', workOrderId: 'WO-1' }),
    finishedGoodsReceiptRequests: ref([]),
    materialIssueRequests: ref(state.materialIssueRequests),
    materialIssueRequestsPending: ref(false),
    materialReadiness: ref({ items: [], readinessStatus: 'Ready', blockingReasons: [] }),
    materialReadinessError: ref(undefined),
    materialReadinessPending: ref(false),
    refreshDetail: vi.fn(),
    refreshMaterialIssueRequests: vi.fn(),
    refreshMaterialReadiness: vi.fn(),
    retryCancelPreview: vi.fn(),
    workOrderManageScopeMessage: ref(''),
    workOrderManageScopeReady: ref(true),
    workOrderReadScopeMessage: ref(''),
  }),
}))

function mountDetail(permissionCodes: string[]) {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'u1',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'op',
      permissionCodes,
    },
  })
  return mount(WorkOrderDetailPage, {
    global: {
      plugins: [pinia],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        QualityHoldPanel: { template: '<div />' },
        NvPageHeader: { template: '<header><slot name="actions" /></header>' },
        NvDataTable: {
          props: ['rows'],
          template: '<div><slot name="cell-wmsRequestId" v-for="row in rows" :row="row" /></div>',
        },
        NvButton: { template: '<button><slot /></button>' },
        NvStatusBadge: {
          props: ['label', 'value'],
          template: '<span>{{ label ?? value }}</span>',
        },
        NvTooltip: { template: '<div><slot /></div>' },
        NvTooltipProvider: { template: '<div><slot /></div>' },
        NvTooltipTrigger: { template: '<div><slot /></div>' },
        NvTooltipContent: { template: '<div><slot /></div>' },
        NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
      },
    },
  })
}

describe('work-order detail — PC 领料入口 (#1324)', () => {
  beforeEach(() => {
    state.createMaterialIssueRequest.mockReset()
    state.confirmLineSideReceipt.mockReset()
    state.materialIssueRequests.length = 0
  })

  it('有领料管理权限时渲染「发起领料」入口', () => {
    const wrapper = mountDetail(['business.mes.work-orders.read', 'business.mes.materials.manage'])

    expect(wrapper.find('[data-testid="open-material-issue"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('领料与收料')
  })

  it('无领料管理权限时不渲染发起领料入口', () => {
    const wrapper = mountDetail(['business.mes.work-orders.read'])

    expect(wrapper.find('[data-testid="open-material-issue"]').exists()).toBe(false)
  })

  it('WMS 未回写出库单时明说「仓库尚未接单」，不用空白冒充已发料', () => {
    state.materialIssueRequests.push({
      requestId: 'MIR-001',
      workOrderId: 'WO-1',
      materialId: 'MAT-OIL',
      requestedQuantity: 7,
      receivedQuantity: 0,
      status: 'Requested',
      wmsRequestId: null,
    })
    const wrapper = mountDetail(['business.mes.work-orders.read', 'business.mes.materials.manage'])

    expect(wrapper.text()).toContain('仓库尚未接单')
  })

  it('WMS 回写出库单后展示出库单号', () => {
    state.materialIssueRequests.push({
      requestId: 'MIR-001',
      workOrderId: 'WO-1',
      materialId: 'MAT-OIL',
      requestedQuantity: 7,
      receivedQuantity: 0,
      status: 'Requested',
      wmsRequestId: 'MI-MIR-001',
    })
    const wrapper = mountDetail(['business.mes.work-orders.read', 'business.mes.materials.manage'])

    expect(wrapper.text()).toContain('MI-MIR-001')
    expect(wrapper.text()).not.toContain('仓库尚未接单')
  })
})
