import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import RulesPage from './rules.vue'
import TemplatesPage from './templates.vue'
import PrintBatchesPage from './print-batches.vue'
import ScansPage from './scans.vue'

const barcode = vi.hoisted(() => ({
  saveRule: vi.fn(),
  saveTemplate: vi.fn(),
  createPrintBatch: vi.fn(),
  recordScan: vi.fn(),
  printBatchSourceDocumentType: 'production.report',
  route: { query: {} as Record<string, unknown> },
  ruleFilters: undefined as undefined | { keyword?: string, skip: number, take: number },
  templateFilters: undefined as undefined | { skip: number, take: number },
  printBatchFilters: undefined as undefined | {
    sourceDocumentType?: string
    sourceDocumentId?: string
    status?: string
    selectedPrintBatchId?: string
    skip: number
    take: number
  },
  scanFilters: undefined as undefined | {
    deviceCode?: string
    scannedValue?: string
    sourceWorkflow?: string
    sourceDocumentId?: string
    skip: number
    take: number
  },
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: vi.fn(), error: vi.fn() },
}))

const routerLinkStub = vi.hoisted(() => ({ props: ['to'], template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>' }))

vi.mock('vue-router', () => ({
  RouterLink: routerLinkStub,
  useRoute: () => barcode.route,
}))

vi.mock('@/composables/useBusinessBarcode', () => ({
  useBarcodeRules: () => {
    const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100, keyword: '', status: undefined })
    barcode.ruleFilters = filters
    return {
      filters,
      rules: computed(() => [
        {
          barcodeRuleId: 'rule-1',
          ruleCode: 'GS1-CASE',
          barcodeType: 'gs1-128',
          prefix: '0691234',
          length: 18,
          checksumRule: 'gs1-mod10',
          gs1CompanyPrefixLength: 7,
          allowedSourceDocumentTypes: ['inventory.receipt', 'production.report'],
          status: 'active',
        },
      ]),
      rulesError: shallowRef(undefined),
      rulesPending: shallowRef(false),
      rulesTotal: computed(() => 1),
      refreshRules: vi.fn(),
      saveRule: barcode.saveRule,
      saveRulePending: shallowRef(false),
      saveRuleError: shallowRef(undefined),
    }
  },
  useBarcodeTemplates: () => {
    const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100, status: undefined })
    barcode.templateFilters = filters
    return {
      filters,
      templates: computed(() => [
        {
          templateId: 'tpl-1',
          templateCode: 'SKU_BOX',
          templateName: '外箱标签',
          templateFileId: 'file-label-box',
          variableSchemaJson: '{"fields":["skuCode","lotNo","expiryDate"]}',
          status: 'active',
        },
      ]),
      templatesError: shallowRef(undefined),
      templatesPending: shallowRef(false),
      templatesTotal: computed(() => 1),
      refreshTemplates: vi.fn(),
      saveTemplate: barcode.saveTemplate,
      saveTemplatePending: shallowRef(false),
      saveTemplateError: shallowRef(undefined),
    }
  },
  useBarcodePrintBatches: () => {
    const filters = reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      skip: 0,
      take: 100,
      sourceDocumentType: undefined as string | undefined,
      sourceDocumentId: undefined as string | undefined,
      status: undefined as string | undefined,
      selectedPrintBatchId: undefined as string | undefined,
    })
    barcode.printBatchFilters = filters
    return {
      filters,
      printBatches: computed(() => [
        {
          printBatchId: 'pb-1',
          labelTemplateId: 'tpl-1',
          sourceDocumentType: barcode.printBatchSourceDocumentType,
          sourceDocumentId: 'WO-001',
          requestedQuantity: 2,
          status: 'completed',
          createdAtUtc: '2026-07-02T01:00:00Z',
        },
      ]),
      printBatchesError: shallowRef(undefined),
      printBatchesPending: shallowRef(false),
      printBatchesTotal: computed(() => 1),
      printBatchDetail: computed(() => ({
        printBatchId: 'pb-1',
        labelTemplateId: 'tpl-1',
        sourceDocumentType: barcode.printBatchSourceDocumentType,
        sourceDocumentId: 'WO-001',
        requestedQuantity: 2,
        status: 'completed',
        items: [
          { sequenceNo: 1, labelValue: '(01)06912345678901(10)L2407', fileId: 'file-label-1' },
          { sequenceNo: 2, labelValue: '(01)06912345678901(10)L2408', fileId: null },
        ],
      })),
      printBatchDetailError: shallowRef(undefined),
      printBatchDetailPending: shallowRef(false),
      refreshPrintBatches: vi.fn(),
      refreshPrintBatchDetail: vi.fn(),
      createPrintBatch: barcode.createPrintBatch,
      createPrintBatchPending: shallowRef(false),
      createPrintBatchError: shallowRef(undefined),
    }
  },
  useBarcodeScans: () => {
    const filters = reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      skip: 0,
      take: 100,
      deviceCode: undefined as string | undefined,
      scannedValue: undefined as string | undefined,
      sourceWorkflow: undefined as string | undefined,
      sourceDocumentId: undefined as string | undefined,
    })
    barcode.scanFilters = filters
    return {
      filters,
      scans: computed(() => [
        {
          scanRecordId: 'scan-1',
          deviceCode: 'PC-01',
          scannedValue: '(01)06912345678901(10)L2407',
          sourceWorkflow: 'inventory.count',
          sourceDocumentId: 'COUNT-001',
          result: 'rejected',
          rejectionReason: 'unsupported-workflow',
          scannedAtUtc: '2026-07-02T01:00:00Z',
        },
        {
          scanRecordId: 'scan-2',
          deviceCode: 'PDA-02',
          scannedValue: 'RAW-NOT-GS1',
          sourceWorkflow: 'wms.receiving',
          sourceDocumentId: 'IB-001',
          result: 'failed',
          rejectionReason: 'parse-failed',
          scannedAtUtc: '2026-07-02T02:00:00Z',
        },
      ]),
      scansError: shallowRef(undefined),
      scansPending: shallowRef(false),
      scansTotal: computed(() => 2),
      refreshScans: vi.fn(),
      recordScan: barcode.recordScan,
      recordScanPending: shallowRef(false),
      recordScanError: shallowRef(undefined),
    }
  },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  DialogPro: { template: '<div><slot /></div>' },
  DialogProTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
}
const selectStubs = {
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select v-bind="$attrs" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<slot />' },
  SelectProValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

function setInput(wrapper: ReturnType<typeof mount>, selector: string, value: string) {
  return wrapper.find(selector).setValue(value)
}

describe('barcode pages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    barcode.route.query = {}
    barcode.printBatchSourceDocumentType = 'production.report'
    barcode.ruleFilters = undefined
    barcode.templateFilters = undefined
    barcode.printBatchFilters = undefined
    barcode.scanFilters = undefined
    barcode.saveRule.mockResolvedValue(undefined)
    barcode.saveTemplate.mockResolvedValue(undefined)
    barcode.createPrintBatch.mockResolvedValue(undefined)
    barcode.recordScan.mockResolvedValue(undefined)
  })

  it('renders rule maintenance with source usage, explicit SKU gap, and route-seeded keyword', async () => {
    barcode.route.query = { ruleCode: 'GS1-CASE' }
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    expect(wrapper.text()).toContain('条码规则')
    expect(wrapper.text()).toContain('GS1-CASE')
    expect(wrapper.text()).toContain('GS1 公司前缀 7 位')
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('生产报工')
    expect(wrapper.text()).toContain('按默认条码规则反查待 SKU facade 支持')
    expect(barcode.ruleFilters?.keyword).toBe('GS1-CASE')
    expect(barcode.ruleFilters?.take).toBe(10)
  })

  it('blocks GS1 rule submission without company prefix length', async () => {
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建规则'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-rule-code', 'GS1-PALLET')
    await setInput(wrapper, '#barcode-rule-prefix', '0691234')
    await setInput(wrapper, '#barcode-rule-length', '18')
    await setInput(wrapper, '#barcode-rule-checksum', 'gs1-mod10')
    await wrapper.find('select[aria-label="条码类型"]').setValue('gs1-128')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('GS1 规则必须填写公司前缀长度。')
  })

  it('submits a valid GS1 rule with source document usage', async () => {
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建规则'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-rule-code', 'GS1-PALLET')
    await setInput(wrapper, '#barcode-rule-prefix', '0691234')
    await setInput(wrapper, '#barcode-rule-length', '18')
    await setInput(wrapper, '#barcode-rule-checksum', 'gs1-mod10')
    await setInput(wrapper, '#barcode-rule-gs1-prefix', '7')
    await wrapper.find('select[aria-label="条码类型"]').setValue('gs1-128')
    await wrapper.find('input[aria-label="适用场景：收货入库"]').setValue(true)
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).toHaveBeenCalledWith(expect.objectContaining({
      ruleCode: 'GS1-PALLET',
      barcodeType: 'gs1-128',
      gs1CompanyPrefixLength: 7,
      allowedSourceDocumentTypes: ['inventory.receipt'],
      status: 'active',
    }))
  })

  it('prefills an existing barcode rule for update', async () => {
    const wrapper = mount(RulesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: { props: ['to'], template: '<a><slot /></a>' } } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('编辑'))!.trigger('click')
    await flushPromises()

    expect((wrapper.find('#barcode-rule-code').element as HTMLInputElement).value).toBe('GS1-CASE')
    expect(wrapper.find('#barcode-rule-code').attributes('readonly')).toBeDefined()
    expect((wrapper.find('#barcode-rule-prefix').element as HTMLInputElement).value).toBe('0691234')

    await setInput(wrapper, '#barcode-rule-prefix', '0699999')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).toHaveBeenCalledWith(expect.objectContaining({
      ruleCode: 'GS1-CASE',
      prefix: '0699999',
      gs1CompanyPrefixLength: 7,
    }))
  })

  it('renders and saves a label template with field schema text', async () => {
    const wrapper = mount(TemplatesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    expect(wrapper.text()).toContain('标签模板')
    expect(wrapper.text()).toContain('SKU_BOX')
    expect(wrapper.text()).toContain('skuCode')
    expect(wrapper.text()).toContain('适用对象')

    await wrapper.findAll('button').find((b) => b.text().includes('新建模板'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await setInput(wrapper, '#barcode-template-file', 'file-pallet')
    await setInput(wrapper, '#barcode-template-schema', '{"fields":["sscc","lotNo"]}')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(expect.objectContaining({
      templateCode: 'PALLET_LABEL',
      templateName: '托盘标签',
      templateFileId: 'file-pallet',
      variableSchemaJson: '{"fields":["sscc","lotNo"]}',
      status: 'active',
    }))
  })

  it('prefills an existing label template for update', async () => {
    const wrapper = mount(TemplatesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('编辑'))!.trigger('click')
    await flushPromises()

    expect((wrapper.find('#barcode-template-code').element as HTMLInputElement).value).toBe('SKU_BOX')
    expect(wrapper.find('#barcode-template-code').attributes('readonly')).toBeDefined()
    expect((wrapper.find('#barcode-template-file').element as HTMLInputElement).value).toBe('file-label-box')

    await setInput(wrapper, '#barcode-template-name', '外箱标签 V2')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(expect.objectContaining({
      templateCode: 'SKU_BOX',
      templateName: '外箱标签 V2',
      templateFileId: 'file-label-box',
    }))
  })

  it('renders print batch list, selected details, and source filters from route context', async () => {
    barcode.route.query = {
      sourceDocumentType: 'production.report',
      sourceDocumentId: 'WO-001',
      printBatchId: 'pb-1',
    }
    const wrapper = mount(PrintBatchesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('打印批次')
    expect(wrapper.text()).toContain('WO-001')
    expect(wrapper.text()).toContain('(01)06912345678901(10)L2407')
    expect(wrapper.text()).toContain('file-label-1')
    expect(barcode.printBatchFilters?.sourceDocumentType).toBe('production.report')
    expect(barcode.printBatchFilters?.sourceDocumentId).toBe('WO-001')
    expect(barcode.printBatchFilters?.selectedPrintBatchId).toBe('pb-1')
    expect(barcode.printBatchFilters?.take).toBe(10)
  })

  it('maps print batch source objects to scan workflow filters when drilling into scans', async () => {
    const wrapper = mount(PrintBatchesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub } } })
    await flushPromises()

    const scanLink = wrapper.findAll('[data-router-link]').find((link) => link.text().includes('扫码记录'))

    expect(scanLink?.attributes('data-to')).toContain('"path":"/barcode/scans"')
    expect(scanLink?.attributes('data-to')).toContain('"sourceWorkflow":"production.report"')
    expect(scanLink?.attributes('data-to')).toContain('"sourceDocumentId":"WO-001"')
  })

  it.each(['inventory.receipt', 'inventory.issue'])('keeps %s print batches filtered when drilling into scan records', async (sourceDocumentType) => {
    barcode.printBatchSourceDocumentType = sourceDocumentType
    const wrapper = mount(PrintBatchesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub } } })
    await flushPromises()

    const scanLink = wrapper.findAll('[data-router-link]').find((link) => link.text().includes('扫码记录'))

    expect(scanLink?.attributes('data-to')).toContain(`"sourceWorkflow":"${sourceDocumentType}"`)
    expect(scanLink?.attributes('data-to')).toContain('"sourceDocumentId":"WO-001"')
  })

  it('creates a print batch with template, source object, and quantity', async () => {
    const wrapper = mount(PrintBatchesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建打印批次'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await setInput(wrapper, '#barcode-print-source-id', 'COUNT-001')
    await setInput(wrapper, '#barcode-print-quantity', '3')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.createPrintBatch).toHaveBeenCalledWith(expect.objectContaining({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      labelTemplateId: 'tpl-2',
      sourceDocumentType: 'inventory.count',
      sourceDocumentId: 'COUNT-001',
      requestedQuantity: 3,
    }))
  })

  it('reuses the print batch idempotency key while retrying the same dialog submission', async () => {
    barcode.createPrintBatch.mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(undefined)
    const wrapper = mount(PrintBatchesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建打印批次'))!.trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-print-template', 'tpl-2')
    await setInput(wrapper, '#barcode-print-source-type', 'inventory.count')
    await setInput(wrapper, '#barcode-print-source-id', 'COUNT-001')
    await setInput(wrapper, '#barcode-print-quantity', '3')
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

  it('renders scan audit records with workflow filters and business failure copy', async () => {
    barcode.route.query = {
      sourceWorkflow: 'inventory.count',
      sourceDocumentId: 'COUNT-001',
    }
    const wrapper = mount(ScansPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
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

  it('records a manual scan audit attempt without pretending to be PDA scanning', async () => {
    const wrapper = mount(ScansPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('补录扫码审计'))!.trigger('click')
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

    expect(barcode.recordScan).toHaveBeenCalledWith(expect.objectContaining({
      deviceCode: 'PC-01',
      scannedValue: '(01)06912345678901(10)L2407',
      sourceWorkflow: 'wms.receiving',
      sourceDocumentId: 'IB-001',
      result: 'rejected',
      rejectionReason: 'unsupported-workflow',
    }))
  })
})
