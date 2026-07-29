import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { NvAlertDialog, NvDialog } from '@nerv-iip/ui'

import { LifecycleStateChangedError } from '@/composables/lifecycleAction'
import CountsPage from './counts.vue'
import InboundPage from './inbound.vue'
import OutboundPage from './outbound.vue'
import WcsPage from './wcs.vue'

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

const wms = vi.hoisted(() => ({
  createIdempotencyKey: vi.fn(() => 'wms-intent-1'),
  completeInbound: vi.fn(),
  completeOutbound: vi.fn(),
  completeCountExecution: vi.fn(),
  failWcs: vi.fn(),
  createInbound: vi.fn(),
  createOutbound: vi.fn(),
  inventoryContext: undefined as unknown,
  receivingQualityGates: [] as unknown[],
  supplierReturns: [] as unknown[],
  qualityGateStatus: undefined as string | undefined,
  isReleasedForPutaway: true,
  permissionCodes: [
    'business.wms.receipts.read',
    'business.wms.receipts.manage',
    'business.quality.inspection-records.read',
  ] as string[],
  refreshReceivingQuality: vi.fn(),
  refreshInboundOrders: vi.fn(async () => undefined),
  refreshCountExecutions: vi.fn(async () => undefined),
}))
const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routeGuardState.guard = guard
  }),
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
}))

// 物料目录与默认工厂来自主数据 facade（需要 pinia 上下文），页面测试里给确定的目录即可。
vi.mock('@/composables/useInventoryScope', async () => {
  const { computed, ref } = await import('vue')
  const catalog = {
    siteOptions: computed(() => [{ value: 'SITE-001', label: '上海工厂' }]),
    sitesPending: ref(false),
    skuOptions: computed(() => [{ value: 'SKU-001', label: '前减振器总成', hint: 'pcs' }]),
    skusPending: ref(false),
    // 收货行的单位随所选物料的基本单位带出（不再手输）。
    resolveUomCode: () => 'EA',
  }
  return {
    FALLBACK_INVENTORY_SITE_CODE: 'SITE-001',
    FALLBACK_INVENTORY_UOM_CODE: 'pcs',
    useInventoryScopeCatalog: () => catalog,
    useInventoryScopeDefaults: () => catalog,
  }
})

// 库位/批次目录后端无读面，真实实现从仓储作业记录派生；测试给确定选项。
vi.mock('@/composables/useWarehouseCodeCatalog', async () => {
  const { computed, shallowRef } = await import('vue')
  return {
    WAREHOUSE_CATALOG_SOURCE_TEXT: '数据来自现有库存与仓储作业记录（暂无库位主数据）',
    WAREHOUSE_LOCATION_EMPTY_TEXT: '系统里还没有出现过库位，可直接录入新库位编码',
    WAREHOUSE_LOT_EMPTY_TEXT: '系统里还没有出现过批次',
    WAREHOUSE_SERIAL_EMPTY_TEXT: '系统里还没有出现过序列号',
    useWarehouseCodeCatalog: () => ({
      locationOptions: computed(() => [
        { value: 'STAGE-01', label: 'STAGE-01' },
        { value: 'RACK-A-01', label: 'RACK-A-01' },
      ]),
      lotOptions: computed(() => [{ value: 'LOT-001', label: 'LOT-001' }]),
      serialOptions: computed(() => [{ value: 'SN-001', label: 'SN-001' }]),
      warehouseCatalogPending: shallowRef(false),
    }),
  }
})

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: wms.permissionCodes } }),
}))

