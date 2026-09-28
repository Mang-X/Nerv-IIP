import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, inject, provide, reactive, shallowRef } from 'vue'

import RulesPage from './rules.vue'
import TemplatesPage from './templates.vue'
import PrintBatchesPage from './print-batches.vue'
import ScansPage from './scans.vue'

vi.mock('@/components/barcode/TemplateAssetRetirement.vue', () => ({
  default: { template: '<span />' },
}))

const barcode = vi.hoisted(() => ({
  saveRule: vi.fn(),
  saveTemplate: vi.fn(),
  createPrintBatch: vi.fn(),
  recordScan: vi.fn(),
  printBatchSourceDocumentType: 'production.report',
  printBatchStatus: 'ready-to-print',
  templateId: 'tpl-1',
  // 打印批次用例额外追加的规则 / 模板；其它页面的用例保持为空。
  extraRules: [] as Array<Record<string, unknown>>,
  extraTemplates: [] as Array<Record<string, unknown>>,
  route: { query: {} as Record<string, unknown> },
  ruleFilters: undefined as undefined | { keyword?: string; skip: number; take: number },
  templateFilters: undefined as undefined | { skip: number; take: number },
  printBatchFilters: undefined as
    | undefined
    | {
        sourceDocumentType?: string
        sourceDocumentId?: string
        status?: string
        selectedPrintBatchId?: string
        skip: number
        take: number
      },
  scanFilters: undefined as
    | undefined
    | {
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

const routerLinkStub = vi.hoisted(() => ({
  props: ['to'],
  template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
}))

vi.mock('vue-router', () => ({
  RouterLink: routerLinkStub,
  useRoute: () => barcode.route,
}))

vi.mock('@/composables/useBusinessBarcode', () => ({
  useBarcodeRules: () => {
    const filters = reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      skip: 0,
      take: 100,
      keyword: '',
      status: undefined,
    })
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
        ...barcode.extraRules,
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
    const filters = reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      skip: 0,
      take: 100,
      status: undefined,
    })
    barcode.templateFilters = filters
    return {
      filters,
      templates: computed(() => [
        {
          templateId: barcode.templateId,
          templateCode: 'SKU_BOX',
          templateName: '外箱标签',
          templateFileId: 'file-label-box',
          variableSchemaJson: '{"fields":["skuCode","lotNo","expiryDate"]}',
          status: 'active',
        },
        {
          templateId: 'tpl-2',
          templateCode: 'PALLET',
          templateName: '托盘标签',
          templateFileId: 'file-label-pallet',
          variableSchemaJson: JSON.stringify({
            version: 1,
            variables: [
              { name: 'palletGrade', type: 'string' },
              {
                name: 'skuCode',
                label: '成品编码',
                type: 'string',
                required: false,
                maxLength: 40,
              },
            ],
          }),
          status: 'active',
        },
        ...barcode.extraTemplates,
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
          status: barcode.printBatchStatus,
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
        status: barcode.printBatchStatus,
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
        {
          scanRecordId: 'scan-3',
          deviceCode: 'PDA-03',
          scannedValue: '(01)06912345678901(10)WO-001',
          sourceWorkflow: 'production.report',
          sourceDocumentId: 'WO-001',
          result: 'accepted',
          scannedAtUtc: '2026-07-02T03:00:00Z',
        },
      ]),
      scansError: shallowRef(undefined),
      scansPending: shallowRef(false),
      scansTotal: computed(() => 3),
      refreshScans: vi.fn(),
      recordScan: barcode.recordScan,
      recordScanPending: shallowRef(false),
      recordScanError: shallowRef(undefined),
    }
  },
}))

// 扫码补录的设备/终端已改成设备资产主数据选择器；目录 composable 走 pinia + colada，测试给定选项。
vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({
    filters: reactive({ skip: 0, take: 200 }),
    resources: computed(() => [{ code: 'PC-01', displayName: '工位一体机 PC-01', active: true }]),
    resourcesError: shallowRef(undefined),
    resourcesPending: shallowRef(false),
    resourcesTotal: computed(() => 1),
    refreshResources: vi.fn(),
  }),
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
}
/**
 * 表单里的「只选」字段桩：
 * - NvEntityPicker / NvSearchSelect / DirectoryPicker 本身是弹层选择器，桩成带同名 id 的输入位，
 *   让用例继续用 `setInput('#id', ...)` 表达「选中了某个候选」。
 * - NvSelect 的 id 挂在 NvSelectTrigger 上（真实组件是 button），桩件里把它上提到
 *   `<select>` 元素，`#id` 选择器与 `setValue` 语义都保持不变。
 */
