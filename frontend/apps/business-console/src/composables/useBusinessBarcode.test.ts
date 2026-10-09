import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  createBusinessConsoleBarcodePrintBatchMutationOptions,
  createOrUpdateBusinessConsoleBarcodeRuleMutationOptions,
  createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions,
  getBusinessConsoleBarcodePrintBatchQueryOptions,
  listBusinessConsoleBarcodePrintersQueryOptions,
  listBusinessConsoleBarcodePrintBatchesQueryOptions,
  listBusinessConsoleBarcodeRulesQueryOptions,
  listBusinessConsoleBarcodeScansQueryOptions,
  listBusinessConsoleBarcodeTemplatesQueryOptions,
  recordBusinessConsoleBarcodeScanMutationOptions,
} from '@nerv-iip/api-client'
import {
  useBarcodePrinters,
  useBarcodePrintBatches,
  useBarcodeRules,
  useBarcodeScans,
  useBarcodeTemplates,
} from './useBusinessBarcode'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  mutations: [] as ReturnType<typeof vi.fn>[],
  mutationOptions: [] as Array<{
    onSuccess?: () => Promise<void> | void
    onSettled?: () => Promise<void> | void
  }>,
  refetches: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@/composables/businessContextBinding', () => ({
  bindBusinessContext: <T extends { organizationId: string; environmentId: string }>(
    filters: T,
  ) => {
    filters.organizationId = 'org-001'
    filters.environmentId = 'env-dev'
    return filters
  },
  withBusinessContextEnabled: (options: object) => options,
  hasBusinessContext: (filters: { organizationId: string; environmentId: string }) =>
    !!filters.organizationId && !!filters.environmentId,
  refetchWithBusinessContext: (_filters: object, query: { refetch: () => Promise<void> }) =>
    query.refetch(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleBarcodePrintersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodePrinters' }],
    query: vi.fn(),
  })),
  dispatchBusinessConsoleBarcodePrintBatchMutationOptions: vi.fn(() => ({})),
  listBusinessConsoleBarcodeRulesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodeRules' }],
    query: vi.fn(),
  })),
  listBusinessConsoleBarcodeTemplatesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodeTemplates' }],
    query: vi.fn(),
  })),
  listBusinessConsoleBarcodePrintBatchesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodePrintBatches' }],
    query: vi.fn(),
  })),
  getBusinessConsoleBarcodePrintBatchQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleBarcodePrintBatch' }],
    query: vi.fn(),
  })),
  listBusinessConsoleBarcodeScansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodeScans' }],
    query: vi.fn(),
  })),
  createOrUpdateBusinessConsoleBarcodeRuleMutationOptions: vi.fn(() => ({})),
  createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions: vi.fn(() => ({})),
  confirmBusinessConsoleBarcodePrintBatchMutationOptions: vi.fn(() => ({})),
  createBusinessConsoleBarcodePrintBatchMutationOptions: vi.fn(() => ({})),
  recordBusinessConsoleBarcodeScanMutationOptions: vi.fn(() => ({})),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''

    const refetch = vi.fn()
    coladaState.refetches.set(id, refetch)
    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
  useMutation: vi.fn((options) => {
    coladaState.mutationOptions.push(options)
    const mutateAsync = vi.fn().mockResolvedValue({ data: { id: 'ok' } })
    coladaState.mutations.push(mutateAsync)
    return {
      mutateAsync,
      isLoading: shallowRef(false),
      error: shallowRef(),
    }
  }),
  useQueryCache: vi.fn(() => ({ invalidateQueries: vi.fn().mockResolvedValue(undefined) })),
}))

