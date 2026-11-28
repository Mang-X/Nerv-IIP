import type { BusinessConsoleWmsInboundOrderItem } from '@nerv-iip/api-client'
import { vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/master-data/useSkuNames', async () => {
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
vi.mock('@/composables/master-data/useBusinessPartnerNames', async () => {
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
vi.mock('@/composables/master-data/useMasterDataDisplayNames', async () => {
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
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const wms = vi.hoisted(() => ({
  createIdempotencyKey: vi.fn(() => 'wms-intent-1'),
  retryInboundPosting: vi.fn(),
  retryOutboundPosting: vi.fn(),
  outboundStatus: 'open',
  outboundPoolCode: 'POOL-01' as string | null,
  completeInbound: vi.fn(),
  confirmReceipt: vi.fn(),
  purchaseReceipt: undefined as BusinessConsoleWmsInboundOrderItem['purchaseReceipt'],
  completeOutbound: vi.fn(),
  completeCountExecution: vi.fn(),
  verifyCountExecution: vi.fn(),
  countRows: undefined as unknown[] | undefined,
  createCountExecution: vi.fn(),
  failWcs: vi.fn(),
  dispatchWcs: vi.fn(),
  completeWcs: vi.fn(),
  wcsRows: undefined as unknown[] | undefined,
  createInbound: vi.fn(),
  createOutbound: vi.fn(),
  inventoryContext: undefined as unknown,
  receivingQualityGates: [] as unknown[],
  supplierReturns: [] as unknown[],
  qualityGateStatus: undefined as string | undefined,
  isReleasedForPutaway: true,
  inboundStatus: 'open',
  permissionCodes: [
    'business.wms.receipts.read',
    'business.wms.receipts.manage',
    'business.quality.inspection-records.read',
  ] as string[],
  refreshReceivingQuality: vi.fn(),
  refreshInboundOrders: vi.fn(async () => undefined),
  refreshOutboundOrders: vi.fn(async () => undefined),
  refreshCountExecutions: vi.fn(async () => undefined),
}))
const inboundVersion = shallowRef(3)
const candidateState = vi.hoisted(() => ({ refresh: vi.fn(async () => undefined) }))
const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() },
}))

vi.mock('vue-router', () => ({
  useRouter: () => ({ replace: vi.fn() }),
  useRoute: () => ({ query: {} }),
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routeGuardState.guard = guard
  }),
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
}))

// 物料目录与默认工厂来自主数据 facade（需要 pinia 上下文），页面测试里给确定的目录即可。
vi.mock('@/composables/inventory/useInventoryScope', async () => {
  const { computed, ref } = await import('vue')
  const catalog = {
    siteOptions: computed(() => [{ value: 'SITE-001', label: '上海工厂' }]),
    sitesPending: ref(false),
    skuOptions: computed(() => [{ value: 'SKU-001', label: '前减振器总成', hint: 'pcs' }]),
    skusPending: ref(false),
    // 收货行的单位随所选物料的基本单位带出（不再手输）。
    resolveUomCode: () => 'pcs',
  }
  return {
    useInventoryScopeCatalog: () => catalog,
    useInventoryScopeDefaults: () => catalog,
  }
})

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: { principalId: 'emp049', permissionCodes: wms.permissionCodes },
  }),
}))

vi.mock('@/composables/wms/useWmsWorkScope', () => import('@/test/wmsWorkScopePage'))

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  confirmBusinessConsoleWmsInboundReceipt: wms.confirmReceipt,
  retryBusinessConsoleWmsInboundInventoryPosting: wms.retryInboundPosting,
  retryBusinessConsoleWmsOutboundInventoryPosting: wms.retryOutboundPosting,
}))

vi.mock('@/composables/erp/useErpProcurement', () => ({
  useErpPurchaseOrders: () => ({
    filters: reactive({}),
    total: shallowRef(1),
    items: shallowRef([
      {
        purchaseOrderNo: 'PO-1',
        siteCode: 'SITE-001',
        lines: [{ lineNo: '30', skuCode: 'SKU-001', uomCode: 'box', openQuantity: 5 }],
      },
    ]),
    pending: shallowRef(false),
  }),
}))