const idInputStub = {
  props: ['modelValue', 'options', 'id'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const selectTriggerIdKey = Symbol('nv-select-stub-trigger-id')

const selectStubs = {
  NvEntityPicker: idInputStub,
  NvSearchSelect: idInputStub,
  DirectoryPicker: idInputStub,
  SourceDocumentCatalogPicker: idInputStub,
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    setup() {
      const triggerId = shallowRef<string | undefined>(undefined)
      provide(selectTriggerIdKey, (id?: string) => {
        triggerId.value = id
      })
      return { triggerId }
    },
    template:
      '<select v-bind="$attrs" :id="triggerId ?? $attrs.id" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: {
    props: ['id'],
    setup(props: { id?: string }) {
      const register = inject<((id?: string) => void) | undefined>(selectTriggerIdKey, undefined)
      register?.(props.id)
    },
    template: '<slot />',
  },
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

function setInput(wrapper: ReturnType<typeof mount>, selector: string, value: string) {
  return wrapper.find(selector).setValue(value)
}

const COUNT_RULE = {
  barcodeRuleId: 'rule-count',
  ruleCode: 'COUNT-TAG',
  barcodeType: 'code128',
  prefix: 'CT',
  length: 12,
  checksumRule: 'none',
  gs1CompanyPrefixLength: null,
  allowedSourceDocumentTypes: ['inventory.count'],
  status: 'active',
}

const LOT_TEMPLATE = {
  templateId: 'tpl-lot',
  templateCode: 'LOT_TAG',
  templateName: '批次标签',
  templateFileId: 'file-label-lot',
  variableSchemaJson: JSON.stringify({
    version: 1,
    variables: [
      { name: 'lotNo', type: 'string', required: false, maxLength: 20 },
      { name: 'sourceDocumentId', type: 'string', required: true, maxLength: 150 },
    ],
  }),
  status: 'active',
}

function mountPrintBatches() {
  return mount(PrintBatchesPage, {
    global: {
      stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, RouterLink: routerLinkStub },
    },
  })
}

async function openCreateDialog(wrapper: ReturnType<typeof mount>) {
  await flushPromises()
  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('新建打印批次'))!
    .trigger('click')
  await flushPromises()
}

