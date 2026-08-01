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
const UUID_PATTERN = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i
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
    resolveSkuLabel: (v?: string | null) =>
      v?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
        ? state.catalogResolved
          ? '轴承钢'
          : '未指定物料'
        : (v ?? '未指定物料'),
    resolveShiftLabel: (v?: string | null) =>
      v?.match(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)
        ? state.catalogResolved
          ? '早班'
          : undefined
        : (v ?? undefined),
    resolveWorkCenter: (v?: string | null) => v ?? undefined,
  }),
}))

const state = vi.hoisted(() => ({
  createMaterialIssueRequest: vi.fn(),
  confirmLineSideReceipt: vi.fn(),
  materialIssueRequests: [] as Record<string, unknown>[],
  baseUomBySku: new Map<string, string>(),
  skusPending: false,
  catalogResolved: true,
  detail: {} as Record<string, unknown>,
  materialReadiness: {} as Record<string, unknown>,
}))

// 单位取自物料主档（#1294 姿势）：主档缺单位就不许发起领料，绝不写死占位值。
vi.mock('@/composables/useSkuNames', () => ({
  useSkuNames: () => ({
    resolveBaseUom: (code?: string | null) =>
      code ? state.baseUomBySku.get(code.trim()) : undefined,
    resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
    resolveSkuName: (code?: string | null) => code ?? undefined,
    skusPending: ref(state.skusPending),
  }),
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
    detail: ref(state.detail),
    detailError: ref(undefined),
    detailPending: ref(false),
    filters: reactive({ organizationId: 'org', environmentId: 'dev', workOrderId: 'WO-1' }),
    finishedGoodsReceiptRequests: ref([]),
    materialIssueRequests: ref(state.materialIssueRequests),
    materialIssueRequestsPending: ref(false),
    materialReadiness: ref(state.materialReadiness),
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
        NvPageHeader: {
          props: ['title'],
          template: '<header><h1>{{ title }}</h1><slot name="actions" /></header>',
        },
        NvDataTable: {
          props: ['rows', 'columns'],
          template: `<div><div v-for="row in rows">
            <span v-for="column in columns">
              {{ column.key === 'wmsRequestId' ? '' : column.accessor ? column.accessor(row) : row[column.key] }}
            </span>
            <slot name="cell-wmsRequestId" :row="row" />
          </div></div>`,
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
        // reka 的 Dialog 子件要求真实 DialogRoot 上下文（门禁绿≠真机可用的老坑），
        // 打开弹窗的用例里整棵子树都要桩掉。
        NvAlertDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
        NvAlertDialogContent: { template: '<div><slot /></div>' },
        NvAlertDialogHeader: { template: '<div><slot /></div>' },
        NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
        NvAlertDialogDescription: { template: '<p><slot /></p>' },
        NvAlertDialogFooter: { template: '<div><slot /></div>' },
        NvFieldGroup: { template: '<div><slot /></div>' },
        NvField: { template: '<div><slot /></div>' },
        NvFieldLabel: { template: '<label><slot /></label>' },
        NvInput: { props: ['modelValue'], template: '<input />' },
        CarriedContextSummary: { template: '<div />' },
      },
    },
  })
}