vi.mock('@/composables/useBusinessWms', () => ({
  createWmsIdempotencyKey: wms.createIdempotencyKey,
  useWmsInboundOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    inboundOrders: computed(() => [
      {
        inboundOrderId: 'ib-1',
        inboundOrderNo: 'IB-1',
        status: 'open',
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
  }),
  useWmsOutboundOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    outboundOrders: computed(() => [
      {
        outboundOrderId: 'ob-1',
        outboundOrderNo: 'OB-1',
        status: 'open',
        createdAtUtc: '2026-06-01T00:00:00Z',
      },
    ]),
    outboundOrdersError: shallowRef(undefined),
    outboundOrdersPending: shallowRef(false),
    outboundOrdersTotal: computed(() => 1),
    refreshOutboundOrders: vi.fn(),
    completeOutbound: wms.completeOutbound,
    completeOutboundPending: shallowRef(false),
    completeOutboundError: shallowRef(undefined),
    createOutbound: wms.createOutbound,
    createOutboundPending: shallowRef(false),
    createOutboundError: shallowRef(undefined),
  }),
  useWmsCountExecutions: () => ({
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      locationCode: undefined,
      status: undefined,
      skip: 0,
      take: 100,
    }),
    countExecutions: computed(() => [
      {
        countExecutionId: 'count-1',
        countNo: 'CNT-1',
        skuCode: 'SKU-001',
        uomCode: 'EA',
        siteCode: 'SITE-001',
        locationCode: 'RACK-A-01',
        expectedQuantity: 7,
        status: 'open',
      },
    ]),
    countExecutionsError: shallowRef(undefined),
    countExecutionsPending: shallowRef(false),
    countExecutionsTotal: computed(() => 1),
    refreshCountExecutions: wms.refreshCountExecutions,
    createCountExecution: vi.fn(),
    createCountExecutionPending: shallowRef(false),
    completeCountExecution: wms.completeCountExecution,
    completeCountExecutionPending: shallowRef(false),
  }),
  useWmsWcsTasks: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    wcsTasks: computed(() => [
      {
        wcsTaskId: 'w-1',
        externalTaskId: 'EXT-1',
        warehouseTaskId: 'WT-1',
        adapterType: 'docker',
        status: 'dispatched',
        attemptCount: 1,
      },
    ]),
    wcsTasksError: shallowRef(undefined),
    wcsTasksPending: shallowRef(false),
    wcsTasksTotal: computed(() => 1),
    refreshWcsTasks: vi.fn(),
    dispatchWcs: vi.fn(),
    dispatchWcsPending: shallowRef(false),
    dispatchWcsError: shallowRef(undefined),
    failWcs: wms.failWcs,
    failWcsPending: shallowRef(false),
    failWcsError: shallowRef(undefined),
    completeWcs: vi.fn(),
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
  props: ['modelValue', 'options', 'id'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const layoutStub = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvEntityPicker: onlySelectStub,
  NvSearchSelect: onlySelectStub,
}

