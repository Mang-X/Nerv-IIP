import {
  state,
  mountOptions,
  fillCompleteReasonAndLocation,
  openCompleteSheet,
} from './maintenance-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import WorkOrdersPage from './work-orders.vue'

describe('maintenance work orders page', () => {
  // 审核：备件成本人工覆盖为负/非法时必须拦截，不得发出负成本或静默丢字段。
  it('rejects a negative / non-numeric spare-part cost override on complete', async () => {
    state.query = {} // 不自动打开建单抽屉
    state.workOrders = [
      {
        workOrderId: 'wo-1',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    // 通过行操作打开「完成工单」抽屉。
    const rowAction = document.body.querySelector<HTMLButtonElement>(
      '[aria-label^="维护工单操作"]',
    )!
    rowAction.click()
    await flushPromises()
    const completeItem = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (el) => el.textContent?.includes('完成工单'),
    )!
    completeItem.click()
    await flushPromises()

    const costInput = document.body.querySelector<HTMLInputElement>('#mwo-spare-cost')!
    costInput.value = '-1'
    costInput.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    await fillCompleteReasonAndLocation()

    const form = costInput.closest('form')!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.completeWorkOrder).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('备件成本汇总需为非负数')
  })

  // #897：完工登记「实际执行技师」，默认沿用建单指派技师并随完工请求提交 actualTechnicianUserId。
  it('submits the actual technician on complete, defaulting from the assigned technician', async () => {
    state.query = {} // 不自动打开建单抽屉
    state.workOrders = [
      {
        workOrderId: 'wo-tech',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
        assignedTechnicianUserId: 'user-1',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    const rowAction = document.body.querySelector<HTMLButtonElement>(
      '[aria-label^="维护工单操作"]',
    )!
    rowAction.click()
    await flushPromises()
    const completeItem = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (el) => el.textContent?.includes('完成工单'),
    )!
    completeItem.click()
    await flushPromises()

    // 「实际执行技师」字段进入完工抽屉。
    expect(document.body.textContent).toContain('实际执行技师')

    // 登记一条更换备件（完工前置：至少一条备件行）；物料取目录里的备件，单位随主档自动带出。
    const spareSku = document.body.querySelector<HTMLInputElement>('[id^="spare-sku-"]')!
    spareSku.value = 'BRG-6205'
    spareSku.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    await fillCompleteReasonAndLocation()

    const form = spareSku.closest('form')!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.completeWorkOrder).toHaveBeenCalledTimes(1)
    const [id, body] = state.completeWorkOrder.mock.calls[0]
    expect(id).toBe('wo-tech')
    // 实际技师默认沿用建单指派技师 user-1（未改选时）。
    expect(body.actualTechnicianUserId).toBe('user-1')
    // 回归 #1285：备件单位来自物料主档的基本单位，不是页面写死的兜底单位。
    expect(body).toMatchObject({ spareParts: [expect.objectContaining({ uomCode: 'pcs' })] })
  })

  it('换件行就地新建物料后物料列表晚一步刷新回来，单位仍按新物料的基本单位带出', async () => {
    state.query = {}
    state.baseUomBySku.value = new Map([['BRG-6205', 'pcs']])
    state.workOrders = [
      {
        workOrderId: 'wo-new-sku',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
        assignedTechnicianUserId: 'user-1',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[aria-label^="维护工单操作"]')!.click()
    await flushPromises()
    ;[...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
      .find((el) => el.textContent?.includes('完成工单'))!
      .click()
    await flushPromises()

    // 选择器选中新建的物料时，物料列表还没刷新回它，查不到基本单位。
    const spareSku = document.body.querySelector<HTMLInputElement>('[id^="spare-sku-"]')!
    spareSku.value = 'SKU-NEW'
    spareSku.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    const spareUom = document.body.querySelector<HTMLInputElement>('[id^="spare-uom-"]')!
    expect(spareUom.value).toBe('')

    // 物料列表刷新回来，基本单位查得到了。
    state.baseUomBySku.value = new Map([
      ['BRG-6205', 'pcs'],
      ['SKU-NEW', 'kg'],
    ])
    await flushPromises()
    expect(spareUom.value).toBe('kg')
    await fillCompleteReasonAndLocation()

    spareSku
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    const [, body] = state.completeWorkOrder.mock.calls.at(-1)!
    expect(body).toMatchObject({
      spareParts: [expect.objectContaining({ skuCode: 'SKU-NEW', uomCode: 'kg' })],
    })
  })

  // #3855：完工停机原因来自权威目录，不再预填目录里根本不存在的写死码。
  it('完工停机原因从目录选择，不预填；未选原因不提交', async () => {
    state.query = {}
    state.workOrders = [
      {
        workOrderId: 'wo-reason',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await openCompleteSheet()

    const reason = document.body.querySelector<HTMLInputElement>('#mwo-reason')!
    expect(reason.value).toBe('')
    const spareSku = document.body.querySelector<HTMLInputElement>('[id^="spare-sku-"]')!
    spareSku.value = 'BRG-6205'
    spareSku.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    spareSku
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.completeWorkOrder).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请选择停机原因')

    await fillCompleteReasonAndLocation()
    spareSku
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    const [, body] = state.completeWorkOrder.mock.calls.at(-1)!
    expect(body.downtimeReasonCode).toBe('Line-A.Spindle')
  })

  // #3902：备件从设备所在工厂的已登记库位领出，出库请求带上工厂与库位。
  it('完工换件行带出设备所在工厂，并要求每条备件选择领出库位', async () => {
    state.query = {}
    state.workOrders = [
      {
        workOrderId: 'wo-location',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await openCompleteSheet()

    expect(document.body.textContent).toContain('一号工厂')
    const spareSku = document.body.querySelector<HTMLInputElement>('[id^="spare-sku-"]')!
    spareSku.value = 'BRG-6205'
    spareSku.dispatchEvent(new Event('input', { bubbles: true }))
    const reason = document.body.querySelector<HTMLInputElement>('#mwo-reason')!
    reason.value = 'Line-A.Spindle'
    reason.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    const form = spareSku.closest('form')!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(state.completeWorkOrder).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请为每条备件选择领出库位')

    await fillCompleteReasonAndLocation('loc-spare-01')
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    const [, body] = state.completeWorkOrder.mock.calls.at(-1)!
    expect(body).toMatchObject({
      spareParts: [
        expect.objectContaining({
          skuCode: 'BRG-6205',
          siteCode: 'SITE-001',
          locationCode: 'loc-spare-01',
        }),
      ],
    })
  })

  it('设备没有所属工厂时如实提示，不猜工厂也不提交', async () => {
    state.query = {}
    state.workOrders = [
      {
        workOrderId: 'wo-no-site',
        deviceAssetId: 'DEV-NO-SITE',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await openCompleteSheet()

    expect(document.body.textContent).toContain('设备未登记所属工厂')
    const spareSku = document.body.querySelector<HTMLInputElement>('[id^="spare-sku-"]')!
    spareSku.value = 'BRG-6205'
    spareSku.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    await fillCompleteReasonAndLocation()
    spareSku
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.completeWorkOrder).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('该设备未登记所属工厂')
  })
})