describe('business barcode composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.mutations.length = 0
    coladaState.mutationOptions.length = 0
    coladaState.refetches.clear()
  })

  it('confirms the selected batch in the current scope and refetches both persisted views', async () => {
    const result = useBarcodePrintBatches({ selectedPrintBatchId: 'pb-1' })
    await result.confirmPrintBatch('pb-1')
    expect(coladaState.mutations[1]).toHaveBeenCalledWith({
      path: { printBatchId: 'pb-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
    await coladaState.mutationOptions[1]!.onSuccess!()
    expect(coladaState.refetches.get('listBusinessConsoleBarcodePrintBatches')).toHaveBeenCalled()
    expect(coladaState.refetches.get('getBusinessConsoleBarcodePrintBatch')).toHaveBeenCalled()
  })

  it('reads configured printers through the scoped facade', () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodePrinters', {
      success: true,
      data: { printers: [{ printerId: 'printer-01', name: '包装标签打印机' }] },
    })
    const result = useBarcodePrinters()
    expect(listBusinessConsoleBarcodePrintersQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
    expect(result.printers.value).toEqual([{ printerId: 'printer-01', name: '包装标签打印机' }])
  })

  it('dispatches to the selected printer and rereads both persisted views when the mutation settles', async () => {
    const result = useBarcodePrintBatches({ selectedPrintBatchId: 'pb-1' })
    await result.dispatchPrintBatch('pb-1', 'printer-01')
    expect(coladaState.mutations[2]).toHaveBeenCalledWith({
      path: { printBatchId: 'pb-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: { printerId: 'printer-01' },
    })
    await coladaState.mutationOptions[2]!.onSettled!()
    expect(coladaState.refetches.get('listBusinessConsoleBarcodePrintBatches')).toHaveBeenCalled()
    expect(coladaState.refetches.get('getBusinessConsoleBarcodePrintBatch')).toHaveBeenCalled()
  })

  it('lists barcode rules with context, status, keyword, paging, and total', () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodeRules', {
      success: true,
      data: {
        total: 2,
        rules: [
          {
            barcodeRuleId: 'rule-1',
            ruleCode: 'GS1-CASE',
            barcodeType: 'gs1-128',
            status: 'active',
          },
        ],
      },
    })

    const result = useBarcodeRules({ status: 'active', keyword: 'GS1', skip: 20, take: 20 })

    expect(listBusinessConsoleBarcodeRulesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 20,
        take: 20,
        status: 'active',
        keyword: 'GS1',
      },
    })
    expect(result.rules.value).toEqual([
      { barcodeRuleId: 'rule-1', ruleCode: 'GS1-CASE', barcodeType: 'gs1-128', status: 'active' },
    ])
    expect(result.rulesTotal.value).toBe(2)
  })

  it('creates or updates a barcode rule through the stable mutation options', async () => {
    const result = useBarcodeRules()

    await result.saveRule({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      ruleCode: 'GS1-CASE',
      barcodeType: 'gs1-128',
      prefix: '0691234',
      length: 18,
      checksumRule: 'gs1-mod10',
      allowedSourceDocumentTypes: ['inventory.receipt'],
      status: 'active',
      gs1CompanyPrefixLength: 7,
    })

    expect(createOrUpdateBusinessConsoleBarcodeRuleMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      body: expect.objectContaining({
        ruleCode: 'GS1-CASE',
        gs1CompanyPrefixLength: 7,
      }),
    })
  })

  it('lists and saves label templates through the barcode facade', async () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodeTemplates', {
      success: true,
      data: {
        total: 1,
        templates: [
          {
            templateId: 'tpl-1',
            templateCode: 'SKU_BOX',
            templateName: '外箱标签',
            status: 'active',
          },
        ],
      },
    })

    const result = useBarcodeTemplates({ status: 'active', skip: 10, take: 10 })

    expect(listBusinessConsoleBarcodeTemplatesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 10,
        take: 10,
        status: 'active',
      },
    })
    expect(result.templates.value).toHaveLength(1)
    expect(result.templatesTotal.value).toBe(1)

    await result.saveTemplate({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      templateCode: 'SKU_BOX',
      templateName: '外箱标签',
      templateFileId: 'file-label-box',
      variableSchemaJson: '{"fields":["skuCode","lotNo"]}',
      status: 'active',
    })

    expect(createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      body: expect.objectContaining({ templateCode: 'SKU_BOX', templateFileId: 'file-label-box' }),
    })
  })

  it('lists print batches, loads selected details, and creates a batch through the facade', async () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodePrintBatches', {
      success: true,
      data: {
        total: 1,
        printBatches: [
          {
            printBatchId: 'pb-1',
            labelTemplateId: 'tpl-1',
            sourceDocumentType: 'production.report',
            sourceDocumentId: 'WO-001',
            requestedQuantity: 2,
            status: 'completed',
          },
        ],
      },
    })
    coladaState.queryDataById.set('getBusinessConsoleBarcodePrintBatch', {
      success: true,
      data: {
        printBatch: {
          printBatchId: 'pb-1',
          labelTemplateId: 'tpl-1',
          sourceDocumentType: 'production.report',
          sourceDocumentId: 'WO-001',
          requestedQuantity: 2,
          status: 'completed',
          items: [
            { sequenceNo: 1, labelValue: '(01)06912345678901(10)L2407', fileId: 'file-label-1' },
          ],
        },
      },
    })

    const result = useBarcodePrintBatches({
      sourceDocumentType: 'production.report',
      sourceDocumentId: 'WO-001',
      status: 'completed',
      selectedPrintBatchId: 'pb-1',
      skip: 0,
      take: 20,
    })

    expect(listBusinessConsoleBarcodePrintBatchesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 20,
        sourceDocumentType: 'production.report',
        sourceDocumentId: 'WO-001',
        status: 'completed',
      },
    })
    expect(getBusinessConsoleBarcodePrintBatchQueryOptions).toHaveBeenCalledWith({
      path: { printBatchId: 'pb-1' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
    expect(result.printBatches.value).toHaveLength(1)
    expect(result.printBatchDetail.value?.items?.[0]?.labelValue).toContain('(01)')

    await result.createPrintBatch({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      labelTemplateId: 'tpl-1',
      sourceDocumentType: 'production.report',
      sourceDocumentId: 'WO-001',
      idempotencyKey: 'print-WO-001-1',
      requestedQuantity: 2,
    })

    expect(createBusinessConsoleBarcodePrintBatchMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      body: expect.objectContaining({ labelTemplateId: 'tpl-1', requestedQuantity: 2 }),
    })
  })

  it('lists scan records with workflow and object filters and records scan audit actions', async () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodeScans', {
      success: true,
      data: {
        total: 1,
        scans: [
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
        ],
      },
    })

    const result = useBarcodeScans({
      sourceWorkflow: 'inventory.count',
      sourceDocumentId: 'COUNT-001',
      scannedValue: '(01)',
      deviceCode: 'PC-01',
      skip: 40,
      take: 20,
    })

    expect(listBusinessConsoleBarcodeScansQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 40,
        take: 20,
        deviceCode: 'PC-01',
        scannedValue: '(01)',
        sourceWorkflow: 'inventory.count',
        sourceDocumentId: 'COUNT-001',
      },
    })
    expect(result.scans.value[0]?.rejectionReason).toBe('unsupported-workflow')
    expect(result.scansTotal.value).toBe(1)

    await result.recordScan({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      deviceCode: 'PC-01',
      scannedValue: '(01)06912345678901(10)L2407',
      sourceWorkflow: 'inventory.count',
      sourceDocumentId: 'COUNT-001',
      idempotencyKey: 'scan-COUNT-001-1',
      result: 'rejected',
      rejectionReason: 'unsupported-workflow',
    })

    expect(recordBusinessConsoleBarcodeScanMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      body: expect.objectContaining({
        sourceWorkflow: 'inventory.count',
        sourceDocumentId: 'COUNT-001',
        rejectionReason: 'unsupported-workflow',
      }),
    })
  })
})
