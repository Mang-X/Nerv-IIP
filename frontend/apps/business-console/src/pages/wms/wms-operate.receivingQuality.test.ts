import { wms, layoutStub, resetFixture } from './wms-operate.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import InboundPage from './inbound.vue'

describe('WMS operate actions', () => {
  beforeEach(resetFixture)

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
    // 入库单的工厂一并带进上架页（#3832 审核 R3-1）。
    expect(wrapper.get('a[aria-label="受限上架 IB-1"]').attributes('data-to')).toContain('SITE-001')
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

  it('offers no putaway entry once a not-released inbound order is completed', async () => {
    // #3927：已完成的免检单曾留着可点的「上架」，点进去建任务被后端拒。
    wms.inboundStatus = 'Completed'
    wms.isReleasedForPutaway = false
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

    expect(wrapper.find('[aria-label="上架 IB-1"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('入库单已完成，不再提供上架入口。')
    expect(wrapper.text()).not.toContain('可进入上架')
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
})
