import { flushPromises, mount } from '@vue/test-utils'
import { vi } from 'vitest'
import { computed, inject, provide, reactive, shallowRef } from 'vue'
import PrintBatchesPage from './print-batches.vue'

vi.mock('@/components/barcode/TemplateAssetRetirement.vue', () => ({
  default: { template: '<span />' },
}))

// 上传三跳（建会话 / tus / complete）由 templateAssetUpload 自己的用例覆盖；页面用例只看弹窗怎么用它。
vi.mock('@/components/barcode/templateAssetUpload', async (orig) => ({
  ...(await orig<typeof import('@/components/barcode/templateAssetUpload')>()),
  uploadTemplateAsset: barcode.uploadTemplateAsset,
}))

const barcode = vi.hoisted(() => ({
  saveRule: vi.fn(),
  saveTemplate: vi.fn(),
  createPrintBatch: vi.fn(),
  confirmPrintBatch: vi.fn(),
  dispatchPrintBatch: vi.fn(),
  previewPrintBatch: vi.fn(),
  resolveBarcode: vi.fn(),
  dispatchPrintBatchPending: false,
  permissionCodes: ['business.barcodes.print'],
  uploadTemplateAsset: vi.fn(),
  recordScan: vi.fn(),
  printBatchSourceDocumentType: 'production.report',
  printBatchStatus: 'ready-to-print',
  printBatchFileId: 'file-label-1' as string | null,
  confirmPrintBatchPending: false,
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

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: barcode.permissionCodes } }),
}))

const toastMock = vi.hoisted(() => ({ success: vi.fn(), warning: vi.fn(), error: vi.fn() }))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: toastMock,
}))

const routerLinkStub = vi.hoisted(() => ({
  props: ['to'],
  template: '<a data-router-link :data-to="JSON.stringify(to)"><slot /></a>',
}))

vi.mock('vue-router', () => ({
  RouterLink: routerLinkStub,
  useRoute: () => barcode.route,
}))

vi.mock('@/composables/barcode/useBusinessBarcode', () => ({
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
  useBarcodePrinters: () => ({
    printers: computed(() => [{ printerId: 'printer-01', name: '包装标签打印机' }]),
    printersPending: shallowRef(false),
    printersError: shallowRef(undefined),
  }),
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
        completedAtUtc: '2026-10-10T01:23:45Z',
        labelTemplateId: 'tpl-1',
        sourceDocumentType: barcode.printBatchSourceDocumentType,
        sourceDocumentId: 'WO-001',
        requestedQuantity: 2,
        status: barcode.printBatchStatus,
        items: [
          {
            sequenceNo: 1,
            labelValue: '(01)06912345678901(10)L2407',
            fileId: barcode.printBatchFileId,
          },
          { sequenceNo: 2, labelValue: '(01)06912345678901(10)L2408', fileId: null },
        ],
      })),
      printBatchDetailError: shallowRef(undefined),
      printBatchDetailPending: shallowRef(false),
      refreshPrintBatches: vi.fn(),
      refreshPrintBatchDetail: vi.fn(),
      dispatchPrintBatch: barcode.dispatchPrintBatch,
      previewPrintBatch: barcode.previewPrintBatch,
      dispatchPrintBatchPending: shallowRef(barcode.dispatchPrintBatchPending),
      confirmPrintBatch: barcode.confirmPrintBatch,
      confirmPrintBatchPending: shallowRef(barcode.confirmPrintBatchPending),
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
      resolveBarcode: barcode.resolveBarcode,
      resolveBarcodePending: shallowRef(false),
      recordScanPending: shallowRef(false),
      recordScanError: shallowRef(undefined),
    }
  },
}))

// 扫码补录的设备/终端已改成设备资产主数据选择器；目录 composable 走 pinia + colada，测试给定选项。
vi.mock('@/composables/master-data', () => ({
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

async function uploadTemplateFile(wrapper: ReturnType<typeof mount>, name = 'pallet-label.json') {
  const input = wrapper.find('[data-testid="barcode-template-file-input"]')
  const file = new File(['{"format":"nerv-iip.label-template"}'], name, {
    type: 'application/json',
  })
  Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
  await input.trigger('change')
  await flushPromises()
  return file
}

async function openNewTemplate(wrapper: ReturnType<typeof mount>) {
  await flushPromises()
  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('新建模板'))!
    .trigger('click')
  await flushPromises()
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

const resetFixture = () => {
  vi.clearAllMocks()
  barcode.route.query = {}
  barcode.printBatchSourceDocumentType = 'production.report'
  barcode.printBatchStatus = 'ready-to-print'
  barcode.printBatchFileId = 'file-label-1'
  barcode.confirmPrintBatchPending = false
  barcode.dispatchPrintBatchPending = false
  barcode.previewPrintBatch.mockReset()
  barcode.resolveBarcode.mockReset()
  barcode.dispatchPrintBatch.mockResolvedValue({ acknowledged: true })
  barcode.templateId = 'tpl-1'
  barcode.extraRules = []
  barcode.extraTemplates = []
  barcode.ruleFilters = undefined
  barcode.templateFilters = undefined
  barcode.printBatchFilters = undefined
  barcode.scanFilters = undefined
  barcode.saveRule.mockResolvedValue(undefined)
  barcode.saveTemplate.mockResolvedValue(undefined)
  barcode.permissionCodes = ['business.barcodes.print']
  barcode.confirmPrintBatch.mockResolvedValue(undefined)
  barcode.createPrintBatch.mockResolvedValue(undefined)
  barcode.recordScan.mockResolvedValue(undefined)
  barcode.uploadTemplateAsset.mockResolvedValue({
    fileId: 'file-pallet',
    fileName: 'pallet-label.json',
    sizeBytes: 363,
  })
}

export {
  barcode,
  toastMock,
  routerLinkStub,
  layoutStub,
  dialogStubs,
  selectStubs,
  mountPrintBatches,
  resetFixture,
  setInput,
  uploadTemplateFile,
  openNewTemplate,
  COUNT_RULE,
  LOT_TEMPLATE,
  openCreateDialog,
}