describe('barcode pages', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    barcode.route.query = {}
    barcode.printBatchSourceDocumentType = 'production.report'
    barcode.printBatchStatus = 'ready-to-print'
    barcode.templateId = 'tpl-1'
    barcode.extraRules = []
    barcode.extraTemplates = []
    barcode.ruleFilters = undefined
    barcode.templateFilters = undefined
    barcode.printBatchFilters = undefined
    barcode.scanFilters = undefined
    barcode.saveRule.mockResolvedValue(undefined)
    barcode.saveTemplate.mockResolvedValue(undefined)
    barcode.createPrintBatch.mockResolvedValue(undefined)
    barcode.recordScan.mockResolvedValue(undefined)
  })

  it('renders rule maintenance with source usage and route-seeded keyword', async () => {
    barcode.route.query = { ruleCode: 'GS1-CASE' }
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('条码规则')
    expect(wrapper.text()).toContain('GS1-CASE')
    expect(wrapper.text()).toContain('GS1 公司前缀 7 位')
    expect(wrapper.text()).toContain('收货入库')
    expect(wrapper.text()).toContain('生产报工')
    expect(barcode.ruleFilters?.keyword).toBe('GS1-CASE')
    expect(barcode.ruleFilters?.take).toBe(10)
  })

  it('blocks GS1 rule submission without company prefix length', async () => {
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...selectStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建规则'))!
      .trigger('click')
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
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...selectStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建规则'))!
      .trigger('click')
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

    expect(barcode.saveRule).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleCode: 'GS1-PALLET',
        barcodeType: 'gs1-128',
        gs1CompanyPrefixLength: 7,
        allowedSourceDocumentTypes: ['inventory.receipt'],
        status: 'active',
      }),
    )
  })

  it('prefills an existing barcode rule for update', async () => {
    const wrapper = mount(RulesPage, {
      global: {
        stubs: {
          ...layoutStub,
          ...dialogStubs,
          ...selectStubs,
          RouterLink: { props: ['to'], template: '<a><slot /></a>' },
        },
      },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('编辑'))!
      .trigger('click')
    await flushPromises()

    // 规则编码由所选行带出，只读展示（不再是 readonly 输入框）。
    const carried = wrapper.find('[data-slot="carried-context"]')
    expect(carried.exists()).toBe(true)
    expect(carried.text()).toContain('GS1-CASE')
    expect(wrapper.find('#barcode-rule-code').exists()).toBe(false)
    expect((wrapper.find('#barcode-rule-prefix').element as HTMLInputElement).value).toBe('0691234')

    await setInput(wrapper, '#barcode-rule-prefix', '0699999')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveRule).toHaveBeenCalledWith(
      expect.objectContaining({
        ruleCode: 'GS1-CASE',
        prefix: '0699999',
        gs1CompanyPrefixLength: 7,
      }),
    )
  })

  it('assembles the variable list from data item rows without showing variable names', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    expect(wrapper.text()).toContain('SKU_BOX')
    expect(wrapper.text()).toContain('物料编码、批次号、有效期')
    // 目录外且没有显示名称的数据项只显示「其他数据项」，变量名不上屏。
    expect(wrapper.text()).toContain('其他数据项、成品编码')
    expect(wrapper.text()).not.toContain('palletGrade')
    expect(wrapper.text()).not.toContain('skuCode')

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建模板'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await setInput(wrapper, '#barcode-template-file', 'file-pallet')
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('添加数据项'))!
      .trigger('click')
    await flushPromises()
    await wrapper.find('#barcode-template-item-1').setValue('lotNo')
    await setInput(wrapper, '#barcode-template-label-1', '来料批次')
    await setInput(wrapper, '#barcode-template-max-1', '30')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        templateCode: 'PALLET_LABEL',
        templateFileId: 'file-pallet',
        variableSchemaJson: JSON.stringify({
          version: 1,
          variables: [
            { name: 'skuCode', label: '物料编码', type: 'string', required: true, maxLength: 200 },
            { name: 'lotNo', label: '来料批次', type: 'string', required: true, maxLength: 30 },
          ],
        }),
      }),
    )
  })

  it('does not submit a template until every data item row is chosen', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建模板'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await setInput(wrapper, '#barcode-template-file', 'file-pallet')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('第 1 行：请选择数据项。')
  })

  it.each([
    ['duplicate data item', 'skuCode', '200', '第 2 行：数据项与前面重复。'],
    ['non-positive max length', 'lotNo', '0', '第 2 行：最大长度需为正整数。'],
  ])('does not submit a template with a %s', async (_case, secondItem, maxLength, message) => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建模板'))!
      .trigger('click')
    await flushPromises()
    await setInput(wrapper, '#barcode-template-code', 'PALLET_LABEL')
    await setInput(wrapper, '#barcode-template-name', '托盘标签')
    await setInput(wrapper, '#barcode-template-file', 'file-pallet')
    await wrapper.find('#barcode-template-item-0').setValue('skuCode')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('添加数据项'))!
      .trigger('click')
    await flushPromises()
    await wrapper.find('#barcode-template-item-1').setValue(secondItem)
    await setInput(wrapper, '#barcode-template-max-1', maxLength)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain(message)
  })

  it('rewrites a legacy field list into the variable list the printer accepts on update', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('编辑'))!
      .trigger('click')
    await flushPromises()

    const carried = wrapper.find('[data-slot="carried-context"]')
    expect(carried.text()).toContain('SKU_BOX')
    expect(wrapper.find('#barcode-template-code').exists()).toBe(false)
    await setInput(wrapper, '#barcode-template-name', '外箱标签 V2')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        templateCode: 'SKU_BOX',
        templateName: '外箱标签 V2',
        templateFileId: 'file-label-box',
        variableSchemaJson: JSON.stringify({
          version: 1,
          variables: [
            { name: 'skuCode', label: '物料编码', type: 'string', required: true, maxLength: 200 },
            { name: 'lotNo', label: '批次号', type: 'string', required: true, maxLength: 200 },
            { name: 'expiryDate', label: '有效期', type: 'string', required: true, maxLength: 200 },
          ],
        }),
      }),
    )
  })

  it('keeps an unlisted data item and its settings when a template is edited', async () => {
    const wrapper = mount(TemplatesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .filter((b) => b.text().includes('编辑'))[1]!
      .trigger('click')
    await flushPromises()
    const firstItem = wrapper.find('#barcode-template-item-0')
    expect(firstItem.text()).toContain('其他数据项')
    expect(firstItem.text()).not.toContain('palletGrade')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(barcode.saveTemplate).toHaveBeenCalledWith(
      expect.objectContaining({
        templateCode: 'PALLET',
        variableSchemaJson: JSON.stringify({
          version: 1,
          variables: [
            { name: 'palletGrade', type: 'string', required: true, maxLength: 200 },
            { name: 'skuCode', label: '成品编码', type: 'string', required: false, maxLength: 40 },
          ],
        }),
      }),
    )
  })

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
    expect(wrapper.text()).not.toContain('file-label-1')
    expect(barcode.printBatchFilters?.sourceDocumentType).toBe('production.report')
    expect(barcode.printBatchFilters?.sourceDocumentId).toBe('WO-001')
    expect(barcode.printBatchFilters?.selectedPrintBatchId).toBe('pb-1')
    expect(barcode.printBatchFilters?.take).toBe(10)
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
      { ...COUNT_RULE, barcodeRuleId: 'rule-off', ruleCode: 'OFF', status: 'disabled' },
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

  it('leaves disabled templates out of the picker', async () => {
    barcode.extraTemplates = [
      { ...LOT_TEMPLATE, templateId: 'tpl-off', templateName: '停用标签', status: 'disabled' },
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
