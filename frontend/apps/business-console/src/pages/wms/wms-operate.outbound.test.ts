import { wms, routeGuardState, layoutStub, setInput, resetFixture } from './wms-operate.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { NvDialog, toast } from '@nerv-iip/ui'
import { BusinessOperationPendingError } from '@nerv-iip/api-client'
import CountsPage from './counts.vue'
import InboundPage from './inbound.vue'
import OutboundPage from './outbound.vue'

describe('WMS operate actions', () => {
  beforeEach(resetFixture)

  it('只为授权用户的明确失败单据提交原单过账重试', async () => {
    wms.outboundStatus = 'InventoryPostingFailed'
    const label = 'button[aria-label="重试库存过账 OB-1"]'
    wms.permissionCodes = ['business.wms.shipments.read']
    const readOnly = mount(OutboundPage, { global: { stubs: layoutStub } })
    expect(readOnly.find(label).exists()).toBe(false)
    const metrics = readOnly.get('[data-slot="nv-metric-strip"]').text()
    expect(metrics).toMatch(/本页待拣货复核发运\s*0\s*张/)
    expect(metrics).toMatch(/本页已完成\s*0\s*张/)
    readOnly.unmount()

    wms.permissionCodes.push('business.wms.shipments.manage')
    wms.retryOutboundPosting.mockResolvedValueOnce({
      data: { success: true, data: { requestId: 'retry-request' } },
    })
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await wrapper.get(label).trigger('click')
    await flushPromises()

    expect(wms.retryOutboundPosting).toHaveBeenCalledOnce()
    expect(wms.retryOutboundPosting).toHaveBeenCalledWith({
      path: { outboundOrderId: 'ob-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: { scopeKind: 'self', scopeId: 'emp049', idempotencyKey: expect.any(String) },
      throwOnError: false,
    })
    expect(wms.refreshOutboundOrders).toHaveBeenCalled()
    wrapper.unmount()
  })

  it('出库单未分配作业池时提示先分配并禁用复核', async () => {
    wms.outboundPoolCode = null
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()
    expect(wrapper.get('button[aria-label="完成复核 OB-1"]').attributes('disabled')).toBeDefined()
    expect(wrapper.text()).toContain('请先分配出库作业池')
    wrapper.unmount()
  })

  it('completes outbound review without asking for a pack review number (#3848)', async () => {
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成复核 OB-1"]').trigger('click')
    await flushPromises()

    // 复核单号由系统按编码规则生成：直接提交复核结论即可。
    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(wms.completeOutbound).toHaveBeenCalledWith(
      'ob-1',
      { passed: true },
      'wms-intent-1',
      expect.objectContaining({ attempt: 'initial' }),
    )
  })

  it('closes the outbound review as submitted while inventory posting is still running', async () => {
    // #3926：复核已落库、过账异步未完，过去报「提交出库复核失败：操作结果尚未确认」。
    wms.completeOutbound.mockRejectedValueOnce(
      new BusinessOperationPendingError('wms.outbound-order.complete'),
    )
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await wrapper.get('button[aria-label="完成复核 OB-1"]').trigger('click')
    await flushPromises()
    wms.refreshOutboundOrders.mockClear()

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    const reviewDialog = wrapper.findAllComponents(NvDialog).find((dialog) => dialog.props('open'))
    expect(reviewDialog).toBeUndefined()
    expect(document.body.textContent).not.toContain('提交出库复核失败')
    expect(toast.warning).toHaveBeenCalledWith(
      '出库复核已提交，库存正在过账。请稍后刷新查看过账结果。',
    )
    expect(wms.refreshOutboundOrders).toHaveBeenCalled()
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
      { passed: true },
      'wms-intent-1',
      expect.objectContaining({ attempt: 'initial' }),
    )
    expect(wms.completeOutbound).toHaveBeenNthCalledWith(
      2,
      'ob-1',
      { passed: true },
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
        return Promise.reject({ success: false, statusCode: 422, message: '复核结论无效' })
      },
    )
    const wrapper = mount(OutboundPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="完成复核 OB-1"]').trigger('click')
    await flushPromises()
    const submit = () =>
      document.body
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    submit()
    await flushPromises()

    document.body.querySelector<HTMLButtonElement>('#wms-pack-passed')!.click()
    await flushPromises()
    submit()
    await flushPromises()

    expect(wms.completeOutbound).toHaveBeenNthCalledWith(
      2,
      'ob-1',
      { passed: false },
      'wms-intent-2',
      expect.objectContaining({ attempt: 'initial' }),
    )
  })

  // #3832 审核 R3-2：库位选择器的工厂只从表单工厂来，每页一条轻量断言证明它传进去了。
  it.each([
    [
      '出库单 · 拣货库位',
      OutboundPage,
      '新建出库单',
      '#wms-out-site',
      '[aria-label="第 1 行拣货库位"]',
    ],
    ['出库单 · 批次', OutboundPage, '新建出库单', '#wms-out-site', '[aria-label="第 1 行批次"]'],
    ['入库单 · 批次建议', InboundPage, '新建入库单', '#wms-in-site', '[aria-label="第 1 行批次"]'],
    ['WMS 盘点', CountsPage, '新建盘点单', '#cnt-site', '#cnt-location'],
    ['WMS 盘点批次', CountsPage, '新建盘点单', '#cnt-site', '#cnt-lot'],
  ])(
    '%s的库位选择器拿到表单工厂',
    async (_name, Page, createLabel, siteSelector, locationSelector) => {
      const wrapper = mount(Page, { global: { stubs: layoutStub } })
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((b) => b.text().includes(createLabel))!
        .trigger('click')
      await flushPromises()

      setInput(siteSelector, 'S1')
      await flushPromises()

      expect(document.body.querySelector(locationSelector)!.getAttribute('data-form-site')).toBe(
        'S1',
      )
    },
  )
})
