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
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('@/composables/useBusinessWms', () => ({
  useWmsInboundOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    inboundOrders: computed(() => [
      { inboundOrderId: 'ib-1', inboundOrderNo: 'IB-1', status: 'created', createdAtUtc: '2026-06-01T00:00:00Z' },
    ]),
    inventoryContext: computed(() => undefined),
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
  }),
  useWmsOutboundOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    outboundOrders: computed(() => [
      { outboundOrderId: 'ob-1', outboundOrderNo: 'OB-1', status: 'created', createdAtUtc: '2026-06-01T00:00:00Z' },
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
      { wcsTaskId: 'w-1', externalTaskId: 'EXT-1', warehouseTaskId: 'WT-1', adapterType: 'docker', status: 'dispatched', attemptCount: 1 },
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

    const confirm = [...document.body.querySelectorAll('button')].find((b) => b.textContent?.trim() === '完成入库')
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
    document.body.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(wms.completeOutbound).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请输入复核单号。')

    const input = document.body.querySelector<HTMLInputElement>('#wms-pack-review-no')!
    input.value = 'PR-1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.completeOutbound).toHaveBeenCalledWith('ob-1', { packReviewNo: 'PR-1', passed: true })
  })

  it('creates an inbound order with a line item', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建入库单'))!.trigger('click')
    await flushPromises()

    setInput('#wms-in-no', 'IB-NEW')
    setInput('#wms-in-site', 'S1')
    setInput('#wms-in-srctype', '采购收货')
    setInput('#wms-in-srcid', 'PO-1')
    setInput('[aria-label="第 1 行物料"]', 'SKU1')
    setInput('[aria-label="第 1 行收货数量"]', '5')
    await flushPromises()

    document.body.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
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
    expect(body.lines[0]).toMatchObject({ lineNo: '1', skuCode: 'SKU1', receivedQuantity: 5 })
  })

  it('blocks inbound creation when header fields are missing', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建入库单'))!.trigger('click')
    await flushPromises()

    document.body.querySelector('form')!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
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
