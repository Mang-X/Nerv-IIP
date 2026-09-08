import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrderDetailPage from './[workOrderId].vue'

/**
 * #2707 工序任务空态复用 scheduleDisabledReason：禁用原因只有一处真实来源（同一份
 * computed 同时喂 tooltip 与空态），不允许空态文案漏掉「没有生产版本」这一支、或在
 * 按钮实际可用时仍说「不可排产」。四支必须各自可达且互不覆盖。
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
    materialReadiness: ref({ items: [], readinessStatus: 'Ready', blockingReasons: [] }),
    materialReadinessError: ref(undefined),
    materialReadinessPending: ref(false),
    refreshDetail: vi.fn(),
    refreshMaterialReadiness: vi.fn(),
    retryCancelPreview: vi.fn(),
    workOrderManageScope: ref(undefined),
    workOrderManageScopeMessage: ref(''),
    workOrderManageScopeReady: ref(true),
    workOrderReadScope: ref(undefined),
    workOrderReadScopeMessage: ref(''),
  }),
  useMesWorkOrderTransformations: () => ({
    splitWorkOrder: vi.fn(),
    mergeWorkOrders: vi.fn(),
    readTransformation: vi.fn(),
    splitWorkOrderPending: ref(false),
    mergeWorkOrdersPending: ref(false),
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
        // 只有本 stub 忠实复现空态渲染：rows 为空时吐出 emptyMessage，否则不吐——
        // 用来钉住「哪张表用了哪句 emptyMessage」，不是钉 NvDataTable 自己的实现。
        NvDataTable: {
          props: ['rows', 'emptyMessage'],
          template: '<div>{{ rows.length === 0 ? emptyMessage : "" }}</div>',
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

const READ = 'business.mes.work-orders.read'
const SCHEDULE_MANAGE = 'business.scheduling.plans.manage'

describe('work-order detail — 工序任务空态复用 scheduleDisabledReason (#2707)', () => {
  beforeEach(() => {
    detailState.detail = undefined
  })

  it('终态且有生产版本：空态说终态，不说没有生产版本', () => {
    detailState.detail = {
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      productionVersionId: 'PV-1',
      quantity: 10,
      status: 'closed',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }
    const wrapper = mountDetail([READ, SCHEDULE_MANAGE])
    expect(wrapper.text()).toContain('暂无工序任务。工单已处于终态，不能再排产。')
    expect(wrapper.text()).not.toContain('暂无工序任务。工单没有生产版本')
  })

  it('非终态但没有生产版本：空态说没有生产版本，不说终态', () => {
    detailState.detail = {
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      productionVersionId: null,
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }
    const wrapper = mountDetail([READ, SCHEDULE_MANAGE])
    expect(wrapper.text()).toContain('暂无工序任务。工单没有生产版本，排程无法展开工艺路线。')
    expect(wrapper.text()).not.toContain('暂无工序任务。工单已处于终态')
  })

  it('可排产但缺排产权限：空态说明缺权限，不说终态/无生产版本', () => {
    detailState.detail = {
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      productionVersionId: 'PV-1',
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }
    const wrapper = mountDetail([READ])
    expect(wrapper.text()).toContain('暂无工序任务。当前账号没有排产管理权限')
    expect(wrapper.text()).not.toContain('暂无工序任务。工单已处于终态')
    expect(wrapper.text()).not.toContain('暂无工序任务。工单没有生产版本')
  })

  it('可排产且有权限：空态引导点击按钮，不出现任何禁用原因', () => {
    detailState.detail = {
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      productionVersionId: 'PV-1',
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }
    const wrapper = mountDetail([READ, SCHEDULE_MANAGE])
    expect(wrapper.text()).toContain('暂无工序任务。点击上方「对该单排产」生成方案')
    expect(wrapper.text()).not.toContain('暂无工序任务。工单已处于终态')
    expect(wrapper.text()).not.toContain('暂无工序任务。工单没有生产版本')
    expect(wrapper.text()).not.toContain('暂无工序任务。当前账号没有排产管理权限')
  })
})
