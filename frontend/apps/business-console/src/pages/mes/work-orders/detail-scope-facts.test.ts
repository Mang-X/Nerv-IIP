import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrderDetailPage from './[workOrderId].vue'

/**
 * #1288 工单详情页拒载时的事实口径。
 *
 * 作业范围未就绪时详情/齐套查询根本没发（enabled=false），detail 与 materialReadiness
 * 都是 undefined。此前页面照样渲染「无阻塞」「已齐套」「用料已备齐，可按工序顺序开工」
 * ——在整页拒载的同时反显安全假文案。本文件守住：没取到读面就不下安全结论。
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

const detailState = vi.hoisted(() => ({
  detail: undefined as Record<string, unknown> | undefined,
  materialReadiness: undefined as Record<string, unknown> | undefined,
  scopeMessage: '',
}))

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
  describeMesReadinessReason: (code: string) => ({ code, label: code, nextStep: '' }),
  useMesWorkScopeSelection: () => ({
    scopeOptions: ref([]),
    scopeSelectionValue: ref(undefined),
    scopeReady: ref(false),
    scopeMessage: ref(''),
    scopePending: ref(false),
    scopeUnavailable: ref(false),
    selectedScope: ref(undefined),
    principalIdentity: ref('principal-test'),
    requireSelectedScope: vi.fn(),
  }),
  useMesWorkOrderDetail: () => ({
    activateCancelPreview: vi.fn(),
    cancelPreviewError: ref(undefined),
    cancelPreviewPending: ref(false),
    cancelPreviewReady: ref(false),
    cancelWorkOrder: vi.fn(),
    cancelWorkOrderPending: ref(false),
    detail: ref(detailState.detail),
    detailError: ref(undefined),
    detailPending: ref(false),
    filters: reactive({ organizationId: 'org', environmentId: 'dev', workOrderId: 'WO-1' }),
    finishedGoodsReceiptRequests: ref([]),
    materialIssueRequests: ref([]),
    materialReadiness: ref(detailState.materialReadiness),
    materialReadinessError: ref(undefined),
    materialReadinessPending: ref(false),
    refreshDetail: vi.fn(),
    refreshMaterialReadiness: vi.fn(),
    retryCancelPreview: vi.fn(),
    workOrderManageScopeMessage: ref(detailState.scopeMessage),
    workOrderManageScopeReady: ref(!detailState.scopeMessage),
    workOrderReadScopeMessage: ref(detailState.scopeMessage),
  }),
}))

function mountDetail() {
  const pinia = createPinia()
  const auth = useAuthStore(pinia)
  auth.$patch({
    principal: {
      principalId: 'u1',
      principalType: 'user',
      organizationId: 'org',
      environmentId: 'dev',
      loginName: 'op',
      permissionCodes: ['business.mes.work-orders.read'],
    },
  })
  return mount(WorkOrderDetailPage, {
    global: {
      plugins: [pinia],
      stubs: {
        BusinessLayout: { template: '<main><slot /></main>' },
        QualityHoldPanel: { template: '<div />' },
        NvPageHeader: { template: '<header><slot name="actions" /></header>' },
        NvDataTable: { props: ['rows'], template: '<div />' },
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

describe('work-order detail — 拒载时不反显安全假文案 (#1288)', () => {
  beforeEach(() => {
    detailState.detail = undefined
    detailState.materialReadiness = undefined
    detailState.scopeMessage = ''
  })

  it('作业范围未就绪整页拒载时，不渲染「无阻塞 / 已齐套 / 用料已备齐」', () => {
    detailState.scopeMessage = '尚未选择已授权作业范围，当前操作已禁用。'
    const wrapper = mountDetail()

    expect(wrapper.find('[data-testid="work-order-read-scope-message"]').text()).toContain(
      '尚未选择已授权作业范围',
    )
    expect(wrapper.text()).not.toContain('无阻塞')
    expect(wrapper.text()).not.toContain('已齐套')
    expect(wrapper.text()).not.toContain('用料已备齐')
    expect(wrapper.text()).toContain('结论未取得')
  })

  it('详情与齐套读面都取到且确实无阻塞时才渲染安全结论', () => {
    detailState.detail = {
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }
    detailState.materialReadiness = { items: [], readinessStatus: 'Ready', blockingReasons: [] }
    const wrapper = mountDetail()

    expect(wrapper.text()).toContain('无阻塞')
    expect(wrapper.text()).toContain('已齐套')
    expect(wrapper.text()).not.toContain('结论未取得')
  })
})
