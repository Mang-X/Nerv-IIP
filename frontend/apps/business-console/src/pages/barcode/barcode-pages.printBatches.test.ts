import {
  barcode,
  routerLinkStub,
  layoutStub,
  dialogStubs,
  selectStubs,
  setInput,
  COUNT_RULE,
  LOT_TEMPLATE,
  mountPrintBatches,
  openCreateDialog,
  resetFixture,
} from './barcode-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import RulesPage from './rules.vue'
import PrintBatchesPage from './print-batches.vue'
import ScansPage from './scans.vue'

describe('barcode pages', () => {
  beforeEach(resetFixture)

  it('renders print batch list, selected details, and source filters from route context', async () => {
    barcode.route.query = {
      sourceDocumentType: 'production.report',
      sourceDocumentId: 'WO-001',
      printBatchId: 'pb-1',
    }
    const wrapper = mount(PrintBatchesPage, {
      global: {
        stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('打印批次')
    expect(wrapper.text()).toContain('WO-001')
    expect(wrapper.text()).toContain('(01)06912345678901(10)L2407')
    expect(wrapper.text()).toContain('已生成')
    expect(wrapper.text()).toContain('无文件可下载')
    expect(wrapper.text()).not.toContain('file-label-1')
    expect(barcode.printBatchFilters?.sourceDocumentType).toBe('production.report')
    expect(barcode.printBatchFilters?.sourceDocumentId).toBe('WO-001')
    expect(barcode.printBatchFilters?.selectedPrintBatchId).toBe('pb-1')
    expect(barcode.printBatchFilters?.take).toBe(10)
  })

  it('explains the absence of saved files after the batch has printed', async () => {
    barcode.printBatchStatus = 'printed'
    barcode.printBatchFileId = null
    const wrapper = await mountPrintBatches()

    expect(wrapper.text()).toContain('无文件可下载')
    expect(wrapper.text()).toContain('批次已完成，本次未保存可下载的标签文件')
  })

  it('uses business labels instead of internal print identifiers and raw enum values', async () => {
    barcode.printBatchSourceDocumentType = 'purchase-receipt'
    barcode.printBatchStatus = 'printed'
    const wrapper = mount(PrintBatchesPage, {
      global: {
        stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('采购收货')
    expect(wrapper.text()).toContain('已打印')
    expect(wrapper.text()).not.toContain('purchase-receipt')
    expect(wrapper.text()).not.toContain('printed')
    expect(wrapper.text()).not.toContain('pb-1')
    expect(wrapper.text()).not.toContain('tpl-1')
    expect(wrapper.text()).not.toContain('file-label-1')
  })

  it.each([
    ['pending', '待处理'],
    ['reserved', '已预留'],
    ['ready-to-print', '待打印'],
    ['sent-to-printer', '已发送打印机'],
    ['delivery-unknown', '送达待核实'],
    ['printed', '已打印'],
    ['failed', '打印失败'],
  ])('shows print batch status %s in Chinese', async (status, label) => {
    barcode.printBatchStatus = status
    const wrapper = mountPrintBatches()
    await flushPromises()

    const cell = wrapper.findAll('td').map((td) => td.text())
    expect(cell).toContain(label)
    expect(wrapper.text()).not.toContain('其他状态')
  })

  it('guides operators to verify delivery without offering another send', async () => {
    barcode.printBatchStatus = 'delivery-unknown'
    const wrapper = mountPrintBatches()
    await flushPromises()

    const detail = wrapper
      .findAll('section')
      .find((section) => section.text().includes('批次详情'))!
    expect(detail.text()).toContain('请检查打印机作业记录和现场出纸。')
    expect(detail.text()).not.toContain('发送打印')
  })

  it('filters print batches by the status codes the service reports', async () => {
    const wrapper = mountPrintBatches()
    await flushPromises()

    const statusSelect = wrapper
      .findAll('select')
      .find((select) => select.text().includes('全部状态'))!
    const values = statusSelect.findAll('option').map((option) => option.attributes('value'))
    expect(values).toEqual([
      'all',
      'pending',
      'reserved',
      'ready-to-print',
      'sent-to-printer',
      'delivery-unknown',
      'printed',
      'failed',
    ])
    await statusSelect.setValue('sent-to-printer')
    await flushPromises()
    expect(barcode.printBatchFilters?.status).toBe('sent-to-printer')
  })

  it('maps print batch source objects to scan workflow filters when drilling into scans', async () => {
    const wrapper = mount(PrintBatchesPage, {
      global: {
        stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub },
      },
    })
    await flushPromises()

    const scanLink = wrapper
      .findAll('[data-router-link]')
      .find((link) => link.text().includes('扫码记录'))

    expect(scanLink?.attributes('data-to')).toContain('"path":"/barcode/scans"')
    expect(scanLink?.attributes('data-to')).toContain('"sourceWorkflow":"production.report"')
    expect(scanLink?.attributes('data-to')).toContain('"sourceDocumentId":"WO-001"')
  })

  // #3824：生产报工的业务对象一律是工单——打印批次、扫码补录都从工单目录里选，点进去都到同一张工单。
  it('points production-report objects at the same work order from print batches and scans', async () => {
    const stubs = { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub }
    const batches = mount(PrintBatchesPage, { global: { stubs } })
    const scans = mount(ScansPage, { global: { stubs } })
    await flushPromises()

    const workOrderLink = (wrapper: ReturnType<typeof mount>) =>
      wrapper
        .findAll('[data-router-link]')
        .find((link) => link.text() === 'WO-001')
        ?.attributes('data-to')
    expect(workOrderLink(batches)).toBe(JSON.stringify('/mes/work-orders/WO-001'))
    expect(workOrderLink(scans)).toBe(workOrderLink(batches))

    await batches
      .findAll('button')
      .find((b) => b.text().includes('新建打印批次'))!
      .trigger('click')
    await setInput(batches, '#barcode-print-source-type', 'production.report')
    await scans
      .findAll('button')
      .find((b) => b.text().includes('补录扫码审计'))!
      .trigger('click')
    await setInput(scans, '#barcode-scan-workflow', 'production.report')
    await flushPromises()

    expect(batches.find('#barcode-print-source-id').attributes('kind')).toBe('mes-work-order')
    expect(scans.find('#barcode-scan-source-id').attributes('kind')).toBe('mes-work-order')
  })

  // 只有工单能按单号直达；其它类型的目标页找不到那张单据，业务对象只显示文本。
  it.each(['wms.receiving', 'quality.inspection', 'purchase-receipt', 'inventory.count'])(
    'shows %s business objects as plain text instead of a dead-end link',
    async (sourceDocumentType) => {
      barcode.printBatchSourceDocumentType = sourceDocumentType
      const stubs = { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub }
      const batches = mount(PrintBatchesPage, { global: { stubs } })
      const scans = mount(ScansPage, { global: { stubs } })
      await flushPromises()

      const linkTexts = (wrapper: ReturnType<typeof mount>) =>
        wrapper.findAll('[data-router-link]').map((link) => link.text())
      expect(batches.text()).toContain('WO-001')
      expect(linkTexts(batches)).not.toContain('WO-001')
      expect(scans.text()).toContain('IB-001')
      expect(linkTexts(scans)).not.toContain('IB-001')
    },
  )

  it.each(['inventory.receipt', 'inventory.issue'])(
    'keeps %s print batches filtered when drilling into scan records',
    async (sourceDocumentType) => {
      barcode.printBatchSourceDocumentType = sourceDocumentType
      const wrapper = mount(PrintBatchesPage, {
        global: {
          stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub },
        },
      })
      await flushPromises()

      const scanLink = wrapper
        .findAll('[data-router-link]')
        .find((link) => link.text().includes('扫码记录'))

      expect(scanLink?.attributes('data-to')).toContain(`"sourceWorkflow":"${sourceDocumentType}"`)
      expect(scanLink?.attributes('data-to')).toContain('"sourceDocumentId":"WO-001"')
    },
  )

  it('creates a print batch with the rule and label values assembled from the template', async () => {
    barcode.extraRules = [COUNT_RULE]
    const wrapper = mountPrintBatches()
    await openCreateDialog(wrapper)
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await setInput(wrapper, '#barcode-print-source-id', 'COUNT-001')
    await setInput(wrapper, '#barcode-print-quantity', '3')
    await flushPromises()

    // 选模板后按数据项逐项出输入框，只显示中文名。
    expect(wrapper.find('#barcode-print-value-palletGrade').exists()).toBe(true)
    expect(wrapper.find('#barcode-print-value-skuCode').exists()).toBe(true)
    expect(wrapper.text()).toContain('成品编码')
    // 只有一条适用规则时自动选上。
    expect((wrapper.find('#barcode-print-rule').element as HTMLInputElement).value).toBe(
      'rule-count',
    )
    await setInput(wrapper, '#barcode-print-value-palletGrade', 'A 级')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.createPrintBatch).toHaveBeenCalledTimes(1)
    const body = barcode.createPrintBatch.mock.calls[0]![0]
    expect(body).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      barcodeRuleId: 'rule-count',
      labelTemplateId: 'tpl-2',
      sourceDocumentType: 'inventory.count',
      sourceDocumentId: 'COUNT-001',
      labelValuesJson: JSON.stringify({ palletGrade: 'A 级' }),
      requestedQuantity: 3,
      idempotencyKey: expect.any(String),
    })
  })

  it('blocks submission until required label values are filled', async () => {
    barcode.extraRules = [COUNT_RULE]
    const wrapper = mountPrintBatches()
    await openCreateDialog(wrapper)
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await setInput(wrapper, '#barcode-print-source-id', 'COUNT-001')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.createPrintBatch).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('补全标签取值')
  })

  it('blocks a label value longer than the template allows', async () => {
    barcode.extraRules = [COUNT_RULE]
    const wrapper = mountPrintBatches()
    await openCreateDialog(wrapper)
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await setInput(wrapper, '#barcode-print-source-id', 'COUNT-001')
    await setInput(wrapper, '#barcode-print-value-palletGrade', 'A')
    // 模板里「成品编码」最大长度 40。
    await setInput(wrapper, '#barcode-print-value-skuCode', 'S'.repeat(41))
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.createPrintBatch).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('不超过 40 个字')

    await setInput(wrapper, '#barcode-print-value-skuCode', 'S'.repeat(40))
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(barcode.createPrintBatch).toHaveBeenCalledTimes(1)
  })

  it('shows every supported barcode type in Chinese on rules and the rule picker', async () => {
    const gs1Matrix = {
      ...COUNT_RULE,
      barcodeRuleId: 'rule-gs1-dm',
      ruleCode: 'GS1-DM',
      barcodeType: 'gs1-datamatrix',
    }
    barcode.extraRules = [gs1Matrix]
    const rules = mount(RulesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    const ruleRow = rules.findAll('tr').find((row) => row.text().includes('GS1-DM'))!
    expect(ruleRow.text()).toContain('GS1 Data Matrix')
    expect(ruleRow.text()).not.toContain('gs1-datamatrix')

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
    await openCreateDialog(wrapper)
    await wrapper.find('#barcode-print-source-type').setValue('inventory.count')
    await flushPromises()
    await wrapper.find('#barcode-print-rule').trigger('click')
    await flushPromises()
    const option = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]')).find(
      (element) => element.textContent?.includes('GS1-DM'),
    )
    expect(option?.textContent).toContain('GS1 Data Matrix')
    expect(option?.textContent).not.toContain('gs1-datamatrix')
    wrapper.unmount()
  })

  it('offers only active rules that allow the chosen source type', async () => {
    barcode.extraRules = [
      COUNT_RULE,
      { ...COUNT_RULE, barcodeRuleId: 'rule-off', ruleCode: 'OFF', status: 'inactive' },
    ]
    const wrapper = mountPrintBatches()
    await openCreateDialog(wrapper)
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.receipt')
    await flushPromises()

    // inventory.receipt 只有 GS1 规则适用，自动选上；换到盘点后它不再适用，改选盘点规则。
    expect((wrapper.find('#barcode-print-rule').element as HTMLInputElement).value).toBe('rule-1')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await flushPromises()
    expect((wrapper.find('#barcode-print-rule').element as HTMLInputElement).value).toBe(
      'rule-count',
    )
  })

  it('requires a lot number for GS1 rules and fills the source number automatically', async () => {
    barcode.extraTemplates = [LOT_TEMPLATE]
    const wrapper = mountPrintBatches()
    await openCreateDialog(wrapper)
    await setInput(wrapper, '#barcode-print-template', 'tpl-lot')
    await setInput(wrapper, '#barcode-print-source-type', 'production.report')
    await setInput(wrapper, '#barcode-print-source-id', 'WO-009')
    await flushPromises()

    expect((wrapper.find('#barcode-print-rule').element as HTMLInputElement).value).toBe('rule-1')
    // 来源单号由业务对象编号带入，不单独填写。
    expect(wrapper.find('#barcode-print-value-sourceDocumentId').exists()).toBe(false)
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(barcode.createPrintBatch).not.toHaveBeenCalled()

    await setInput(wrapper, '#barcode-print-value-lotNo', 'L2409')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.createPrintBatch).toHaveBeenCalledTimes(1)
    const body = barcode.createPrintBatch.mock.calls[0]![0]
    expect(body.barcodeRuleId).toBe('rule-1')
    expect(JSON.parse(body.labelValuesJson)).toEqual({ lotNo: 'L2409', sourceDocumentId: 'WO-009' })
  })

  it('explains when a GS1 rule meets a template without a lot number', async () => {
    const wrapper = mountPrintBatches()
    await openCreateDialog(wrapper)
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.receipt')
    await setInput(wrapper, '#barcode-print-source-id', 'RC-001')
    await flushPromises()

    expect(wrapper.text()).toContain('这个模板没有「批次号」数据项')
    expect(wrapper.find('#barcode-print-value-palletGrade').exists()).toBe(false)
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(barcode.createPrintBatch).not.toHaveBeenCalled()
  })

  it('leaves inactive templates out of the picker', async () => {
    barcode.extraTemplates = [
      { ...LOT_TEMPLATE, templateId: 'tpl-off', templateName: '停用标签', status: 'inactive' },
    ]
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
    await openCreateDialog(wrapper)
    await wrapper.find('#barcode-print-template').trigger('click')
    await flushPromises()

    const options = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]')).map(
      (element) => element.textContent ?? '',
    )
    expect(options.some((text) => text.includes('外箱标签'))).toBe(true)
    expect(options.some((text) => text.includes('停用标签'))).toBe(false)
    wrapper.unmount()
  })

  // 模板选择器回传的是模板主键（GUID），选中后屏幕上只能出现模板名称 / 编码。
  it('shows the template name and code, never its internal id, after picking a label template', async () => {
    const templateId = '01a0de6f-211c-71c5-83ed-2348ece32398'
    barcode.templateId = templateId
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
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建打印批次'))!
      .trigger('click')
    await wrapper.find('#barcode-print-template').trigger('click')
    await flushPromises()
    const option = Array.from(document.body.querySelectorAll<HTMLElement>('[role="option"]')).find(
      (element) => element.textContent?.includes('外箱标签'),
    )
    expect(option?.textContent).not.toContain(templateId)
    option!.click()
    await flushPromises()

    expect(wrapper.find('#barcode-print-template').text()).toContain('外箱标签')
    expect(document.body.textContent).not.toContain(templateId)
    wrapper.unmount()
  })

  it('reuses the print batch idempotency key while retrying the same dialog submission', async () => {
    barcode.extraRules = [COUNT_RULE]
    barcode.createPrintBatch
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce(undefined)
    const wrapper = mount(PrintBatchesPage, {
      global: {
        stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建打印批次'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await setInput(wrapper, '#barcode-print-source-id', 'COUNT-001')
    await setInput(wrapper, '#barcode-print-quantity', '3')
    await setInput(wrapper, '#barcode-print-value-palletGrade', 'A')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    const firstKey = barcode.createPrintBatch.mock.calls[0][0].idempotencyKey
    const secondKey = barcode.createPrintBatch.mock.calls[1][0].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(secondKey).toBe(firstKey)
  })
})