vi.mock('@/composables/wms', () => ({
  useWmsPickingTasks: () => ({
    filters: reactive({}),
    pickingTasks: shallowRef([]),
    pickingTasksPending: shallowRef(false),
    pickingTasksError: shallowRef(undefined),
    pickingTasksTotal: shallowRef(0),
  }),
  createWmsIdempotencyKey: wms.createIdempotencyKey,
  useWmsInboundOrders: vi.fn(() => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    inboundOrders: computed(() => [
      {
        inboundOrderId: 'ib-1',
        inboundOrderNo: 'IB-1',
        siteCode: 'SITE-001',
        status: wms.inboundStatus,
        version: inboundVersion.value,
        purchaseReceipt: wms.purchaseReceipt,
        createdAtUtc: '2026-06-01T00:00:00Z',
        qualityGateStatus: wms.qualityGateStatus,
        isReleasedForPutaway: wms.isReleasedForPutaway,
      },
    ]),
    inventoryContext: computed(() => wms.inventoryContext),
    inboundOrdersError: shallowRef(undefined),
    inboundOrdersPending: shallowRef(false),
    inboundOrdersTotal: computed(() => 1),
    refreshInboundOrders: wms.refreshInboundOrders,
    completeInbound: wms.completeInbound,
    completeInboundPending: shallowRef(false),
    completeInboundError: shallowRef(undefined),
    createInbound: wms.createInbound,
    createInboundPending: shallowRef(false),
    createInboundError: shallowRef(undefined),
    receivingQualityGates: computed(() => wms.receivingQualityGates),
    receivingQualityGatesPending: shallowRef(false),
    receivingQualityGatesError: shallowRef(undefined),
    supplierReturns: computed(() => wms.supplierReturns),
    supplierReturnsPending: shallowRef(false),
    supplierReturnsError: shallowRef(undefined),
    refreshReceivingQuality: wms.refreshReceivingQuality,
  })),
  useWmsOutboundOrders: vi.fn(() => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    outboundOrders: computed(() => [
      {
        outboundOrderId: 'ob-1',
        outboundOrderNo: 'OB-1',
        status: wms.outboundStatus,
        assignedPoolCode: wms.outboundPoolCode,
        createdAtUtc: '2026-06-01T00:00:00Z',
      },
    ]),
    outboundOrdersError: shallowRef(undefined),
    outboundOrdersPending: shallowRef(false),
    outboundOrdersTotal: computed(() => 1),
    refreshOutboundOrders: wms.refreshOutboundOrders,
    completeOutbound: wms.completeOutbound,
    completeOutboundPending: shallowRef(false),
    completeOutboundError: shallowRef(undefined),
    createOutbound: wms.createOutbound,
    createOutboundPending: shallowRef(false),
    createOutboundError: shallowRef(undefined),
  })),
  useWmsCountExecutions: vi.fn(() => ({
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      locationCode: undefined,
      status: undefined,
      skip: 0,
      take: 100,
    }),
    countExecutions: computed(
      () =>
        wms.countRows ?? [
          {
            countExecutionId: 'count-1',
            countNo: 'CNT-1',
            lotNo: 'LOT-COUNT-01',
            skuCode: 'SKU-001',
            uomCode: 'pcs',
            siteCode: 'SITE-001',
            locationCode: 'RACK-A-01',
            expectedQuantity: 7,
            status: 'open',
          },
        ],
    ),
    countExecutionsError: shallowRef(undefined),
    countExecutionsPending: shallowRef(false),
    countExecutionsTotal: computed(() => 1),
    refreshCountExecutions: wms.refreshCountExecutions,
    createCountExecution: wms.createCountExecution,
    createCountExecutionPending: shallowRef(false),
    completeCountExecution: wms.completeCountExecution,
    verifyCountExecution: wms.verifyCountExecution,
    completeCountExecutionPending: shallowRef(false),
  })),
  useWmsWcsTasks: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    wcsTasks: computed(
      () =>
        wms.wcsRows ?? [
          {
            wcsTaskId: 'w-1',
            externalTaskId: 'EXT-1',
            warehouseTaskId: 'WT-1',
            adapterType: 'agv',
            status: 'Failed',
            attemptCount: 1,
            warehouseTaskVersion: 7,
            plannedQuantity: 10,
            executedQuantity: 4,
          },
        ],
    ),
    wcsTasksError: shallowRef(undefined),
    wcsTasksPending: shallowRef(false),
    wcsTasksTotal: computed(() => wms.wcsRows?.length ?? 1),
    refreshWcsTasks: vi.fn(),
    dispatchWcs: wms.dispatchWcs,
    dispatchWcsPending: shallowRef(false),
    dispatchWcsError: shallowRef(undefined),
    failWcs: wms.failWcs,
    failWcsPending: shallowRef(false),
    failWcsError: shallowRef(undefined),
    completeWcs: wms.completeWcs,
    completeWcsPending: shallowRef(false),
    completeWcsError: shallowRef(undefined),
  }),
}))

