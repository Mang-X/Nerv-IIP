import {
  barcode,
  layoutStub,
  dialogStubs,
  selectStubs,
  setInput,
  resetFixture,
} from './barcode-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ScansPage from './scans.vue'

describe('barcode pages', () => {
  beforeEach(resetFixture)

  it('resolves the scanned value and shows candidate identity without recording a scan', async () => {
    barcode.permissionCodes.push('business.barcodes.scans.write')
    barcode.resolveBarcode.mockResolvedValue({
      status: 'resolved',
      candidates: [{ objectType: 'work-center', strongIds: { workCenterId: 'WC-01' } }],
      total: 1,
    })
    const wrapper = mount(ScansPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '条码解析查询')!
      .trigger('click')
    const input = wrapper.get('input[aria-label="扫码原文"]')
    await input.setValue(' WC-01 ')
    await wrapper
      .findAll('form')
      .find((form) => form.find('input[aria-label="扫码原文"]').exists())!
      .trigger('submit')
    await flushPromises()

    expect(barcode.resolveBarcode).toHaveBeenCalledWith('WC-01', 1)
    expect(barcode.recordScan).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('已解析')
    expect(wrapper.text()).toContain('工作中心')
    expect(wrapper.text()).toContain('WC-01')
  })

  it('renders scan audit records with workflow filters and business failure copy', async () => {
    barcode.route.query = {
      sourceWorkflow: 'inventory.count',
      sourceDocumentId: 'COUNT-001',
    }
    const wrapper = mount(ScansPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('扫码记录')
    expect(wrapper.text()).toContain('(01)06912345678901(10)L2407')
    expect(wrapper.text()).toContain('库存盘点')
    expect(wrapper.text()).toContain('COUNT-001')
    expect(wrapper.text()).toContain('该扫码场景暂未接入自动业务动作')
    expect(wrapper.text()).toContain('条码解析失败')
    expect(barcode.scanFilters?.sourceWorkflow).toBe('inventory.count')
    expect(barcode.scanFilters?.sourceDocumentId).toBe('COUNT-001')
    expect(barcode.scanFilters?.take).toBe(10)
  })

  it('keys a manual scan audit with gateway-safe characters and reuses the key on retry', async () => {
    // 与 #3922 同类：来源单号原样拼进键，中文或带空格的单号会被网关 400 拒掉。
    barcode.recordScan.mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(undefined)
    const wrapper = mount(ScansPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('补录扫码审计'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-scan-device', 'PC-01')
    await setInput(wrapper, '#barcode-scan-value', '(01)06912345678901(10)L2407')
    await setInput(wrapper, '#barcode-scan-workflow', 'inventory.count')
    await setInput(wrapper, '#barcode-scan-source-id', '期初盘点 一号仓')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const keys = barcode.recordScan.mock.calls.map(([body]) => body.idempotencyKey)
    expect(keys).toHaveLength(2)
    for (const key of keys) expect(key).toMatch(/^[A-Za-z0-9._:/-]+$/)
    expect(keys[1]).toBe(keys[0])
  })

  it('records a manual scan audit attempt without pretending to be PDA scanning', async () => {
    const wrapper = mount(ScansPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('补录扫码审计'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-scan-device', 'PC-01')
    await setInput(wrapper, '#barcode-scan-value', '(01)06912345678901(10)L2407')
    await setInput(wrapper, '#barcode-scan-workflow', 'wms.receiving')
    await setInput(wrapper, '#barcode-scan-source-id', 'IB-001')
    await wrapper.find('select[aria-label="扫码结果"]').setValue('rejected')
    await setInput(wrapper, '#barcode-scan-reason', 'unsupported-workflow')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.recordScan).toHaveBeenCalledWith(
      expect.objectContaining({
        deviceCode: 'PC-01',
        scannedValue: '(01)06912345678901(10)L2407',
        sourceWorkflow: 'wms.receiving',
        sourceDocumentId: 'IB-001',
        result: 'rejected',
        rejectionReason: 'unsupported-workflow',
      }),
    )
  })
})