describe('work-order detail — PC 领料入口 (#1324)', () => {
  beforeEach(() => {
    routeState.params.workOrderId = 'WO-1'
    state.createMaterialIssueRequest.mockReset()
    state.confirmLineSideReceipt.mockReset()
    state.materialIssueRequests.length = 0
    state.baseUomBySku.clear()
    state.baseUomBySku.set('MAT-OIL', 'L')
    state.skusPending = false
    state.catalogResolved = true
    state.detail = {
      workOrderId: 'WO-1',
      skuId: 'FG-1',
      quantity: 10,
      status: 'released',
      operationTasks: [],
      blockingReasons: [],
      qualityHolds: [],
    }
    state.materialReadiness = { items: [], readinessStatus: 'Ready', blockingReasons: [] }
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

  it('发起领料带上物料主档单位，而不是占位值 UNSPECIFIED', async () => {
    const wrapper = mountDetail(['business.mes.work-orders.read', 'business.mes.materials.manage'])
    await wrapper.find('[data-testid="open-material-issue"]').trigger('click')
    const vm = wrapper.vm as unknown as {
      issueForm: { materialId: string }
      submitIssue: () => Promise<void>
    }
    vm.issueForm.materialId = 'MAT-OIL'
    await wrapper.vm.$nextTick()
    await vm.submitIssue()

    expect(state.createMaterialIssueRequest).toHaveBeenCalledTimes(1)
    expect(state.createMaterialIssueRequest.mock.calls[0][0]).toMatchObject({
      materialId: 'MAT-OIL',
      uomCode: 'L',
    })
  })

  it('物料主档没有基本计量单位时阻断提交并给出中文原因', async () => {
    state.baseUomBySku.clear()
    const wrapper = mountDetail(['business.mes.work-orders.read', 'business.mes.materials.manage'])
    await wrapper.find('[data-testid="open-material-issue"]').trigger('click')
    const vm = wrapper.vm as unknown as {
      issueForm: { materialId: string }
      canSubmitIssue: boolean
      submitIssue: () => Promise<void>
    }
    vm.issueForm.materialId = 'MAT-OIL'
    await wrapper.vm.$nextTick()

    expect(vm.canSubmitIssue).toBe(false)
    expect(wrapper.find('[data-testid="issue-uom-blocked"]').text()).toContain(
      '物料主档没有基本计量单位',
    )
    await vm.submitIssue()
    expect(state.createMaterialIssueRequest).not.toHaveBeenCalled()
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

  it('read-face guard：目录可解析时展示工序、工作中心、设备与业务单号', () => {
    routeState.params.workOrderId = 'WO-2026-08001'
    state.detail = {
      workOrderId: 'WO-2026-08001',
      skuId: 'FG-1',
      quantity: 10,
      status: 'released',
      blockingReasons: [],
      qualityHolds: [],
      operationTasks: [
        {
          operationTaskId: '019fbb41-1111-7111-8111-111111111111',
          operationTaskNo: 'OP-08001-10',
          workCenterId: '019fbb41-2222-7222-8222-222222222222',
          workCenterName: '精加工一线',
          deviceAssetId: '019fbb41-3333-7333-8333-333333333333',
          deviceAssetName: '五轴加工中心',
          shiftId: '019fbb41-4444-7444-8444-444444444444',
        },
      ],
    }
    state.materialReadiness = {
      items: [
        {
          materialId: '019fbb41-5555-7555-8555-555555555555',
          materialLotId: 'LOT-20260801-A',
          requiredQuantity: 2,
          availableQuantity: 2,
          stagedQuantity: 2,
          shortageQuantity: 0,
        },
      ],
      readinessStatus: 'Ready',
      blockingReasons: [],
    }
    state.materialIssueRequests.push({
      requestId: 'MIR-20260801-01',
      materialId: 'MAT-OIL',
      requestedQuantity: 1,
      receivedQuantity: 0,
      wmsRequestId: 'MI-MIR-20260801-01',
    })

    const visibleText = mountDetail([
      'business.mes.work-orders.read',
      'business.mes.materials.manage',
    ]).text()
    expect(visibleText).toContain('OP-08001-10')
    expect(visibleText).toContain('精加工一线')
    expect(visibleText).toContain('五轴加工中心')
    expect(visibleText).toContain('轴承钢')
    expect(visibleText).toContain('MI-MIR-20260801-01')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  it('read-face guard：目录失败与人读字段缺失时显示占位符且不泄露 ID', () => {
    const technicalId = '019fbb41-aaaa-7aaa-8aaa-aaaaaaaaaaaa'
    routeState.params.workOrderId = technicalId
    state.catalogResolved = false
    state.detail = {
      workOrderId: technicalId,
      skuId: 'FG-1',
      quantity: 10,
      status: 'released',
      blockingReasons: [],
      qualityHolds: [],
      operationTasks: [
        {
          operationTaskId: technicalId,
          workCenterId: '019fbb41-bbbb-7bbb-8bbb-bbbbbbbbbbbb',
          deviceAssetId: '019fbb41-cccc-7ccc-8ccc-cccccccccccc',
          shiftId: '019fbb41-dddd-7ddd-8ddd-dddddddddddd',
        },
      ],
    }
    state.materialReadiness = {
      items: [{ materialId: technicalId, materialLotId: technicalId, shortageQuantity: 0 }],
      readinessStatus: 'Ready',
      blockingReasons: [],
    }
    state.materialIssueRequests.push({
      requestId: technicalId,
      materialId: 'MAT-OIL',
      wmsRequestId: technicalId,
    })

    const visibleText = mountDetail([
      'business.mes.work-orders.read',
      'business.mes.materials.manage',
    ]).text()
    expect(visibleText).toContain('—')
    expect(visibleText).toContain('未指定')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })
})