/**
 * 库位/物料/工厂/来源类型等字段已从自由文本输入框改成只选控件。
 * 这些用例关心的是「填表 → 提交体」，不是选择器自身的交互，
 * 所以把选择器桩成输入位（透传 id 与 aria-label），让下面的 setInput 仍然表达「选中了某个候选」。
 */
const onlySelectStub = {
  props: ['modelValue', 'options', 'id', 'formSiteCode'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :data-form-site="formSiteCode" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const layoutStub = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvEntityPicker: onlySelectStub,
  NvSearchSelect: onlySelectStub,
  DirectoryPicker: onlySelectStub,
  DirectorySuggestInput: onlySelectStub,
}
vi.mock('@/composables/wms/useWmsOperationalCandidates', async () => {
  const { shallowRef } = await import('vue')
  return {
    useWmsOperationalCandidates: () => ({
      locationOptions: shallowRef([]),
      lotOptions: shallowRef([]),
      ready: shallowRef(true),
      searchKeyword: shallowRef(''),
      sourceLabel: shallowRef('当前范围仓储作业记录候选'),
      asOfUtc: shallowRef(),
      freshnessUtc: shallowRef(),
      truncated: shallowRef(false),
      pending: shallowRef(false),
      error: shallowRef(),
      refresh: candidateState.refresh,
    }),
  }
})

function setInput(selector: string, value: string) {
  const el = document.body.querySelector<HTMLInputElement>(selector)!
  el.value = value
  el.dispatchEvent(new Event('input', { bubbles: true }))
}

const resetFixture = () => {
  vi.clearAllMocks()
  document.body.innerHTML = ''
  let keyIndex = 0
  wms.createIdempotencyKey.mockImplementation(() => `wms-intent-${++keyIndex}`)
  wms.completeInbound.mockImplementation(
    (_id: string, _key: string, options?: { onCommandAttempt?: () => void }) => {
      options?.onCommandAttempt?.()
      return Promise.resolve(undefined)
    },
  )
  wms.refreshInboundOrders.mockClear()
  wms.completeOutbound.mockImplementation(
    (_id: string, _payload: unknown, _key: string, options?: { onCommandAttempt?: () => void }) => {
      options?.onCommandAttempt?.()
      return Promise.resolve(undefined)
    },
  )
  wms.completeCountExecution.mockImplementation(
    (
      _id: string,
      _countedQuantity: number,
      _key: string,
      options?: { onCommandAttempt?: () => void },
    ) => {
      options?.onCommandAttempt?.()
      return Promise.resolve(undefined)
    },
  )
  wms.failWcs.mockResolvedValue(undefined)
  wms.wcsRows = undefined
  wms.countRows = undefined
  wms.createInbound.mockResolvedValue(undefined)
  wms.createOutbound.mockResolvedValue(undefined)
  wms.inventoryContext = undefined
  wms.receivingQualityGates = []
  wms.supplierReturns = []
  wms.qualityGateStatus = undefined
  wms.isReleasedForPutaway = true
  wms.inboundStatus = 'open'
  inboundVersion.value = 3
  wms.confirmReceipt.mockReset()
  wms.purchaseReceipt = undefined
  wms.outboundStatus = 'open'
  wms.outboundPoolCode = 'POOL-01'
  wms.retryInboundPosting.mockReset()
  wms.retryOutboundPosting.mockReset()
  wms.permissionCodes = [
    'business.wms.receipts.read',
    'business.wms.receipts.manage',
    'business.wms.counts.read',
    'business.inventory.counts.manage',
    'business.quality.inspection-records.read',
  ]
}

export { wms, candidateState, inboundVersion, layoutStub, resetFixture, routeGuardState, setInput }
