import {
  barcode,
  toastMock,
  routerLinkStub,
  layoutStub,
  dialogStubs,
  selectStubs,
  mountPrintBatches,
  resetFixture,
} from './barcode-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import PrintBatchesPage from './print-batches.vue'

describe('barcode pages', () => {
  beforeEach(resetFixture)

  it('searches the configured printer directory and sends the selected option identity', async () => {
    const { NvEntityPicker: _realPicker, ...stubsWithRealPicker } = selectStubs
    const wrapper = mount(PrintBatchesPage, {
      attachTo: document.body,
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...stubsWithRealPicker,
          RouterLink: routerLinkStub,
        },
      },
    })
    try {
      await flushPromises()
      await wrapper.find('#barcode-print-printer').trigger('click')
      await flushPromises()
      const search = document.body.querySelector<HTMLInputElement>(
        'input[placeholder="搜索打印机"]',
      )!
      search.value = '不存在的打印机'
      search.dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
      expect(document.body.querySelectorAll('[role="option"]')).toHaveLength(0)
      search.value = '包装'
      search.dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
      const option = Array.from(
        document.body.querySelectorAll<HTMLElement>('[role="option"]'),
      ).find((element) => element.textContent?.includes('包装标签打印机'))!
      option.click()
      await flushPromises()
      const form = wrapper.find('#barcode-print-printer').element.closest('form')!
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
      await flushPromises()
      expect(barcode.dispatchPrintBatch).toHaveBeenCalledWith('pb-1', 'printer-01')
    } finally {
      wrapper.unmount()
    }
  })

  it('requires a selected printer and sends its configured identity without claiming printed', async () => {
    const wrapper = mountPrintBatches()
    await flushPromises()
    const form = wrapper.find('#barcode-print-printer').element.closest('form')!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(barcode.dispatchPrintBatch).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('请选择打印机。')
    barcode.dispatchPrintBatch.mockResolvedValueOnce({
      acknowledged: true,
      batch: { printBatchId: 'pb-1', status: 'sent-to-printer' },
    })
    await wrapper.find('#barcode-print-printer').setValue('printer-01')
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(barcode.dispatchPrintBatch).toHaveBeenCalledWith('pb-1', 'printer-01')
    expect(toastMock.success).toHaveBeenCalledWith('打印批次已发送至打印机，请现场确认出纸。')
    expect(wrapper.text()).toContain('待打印')
    expect(wrapper.find('section').text()).not.toContain('已打印')
  })

  it.each(['printed', 'sent-to-printer'])(
    'shows the persisted print time only for %s',
    async (status) => {
      barcode.printBatchStatus = status
      const wrapper = mountPrintBatches()
      await flushPromises()
      if (status === 'printed') {
        expect(wrapper.text()).toContain('打印时间：')
        expect(wrapper.text()).toContain(new Date('2026-10-10T01:23:45Z').toLocaleString())
        expect(wrapper.text()).toContain('(01)06912345678901(10)L2407')
      } else {
        expect(wrapper.text()).not.toContain('打印时间：')
      }
      wrapper.unmount()
    },
  )

  it.each(['reserved', 'sent-to-printer', 'delivery-unknown', 'printed'])(
    'does not offer dispatch in %s',
    async (status) => {
      barcode.printBatchStatus = status
      const wrapper = mountPrintBatches()
      await flushPromises()
      expect(wrapper.find('#barcode-print-printer').exists()).toBe(false)
    },
  )

  it('disables dispatch while sending and hides it without the print permission', async () => {
    barcode.dispatchPrintBatchPending = true
    const wrapper = mountPrintBatches()
    await flushPromises()
    const button = wrapper.findAll('button').find((item) => item.text().includes('发送打印'))!
    expect(button.attributes('disabled')).toBeDefined()
    wrapper.unmount()
    barcode.permissionCodes = []
    const denied = mountPrintBatches()
    await flushPromises()
    expect(denied.find('#barcode-print-printer').exists()).toBe(false)
  })

  it('confirms a reserved batch explicitly and leaves displayed state to server readback', async () => {
    barcode.printBatchStatus = 'reserved'
    const wrapper = mountPrintBatches()
    await flushPromises()
    expect(barcode.confirmPrintBatch).not.toHaveBeenCalled()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认待打印')!
      .trigger('click')
    await flushPromises()
    expect(barcode.confirmPrintBatch).toHaveBeenCalledWith('pb-1')
    expect(toastMock.success).toHaveBeenCalledWith('打印批次已确认待打印。')
    expect(wrapper.text()).toContain('已预留')
  })

  it('exposes confirmation as busy and disabled while the request is pending', async () => {
    barcode.printBatchStatus = 'reserved'
    barcode.confirmPrintBatchPending = true
    const wrapper = mountPrintBatches()
    await flushPromises()
    const button = wrapper.findAll('button').find((button) => button.text().includes('确认待打印'))!
    expect(button.attributes('aria-busy')).toBe('true')
    expect(button.attributes('disabled')).toBeDefined()
  })

  it.each(['ready-to-print', 'sent-to-printer', 'delivery-unknown', 'printed', 'failed'])(
    'does not offer confirmation for a batch in %s',
    async (status) => {
      barcode.printBatchStatus = status
      const wrapper = mountPrintBatches()
      await flushPromises()
      expect(wrapper.findAll('button').some((button) => button.text() === '确认待打印')).toBe(false)
    },
  )

  it('hides confirmation without print permission and reports a rejected confirmation as an operation error', async () => {
    barcode.printBatchStatus = 'reserved'
    barcode.permissionCodes = []
    const denied = mountPrintBatches()
    await flushPromises()
    expect(denied.findAll('button').some((button) => button.text() === '确认待打印')).toBe(false)
    denied.unmount()
    barcode.permissionCodes = ['business.barcodes.print']
    barcode.confirmPrintBatch.mockRejectedValueOnce(new Error('仅已预留的打印批次可以确认待打印。'))
    const wrapper = mountPrintBatches()
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '确认待打印')!
      .trigger('click')
    await flushPromises()
    expect(toastMock.error).toHaveBeenCalledWith(
      '确认待打印失败：仅已预留的打印批次可以确认待打印。',
    )
    expect(wrapper.text()).toContain('已预留')
  })
})
