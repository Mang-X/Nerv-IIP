import { describeMesReadinessReasons as describeReadinessReasons } from '@nerv-iip/business-core'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import { formatDateTime } from '@/utils/format'
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
  RouterLink: { props: ['to'], template: '<a :data-to="to"><slot /></a>' },
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
  describeMesReadinessReasons: (reasons: string[]) => describeReadinessReasons(reasons),
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
    materialIssueRequestsError: ref(undefined),
    materialReadiness: ref(detailState.materialReadiness),
    materialReadinessError: ref(undefined),
    materialReadinessPending: ref(false),
    refreshDetail: vi.fn(),
    refreshMaterialReadiness: vi.fn(),
    retryCancelPreview: vi.fn(),
    workOrderManageScope: ref(undefined),
    workOrderManageScopeMessage: ref(detailState.scopeMessage),
    workOrderManageScopeReady: ref(!detailState.scopeMessage),
    workOrderReadScope: ref(undefined),
    workOrderReadScopeMessage: ref(detailState.scopeMessage),
  }),
  useMesWorkOrderTransformations: () => ({
    splitWorkOrder: vi.fn(),
    mergeWorkOrders: vi.fn(),
    readTransformation: vi.fn(),
    splitWorkOrderPending: ref(false),
    mergeWorkOrdersPending: ref(false),
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
        NvDataTable: {
          props: ['rows'],
          template:
            '<div><slot v-for="row in rows" name="cell-operationTaskId" :row="row" /></div>',
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

describe('work-order detail — 拒载时不反显安全假文案 (#1288)', () => {
  beforeEach(() => {
    routeState.query = {}
    detailState.detail = undefined
    detailState.materialReadiness = undefined
    detailState.scopeMessage = ''
  })

  it('安灯来源链接在工单详情标识同一来源工序，不把其它工序标成来源', () => {
    routeState.query = { operationTaskId: 'task-20' }
    detailState.detail = {
      workOrderId: 'WO-1',
      operationTasks: [
        { operationTaskId: 'task-10', operationTaskNo: 'WO-1-OP-10' },
        { operationTaskId: 'task-20', operationTaskNo: 'WO-1-OP-20' },
      ],
    }
    const wrapper = mountDetail()
    expect(wrapper.findAll('[data-andon-source="true"]')).toHaveLength(1)
    expect(wrapper.get('[data-andon-source="true"]').text()).toContain('WO-1-OP-20')
    expect(wrapper.get('[data-andon-source="true"]').text()).toContain('来源工序')
    wrapper.unmount()
  })

  it('追溯来源在详情提供限定到追溯页的返回入口，保留原查询', () => {
    const traceReturn = '/mes/traceability?mode=batch&batchOrSerial=SER-001&workOrderId=WO-QUERY'
    routeState.query = {
      traceReturn,
      traceOrganizationId: 'org',
      traceEnvironmentId: 'dev',
      operationTaskId: 'task-20',
    }
    detailState.detail = {
      workOrderId: 'WO-1',
      operationTasks: [{ operationTaskId: 'task-20', operationTaskNo: 'WO-1-OP-20' }],
    }
    const wrapper = mountDetail()
    const back = wrapper.findAll('a').find((link) => link.text() === '返回追溯')!
    expect(back.attributes('data-to')).toBe(traceReturn)
    expect(wrapper.text()).toContain('来源工序')
    expect(wrapper.text()).not.toContain('与追溯来源不一致')
    wrapper.unmount()
  })

  it.each(['https://example.com/', '/mes/work-orders/WO-OTHER', '//example.com/mes/traceability'])(
    '不把任意返回地址 %s 变成导航入口',
    (traceReturn) => {
      routeState.query = { traceReturn }
      const wrapper = mountDetail()
      expect(wrapper.text()).not.toContain('返回追溯')
      wrapper.unmount()
    },
  )

  it.each([
    {
      workOrderId: 'WO-OTHER',
      operationTasks: [{ operationTaskId: 'task-20', operationTaskNo: 'WO-OTHER-OP-20' }],
      hiddenOperationTaskNo: 'WO-OTHER-OP-20',
      expected: '工单详情与追溯来源不一致',
    },
    {
      workOrderId: 'WO-1',
      operationTasks: [{ operationTaskId: 'task-10', operationTaskNo: 'WO-1-OP-10' }],
      hiddenOperationTaskNo: 'WO-1-OP-10',
      expected: '工单详情中未找到来源工序',
    },
  ])(
    '公开详情与来源不一致时解释并停止展示错误对象：$expected',
    ({ expected, hiddenOperationTaskNo, ...detail }) => {
      routeState.query = {
        traceReturn: '/mes/traceability?mode=batch&batchOrSerial=SER-001',
        traceOrganizationId: 'org',
        traceEnvironmentId: 'dev',
        operationTaskId: 'task-20',
      }
      detailState.detail = detail
      const wrapper = mountDetail()
      expect(wrapper.get('[role="alert"][data-testid="trace-source-message"]').text()).toContain(
        expected,
      )
      expect(wrapper.findAll('[data-andon-source="true"]')).toHaveLength(0)
      expect(wrapper.text()).not.toContain(hiddenOperationTaskNo)
      expect(wrapper.text()).toContain('返回追溯')
      wrapper.unmount()
    },
  )

  it('组织环境已变时解释上下文不一致，不把另一范围的详情冒称来源对象', () => {
    routeState.query = {
      traceReturn: '/mes/traceability?workOrderId=WO-1',
      traceOrganizationId: 'other-org',
      traceEnvironmentId: 'dev',
    }
    detailState.detail = {
      workOrderId: 'WO-1',
      operationTasks: [{ operationTaskId: 'task-10', operationTaskNo: 'WO-1-OP-10' }],
    }
    const wrapper = mountDetail()
    expect(wrapper.get('[data-testid="trace-source-message"]').text()).toContain(
      '当前查看范围已改变',
    )
    expect(wrapper.text()).not.toContain('WO-1-OP-10')
    expect(wrapper.text()).toContain('返回追溯')
    wrapper.unmount()
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

  it('缺料时展示冻结快照的真实捕获时间和领料、线边收料路径', () => {
    const capturedAtUtc = '2026-09-22T08:15:00Z'
    detailState.detail = { workOrderId: 'WO-1', operationTasks: [], blockingReasons: [] }
    detailState.materialReadiness = {
      readinessStatus: 'Blocked',
      snapshotCapturedAtUtc: capturedAtUtc,
      items: [
        {
          materialId: 'PK-BOX-01',
          requiredQuantity: 5.05,
          availableQuantity: 0,
          shortageQuantity: 5.05,
        },
      ],
    }
    const wrapper = mountDetail()

    expect(wrapper.get('[data-testid="material-readiness-snapshot"]').text()).toContain(
      formatDateTime(capturedAtUtc),
    )
    expect(wrapper.get('[data-testid="material-readiness-scope"]').text()).toContain(
      '原料仓补库存不会直接改变下达结论',
    )
    expect(wrapper.text()).toContain('发起领料')
    expect(wrapper.text()).toContain('确认收料')
    expect(wrapper.text()).not.toContain('已齐套')
    wrapper.unmount()
  })

  it('快照时间未记录时不补造钟点', () => {
    detailState.detail = { workOrderId: 'WO-1', operationTasks: [], blockingReasons: [] }
    detailState.materialReadiness = {
      readinessStatus: 'Blocked',
      snapshotCapturedAtUtc: null,
      blockingReasons: ['MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照'],
      items: [],
    }
    const wrapper = mountDetail()

    expect(wrapper.get('[data-testid="material-readiness-snapshot"]').text()).toBe(
      '齐套快照捕获时间：未记录',
    )
    expect(wrapper.text()).toContain('工单缺少齐套需求快照')
    expect(wrapper.text()).not.toContain('已齐套')
    expect(wrapper.text()).not.toContain('MATERIAL_REQUIREMENT_SNAPSHOT_MISSING')
    wrapper.unmount()
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
