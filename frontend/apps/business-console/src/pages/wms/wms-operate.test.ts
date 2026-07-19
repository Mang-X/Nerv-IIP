import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import InboundPage from './inbound.vue'
import OutboundPage from './outbound.vue'
import WcsPage from './wcs.vue'

const wms = vi.hoisted(() => ({
  completeInbound: vi.fn(),
  completeOutbound: vi.fn(),
  failWcs: vi.fn(),
  createInbound: vi.fn(),
  createOutbound: vi.fn(),
  inventoryContext: undefined as unknown,
  receivingQualityGates: [] as unknown[],
  supplierReturns: [] as unknown[],
  isReleasedForPutaway: true,
  refreshReceivingQuality: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('vue-router', () => ({
  RouterLink: {
    props: ['to'],
    template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
  },
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsInboundOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    inboundOrders: computed(() => [
      {
        inboundOrderId: 'ib-1',
        inboundOrderNo: 'IB-1',
        status: 'created',
        createdAtUtc: '2026-06-01T00:00:00Z',
        isReleasedForPutaway: wms.isReleasedForPutaway,
      },
    ]),
    inventoryContext: computed(() => wms.inventoryContext),
    inboundOrdersError: shallowRef(undefined),
    inboundOrdersPending: shallowRef(false),
    inboundOrdersTotal: computed(() => 1),
    refreshInboundOrders: vi.fn(),
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
        status: 'created',
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

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

describe('WMS operate actions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    wms.completeInbound.mockResolvedValue(undefined)
    wms.completeOutbound.mockResolvedValue(undefined)
    wms.failWcs.mockResolvedValue(undefined)
    wms.createInbound.mockResolvedValue(undefined)
    wms.createOutbound.mockResolvedValue(undefined)
    wms.inventoryContext = undefined
    wms.receivingQualityGates = []
    wms.supplierReturns = []
    wms.isReleasedForPutaway = true
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

    expect(wms.completeInbound).toHaveBeenCalledWith('ib-1')
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

    expect(wms.completeOutbound).toHaveBeenCalledWith('ob-1', {
      packReviewNo: 'PR-1',
      passed: true,
    })
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
    setInput('[aria-label="第 1 行物料"]', 'SKU1')
    setInput('[aria-label="第 1 行单位"]', 'EA')
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

    expect(wrapper.text()).toContain('库存上下文')
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
    const taskLink = wrapper.get('a[aria-label="查看检验任务 IB-1"]')
    expect(taskLink.attributes('data-to')).toContain('/quality/inspection-tasks')
    expect(taskLink.attributes('data-to')).toContain('sourceDocumentNo')
    expect(taskLink.attributes('data-to')).toContain('IB-1')
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
