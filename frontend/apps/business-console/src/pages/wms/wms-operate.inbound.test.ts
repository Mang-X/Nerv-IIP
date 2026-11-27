import { wms, layoutStub, setInput, resetFixture } from './wms-operate.fixture'
import { WMS_RECEIVING_QUALITY_OPTIONS } from '@/data/inventoryReference'
import { BusinessOperationPendingError } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { NvAlertDialog, toast } from '@nerv-iip/ui'
import { LifecycleStateChangedError } from '@/composables/shared/lifecycleAction'
import InboundPage from './inbound.vue'

describe('WMS operate actions', () => {
  beforeEach(resetFixture)

  it('只为授权用户的明确失败单据提交原单过账重试', async () => {
    wms.inboundStatus = 'InventoryPostingFailed'
    const label = 'button[aria-label="重试库存过账 IB-1"]'
    wms.permissionCodes = ['business.wms.receipts.read']
    const readOnly = mount(InboundPage, { global: { stubs: layoutStub } })
    expect(readOnly.find(label).exists()).toBe(false)
    readOnly.unmount()

    wms.permissionCodes.push('business.wms.receipts.manage')
    wms.retryInboundPosting.mockResolvedValueOnce({
      data: { success: true, data: { requestId: 'retry-request' } },
    })
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await wrapper.get(label).trigger('click')
    await flushPromises()

    expect(wms.retryInboundPosting).toHaveBeenCalledOnce()
    expect(wms.retryInboundPosting).toHaveBeenCalledWith({
      path: { inboundOrderId: 'ib-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: { scopeKind: 'self', scopeId: 'emp049', idempotencyKey: expect.any(String) },
      throwOnError: false,
    })
    expect(wms.refreshInboundOrders).toHaveBeenCalled()
    wrapper.unmount()
  })

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

  it('closes the inbound completion dialog when inventory posting is pending', async () => {
    wms.completeInbound.mockRejectedValueOnce(
      new BusinessOperationPendingError('wms.inbound-order.complete'),
    )
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await wrapper.get('button[aria-label="完成入库 IB-1"]').trigger('click')
    await flushPromises()
    const confirm = [...document.body.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === '完成入库',
    )!
    confirm.click()
    await flushPromises()

    expect(document.body.textContent).not.toContain('确认完成入库单 IB-1')
    expect(toast.warning).toHaveBeenCalledWith(
      '入库完成请求已提交，库存正在过账。请稍后刷新查看过账结果。',
    )
    expect(wms.refreshInboundOrders).toHaveBeenCalled()
    wrapper.unmount()
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

  it('creates an inbound order with a line item', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建入库单'))!
      .trigger('click')
    await flushPromises()

    setInput('#wms-in-site', 'S1')
    setInput('#wms-in-srctype', '采购收货')
    setInput('#wms-in-srcid', 'PO-1')
    // 单位不再是输入位：选完物料后由该物料的基本单位带出。
    setInput('[aria-label="第 1 行物料"]', 'SKU1')
    setInput('[aria-label="第 1 行收货数量"]', '5')
    setInput('[aria-label="第 1 行暂存库位"]', 'A-01')
    await flushPromises()
    // 暂存库位按表单工厂收窄候选、就地新增预填它（#3832）。
    expect(
      document.body.querySelector('[aria-label="第 1 行暂存库位"]')!.getAttribute('data-form-site'),
    ).toBe('S1')

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.createInbound).toHaveBeenCalledTimes(1)
    const body = wms.createInbound.mock.calls[0][0]
    expect(body).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      idempotencyKey: expect.any(String),
      siteCode: 'S1',
      sourceDocumentType: '采购收货',
      sourceDocumentId: 'PO-1',
    })
    expect(body.lines).toHaveLength(1)
    // 新行默认质量状态是「可用」对应的 WMS 免检码（#3923），不是库存侧别名 available。
    expect(body.lines[0].qualityStatus).toBe(
      WMS_RECEIVING_QUALITY_OPTIONS.find((option) => option.label === '可用')!.value,
    )
    // 后端契约要求的行字段必须全部下发。
    expect(body.lines[0]).toMatchObject({
      lineNo: '1',
      skuCode: 'SKU1',
      uomCode: 'pcs',
      receivedQuantity: 5,
      stagingLocationCode: 'A-01',
      qualityStatus: 'unrestricted',
      ownerType: 'owned',
    })
  })

  it('创建内容冲突时展示服务端中文原因并保留入库表单', async () => {
    const reason = '本次提交与此前的创建内容不一致，请重新发起创建。'
    wms.createInbound.mockRejectedValueOnce({ success: false, message: reason })
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建入库单'))!
      .trigger('click')
    await flushPromises()

    setInput('#wms-in-site', 'S1')
    setInput('#wms-in-srctype', '采购收货')
    setInput('#wms-in-srcid', 'PO-1')
    setInput('[aria-label="第 1 行物料"]', 'SKU1')
    setInput('[aria-label="第 1 行收货数量"]', '5')
    setInput('[aria-label="第 1 行暂存库位"]', 'A-01')
    await flushPromises()

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(toast.error).toHaveBeenCalledWith(`创建入库单失败：${reason}`)
    expect(document.body.querySelector('#wms-in-site')).not.toBeNull()
    expect(toast.success).not.toHaveBeenCalled()
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
      uomCode: 'pcs',
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

  it('blocks inbound creation when a required line field or positive quantity is missing', async () => {
    const wrapper = mount(InboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建入库单'))!
      .trigger('click')
    await flushPromises()

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
    expect(document.body.textContent).toContain('请填写来源类型、来源单据与工厂。')
  })
})