describe('WMS operate actions', () => {
  beforeEach(() => {
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
      (
        _id: string,
        _payload: unknown,
        _key: string,
        options?: { onCommandAttempt?: () => void },
      ) => {
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
    wms.createInbound.mockResolvedValue(undefined)
    wms.createOutbound.mockResolvedValue(undefined)
    wms.inventoryContext = undefined
    wms.receivingQualityGates = []
    wms.supplierReturns = []
    wms.qualityGateStatus = undefined
    wms.isReleasedForPutaway = true
    wms.permissionCodes = [
      'business.wms.receipts.read',
      'business.wms.receipts.manage',
      'business.quality.inspection-records.read',
    ]
  })

  function setInput(selector: string, value: string) {
    const el = document.body.querySelector<HTMLInputElement>(selector)!
    el.value = value
    el.dispatchEvent(new Event('input', { bubbles: true }))
  }

  it('completes an inbound order after confirmation', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成入库 IB-1"]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('确认完成入库单 IB-1')
    expect(wms.completeInbound).not.toHaveBeenCalled()

    const confirm = [...document.body.querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === '完成入库',
    )
    confirm?.click()
    await flushPromises()

    expect(wms.completeInbound).toHaveBeenCalledWith(
      'ib-1',
      'wms-intent-1',
      expect.objectContaining({ attempt: 'initial' }),
    )
  })

  it('closes and clears a stale completion dialog after a typed lifecycle conflict', async () => {
    wms.completeInbound.mockRejectedValueOnce(new LifecycleStateChangedError('conflict'))
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成入库 IB-1"]').trigger('click')
    await flushPromises()
    const confirm = [...document.body.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === '完成入库',
    )
    confirm?.click()
    await flushPromises()

    expect(wms.refreshInboundOrders).toHaveBeenCalledOnce()
    expect(document.body.textContent).not.toContain('确认完成入库单 IB-1')
  })

  it('reuses the same idempotency key when an inbound completion is retried', async () => {
    wms.completeInbound.mockImplementationOnce(
      (_id: string, _key: string, options?: { onCommandAttempt?: () => void }) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new Error('network interrupted'))
      },
    )
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成入库 IB-1"]').trigger('click')
    await flushPromises()
    const submit = () =>
      [...document.body.querySelectorAll('button')]
        .find((button) => button.textContent?.trim() === '完成入库')
        ?.click()

    submit()
    await flushPromises()
    submit()
    await flushPromises()

    expect(wms.createIdempotencyKey).toHaveBeenCalledOnce()
    expect(wms.completeInbound).toHaveBeenNthCalledWith(
      1,
      'ib-1',
      'wms-intent-1',
      expect.objectContaining({ attempt: 'initial' }),
    )
    expect(wms.completeInbound).toHaveBeenNthCalledWith(
      2,
      'ib-1',
      'wms-intent-1',
      expect.objectContaining({ attempt: 'retry' }),
    )
  })

  it('keeps an indeterminate inbound completion dialog locked to its frozen intent', async () => {
    wms.completeInbound.mockImplementationOnce(
      (_id: string, _key: string, options?: { onCommandAttempt?: () => void }) => {
        options?.onCommandAttempt?.()
        return Promise.reject({ response: { status: 503 }, message: 'service unavailable' })
      },
    )
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成入库 IB-1"]').trigger('click')
    await flushPromises()
    const submit = () =>
      [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => button.textContent?.trim() === '完成入库',
      )
    submit()?.click()
    await flushPromises()

    const completeDialog = wrapper
      .findAllComponents(NvAlertDialog)
      .find((dialog) => dialog.props('open'))
    expect(completeDialog).toBeTruthy()
    completeDialog!.vm.$emit('update:open', false)
    await flushPromises()
    expect(completeDialog!.props('open')).toBe(true)

    submit()?.click()
    await flushPromises()
    expect(wms.completeInbound).toHaveBeenNthCalledWith(
      2,
      'ib-1',
      'wms-intent-1',
      expect.objectContaining({ attempt: 'retry' }),
    )
  })

  it('requires a pack review number before completing outbound review', async () => {
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成复核 OB-1"]').trigger('click')
    await flushPromises()

    // Submit without a review number → validation blocks the mutation.
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(wms.completeOutbound).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请输入复核单号。')

    const input = document.body.querySelector<HTMLInputElement>('#wms-pack-review-no')!
    input.value = 'PR-1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.completeOutbound).toHaveBeenCalledWith(
      'ob-1',
      {
        packReviewNo: 'PR-1',
        passed: true,
      },
      'wms-intent-1',
      expect.objectContaining({ attempt: 'initial' }),
    )
  })

  it('marks only the second same-key outbound submission as a retry', async () => {
    wms.completeOutbound.mockImplementationOnce(
      (
        _id: string,
        _payload: unknown,
        _key: string,
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new Error('network interrupted'))
      },
    )
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成复核 OB-1"]').trigger('click')
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('#wms-pack-review-no')!
    input.value = 'PR-1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    const submit = () =>
      document.body
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))

    submit()
    await flushPromises()
    const reviewDialog = wrapper.findAllComponents(NvDialog).find((dialog) => dialog.props('open'))
    expect(reviewDialog).toBeTruthy()
    reviewDialog!.vm.$emit('update:open', false)
    await flushPromises()
    expect(reviewDialog!.props('open')).toBe(true)
    const cancel = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '取消',
    )
    expect(cancel?.disabled).toBe(true)
    expect(routeGuardState.guard?.()).toBe(false)
    submit()
    await flushPromises()

    expect(wms.completeOutbound).toHaveBeenNthCalledWith(
      1,
      'ob-1',
      { packReviewNo: 'PR-1', passed: true },
      'wms-intent-1',
      expect.objectContaining({ attempt: 'initial' }),
    )
    expect(wms.completeOutbound).toHaveBeenNthCalledWith(
      2,
      'ob-1',
      { packReviewNo: 'PR-1', passed: true },
      'wms-intent-1',
      expect.objectContaining({ attempt: 'retry' }),
    )
  })

  it('keeps an indeterminate count intent frozen when the dialog requests close', async () => {
    wms.completeCountExecution.mockImplementationOnce(
      (
        _id: string,
        _countedQuantity: number,
        _key: string,
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new Error('network interrupted'))
      },
    )
    const wrapper = mount(CountsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="盘点操作 CNT-1"]').trigger('click')
    await flushPromises()
    const completeItem = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (item) => item.textContent?.includes('完成盘点'),
    )
    expect(completeItem).toBeTruthy()
    completeItem!.click()
    await flushPromises()

    const input = document.body.querySelector<HTMLInputElement>('#cnt-counted')!
    input.value = '8'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    const form = [...document.body.querySelectorAll<HTMLFormElement>('form')].find((candidate) =>
      candidate.querySelector('#cnt-counted'),
    )!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    const completeDialog = wrapper
      .findAllComponents(NvDialog)
      .find((dialog) => dialog.props('open'))
    expect(completeDialog).toBeTruthy()
    completeDialog!.vm.$emit('update:open', false)
    await flushPromises()
    expect(completeDialog!.props('open')).toBe(true)
    const cancel = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '取消',
    )
    expect(cancel?.disabled).toBe(true)
    expect(input.disabled).toBe(true)
    expect(routeGuardState.guard?.()).toBe(false)

    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(wms.completeCountExecution).toHaveBeenNthCalledWith(
      2,
      'count-1',
      8,
      'wms-intent-1',
      expect.objectContaining({ attempt: 'retry' }),
    )
  })

  it('rotates the outbound key and resets to initial after a determinate 422 input correction', async () => {
    wms.completeOutbound.mockImplementationOnce(
      (
        _id: string,
        _payload: unknown,
        _key: string,
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject({ success: false, statusCode: 422, message: '复核单号无效' })
      },
    )
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成复核 OB-1"]').trigger('click')
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('#wms-pack-review-no')!
    input.value = 'PR-1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    const submit = () =>
      document.body
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    submit()
    await flushPromises()

    input.value = 'PR-2'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    submit()
    await flushPromises()

    expect(wms.completeOutbound).toHaveBeenNthCalledWith(
      2,
      'ob-1',
      { packReviewNo: 'PR-2', passed: true },
      'wms-intent-2',
      expect.objectContaining({ attempt: 'initial' }),
    )
  })

  it('creates an inbound order with a line item', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建入库单'))!
      .trigger('click')
    await flushPromises()

    setInput('#wms-in-no', 'IB-NEW')
    setInput('#wms-in-site', 'S1')
    setInput('#wms-in-srctype', '采购收货')
    setInput('#wms-in-srcid', 'PO-1')
    // 单位不再是输入位：选完物料后由该物料的基本单位带出。
    setInput('[aria-label="第 1 行物料"]', 'SKU1')
    setInput('[aria-label="第 1 行收货数量"]', '5')
    setInput('[aria-label="第 1 行暂存库位"]', 'A-01')
    await flushPromises()

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.createInbound).toHaveBeenCalledTimes(1)
    const body = wms.createInbound.mock.calls[0][0]
    expect(body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      inboundOrderNo: 'IB-NEW',
      siteCode: 'S1',
      sourceDocumentType: '采购收货',
      sourceDocumentId: 'PO-1',
    })
    expect(body.lines).toHaveLength(1)
    // 后端契约要求的行字段必须全部下发。
    expect(body.lines[0]).toMatchObject({
      lineNo: '1',
      skuCode: 'SKU1',
      uomCode: 'EA',
      receivedQuantity: 5,
      stagingLocationCode: 'A-01',
      qualityStatus: 'available',
      ownerType: 'owned',
    })
  })

  it('links inbound orders to scan records through the SPA router', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const scanLink = wrapper
      .findAll('[data-router-link]')
      .find((link) => link.attributes('data-to')?.includes('/barcode/scans'))!
    const target = scanLink.attributes('data-to')

    expect(scanLink.text()).toContain('扫码记录')
    expect(target).toContain('"path":"/barcode/scans"')
    expect(target).toContain('"sourceWorkflow":"wms.receiving"')
    expect(target).toContain('"sourceDocumentId":"IB-1"')
  })

  it('renders inbound inventory facts with inventory links and row-level scan links', async () => {
    wms.inventoryContext = {
      source: 'BusinessInventory',
      status: 'ok',
      skuCode: 'SKU-001',
      uomCode: 'EA',
      siteCode: 'S1',
      locationCode: 'A-01',
      lotNo: 'LOT-001',
      serialNo: 'SN-001',
      onHandQuantity: 10,
      reservedQuantity: 2,
      availableQuantity: 8,
      items: [
        {
          locationCode: 'A-01',
          lotNo: 'LOT-001',
          serialNo: 'SN-001',
          qualityStatus: 'blocked',
          ownerType: 'owned',
          onHandQuantity: 10,
          reservedQuantity: 2,
          availableQuantity: 8,
        },
      ],
    }

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('库存明细')
    expect(wrapper.text()).toContain('LOT-001')
    expect(wrapper.text()).toContain('SN-001')
    expect(wrapper.text()).toContain('冻结/其他')
    expect(wrapper.text()).toContain('2')

    const links = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
    expect(
      links.some(
        (to) => to.includes('/inventory/lots') && to.includes('LOT-001') && to.includes('SN-001'),
      ),
    ).toBe(true)
    expect(
      links.some(
        (to) =>
          to.includes('/inventory/availability') && to.includes('SKU-001') && to.includes('A-01'),
      ),
    ).toBe(true)
    expect(
      links.some(
        (to) =>
          to.includes('/barcode/scans') && to.includes('wms.receiving') && to.includes('IB-1'),
      ),
    ).toBe(true)
  })

  it('disables putaway while the server reports a pending inspection and explains the gate', async () => {
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'pending',
        qualityStatus: 'inspection',
        stagingLocationCode: 'QA-STAGE-01',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('待检')
    expect(wrapper.text()).toContain('检验完成前不能上架')
    expect(wrapper.text()).toContain('QA-STAGE-01')
    expect(wrapper.get('button[aria-label="上架 IB-1"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('a[aria-label="查看检验任务 IB-1"]').attributes('data-to')).toContain(
      'sourceDocumentNo',
    )
  })

  it('shows exempt receiving as released without inventing an inspection task', async () => {
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'not-required',
        qualityStatus: 'available',
        stagingLocationCode: 'STAGE-01',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('免检')
    expect(wrapper.text()).toContain('已跳过待检，可进入上架')
    const putawayLink = wrapper.get('a[aria-label="上架 IB-1"]')
    expect(putawayLink.attributes('data-to')).toContain('/wms/putaway')
    expect(putawayLink.attributes('data-to')).toContain('inboundOrderNo')
    expect(putawayLink.attributes('data-to')).toContain('IB-1')
    expect(putawayLink.attributes('data-to')).toContain('inboundOrderId')
    expect(putawayLink.attributes('data-to')).toContain('ib-1')
    expect(putawayLink.attributes('data-to')).toContain('create')
    expect(wrapper.find('a[aria-label="查看检验任务 IB-1"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('免检无需检验任务')
  })

  it('shows conditional release as restricted putaway and rejected receiving with real return facts', async () => {
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'conditional-release',
        qualityStatus: 'available',
        stagingLocationCode: 'QA-STAGE-01',
        inspectionRecordId: 'QI-1',
      },
      {
        inboundOrderNo: 'IB-1',
        lineNo: '2',
        skuCode: 'SKU-002',
        qualityGateStatus: 'rejected',
        qualityStatus: 'rejected',
        stagingLocationCode: 'QUAR-01',
        inspectionRecordId: 'QI-2',
        qualityDispositionReason: '包装破损',
      },
    ]
    wms.supplierReturns = [
      {
        inboundOrderNo: 'IB-1',
        inboundOrderLineNo: '2',
        inspectionRecordId: 'QI-2',
        supplierReturnNo: 'RTS-IB-1-002',
        skuCode: 'SKU-002',
        locationCode: 'QUAR-01',
        dispositionType: 'return-to-supplier',
        status: 'Open',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('条件放行')
    expect(wrapper.text()).toContain('不合格')
    expect(wrapper.text()).toContain('退供应商')
    expect(wrapper.text()).toContain('RTS-IB-1-002')
    expect(wrapper.text()).toContain('QUAR-01')
    expect(wrapper.find('a[aria-label="查看检验任务 IB-1"]').exists()).toBe(false)
    const inspectionRecordLinks = wrapper
      .findAll('[data-router-link]')
      .map((link) => link.attributes('data-to') ?? '')
      .filter((to) => to.includes('/quality/inspections'))
    expect(inspectionRecordLinks).toHaveLength(2)
    expect(inspectionRecordLinks.some((to) => to.includes('QI-1'))).toBe(true)
    expect(inspectionRecordLinks.some((to) => to.includes('QI-2'))).toBe(true)
  })

  it('routes a conditionally released order to restricted putaway', async () => {
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'conditional-release',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('条件放行')
    expect(wrapper.get('a[aria-label="受限上架 IB-1"]').attributes('data-to')).toContain(
      '/wms/putaway',
    )
  })

  it('keeps putaway disabled when the inbound response has not released the order', async () => {
    wms.isReleasedForPutaway = false
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'passed',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.get('button[aria-label="上架 IB-1"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('WMS 尚未返回整单上架放行权限')
  })

  it('keeps putaway disabled for a read-only WMS principal', async () => {
    wms.permissionCodes = ['business.wms.receipts.read', 'business.quality.inspection-records.read']
    wms.qualityGateStatus = 'passed'
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'passed',
        inspectionRecordId: 'QI-1',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.get('button[aria-label="上架 IB-1"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('缺少收货管理权限')
  })

  it('does not expose quality links without inspection-record read permission', async () => {
    wms.permissionCodes = ['business.wms.receipts.read', 'business.wms.receipts.manage']
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'pending',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.find('a[aria-label="查看检验任务 IB-1"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('缺少质量检验读取权限')
  })

  it('does not let a stale released snapshot override a stricter pending line', async () => {
    wms.qualityGateStatus = 'passed'
    wms.receivingQualityGates = [
      {
        inboundOrderNo: 'IB-1',
        lineNo: '1',
        skuCode: 'SKU-001',
        qualityGateStatus: 'pending',
      },
    ]

    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('待检')
    expect(wrapper.get('button[aria-label="上架 IB-1"]').attributes('disabled')).toBeDefined()
  })

  it('blocks inbound creation when a required line field or positive quantity is missing', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建入库单'))!
      .trigger('click')
    await flushPromises()

    setInput('#wms-in-no', 'IB-NEW')
    setInput('#wms-in-site', 'S1')
    setInput('#wms-in-srctype', '采购收货')
    setInput('#wms-in-srcid', 'PO-1')
    // 物料填了，但单位/库位缺失、数量非正 → 应被前端校验拦截，不发请求。
    setInput('[aria-label="第 1 行物料"]', 'SKU1')
    setInput('[aria-label="第 1 行收货数量"]', '0')
    await flushPromises()

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.createInbound).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('第 1 行')
  })

  it('blocks inbound creation when header fields are missing', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建入库单'))!
      .trigger('click')
    await flushPromises()

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.createInbound).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请填写入库单号、来源类型、来源单据与工厂。')
  })

  it('renders per-row WCS action menus', async () => {
    const wrapper = mount(WcsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.find('button[aria-label="WCS 任务操作 EXT-1"]').exists()).toBe(true)
  })
})
