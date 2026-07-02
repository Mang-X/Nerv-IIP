import {
  createBusinessConsoleBarcodePrintBatchMutationOptions,
  createOrUpdateBusinessConsoleBarcodeRuleMutationOptions,
  createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions,
  getBusinessConsoleBarcodePrintBatchQueryOptions,
  listBusinessConsoleBarcodePrintBatchesQueryOptions,
  listBusinessConsoleBarcodeRulesQueryOptions,
  listBusinessConsoleBarcodeScansQueryOptions,
  listBusinessConsoleBarcodeTemplatesQueryOptions,
  recordBusinessConsoleBarcodeScanMutationOptions,
  type BusinessConsoleBarcodePrintBatchDetail,
  type BusinessConsoleBarcodePrintBatchEnvelope,
  type BusinessConsoleBarcodePrintBatchItem,
  type BusinessConsoleBarcodePrintBatchListEnvelope,
  type BusinessConsoleBarcodeRuleItem,
  type BusinessConsoleBarcodeRuleListEnvelope,
  type BusinessConsoleBarcodeScanListEnvelope,
  type BusinessConsoleBarcodeScanRecordItem,
  type BusinessConsoleBarcodeTemplateItem,
  type BusinessConsoleBarcodeTemplateListEnvelope,
  type CreateBusinessConsoleBarcodePrintBatchData,
  type CreateOrUpdateBusinessConsoleBarcodeRuleData,
  type CreateOrUpdateBusinessConsoleBarcodeTemplateData,
  type RecordBusinessConsoleBarcodeScanData,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { bindBusinessContext, withBusinessContextEnabled, type BusinessContextFields } from './businessContextBinding'

const DEFAULT_TAKE = 100

export interface BarcodeListFilters extends BusinessContextFields {
  skip: number
  take: number
  status?: string
}

export interface BarcodeRuleFilters extends BarcodeListFilters {
  keyword?: string
}

export interface BarcodePrintBatchFilters extends BarcodeListFilters {
  sourceDocumentType?: string
  sourceDocumentId?: string
  selectedPrintBatchId?: string
}

export interface BarcodeScanFilters extends BarcodeListFilters {
  deviceCode?: string
  scannedValue?: string
  sourceWorkflow?: string
  sourceDocumentId?: string
}

function defaultFilters<T extends BarcodeListFilters>(initial: Partial<T> = {}): T {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  }) as T)
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function ruleItems(envelope: BusinessConsoleBarcodeRuleListEnvelope | undefined) {
  if (!envelope?.success) return []
  return envelope.data?.rules ?? []
}

function templateItems(envelope: BusinessConsoleBarcodeTemplateListEnvelope | undefined) {
  if (!envelope?.success) return []
  return envelope.data?.templates ?? []
}

function printBatchItems(envelope: BusinessConsoleBarcodePrintBatchListEnvelope | undefined) {
  if (!envelope?.success) return []
  return envelope.data?.printBatches ?? []
}

function printBatchDetail(envelope: BusinessConsoleBarcodePrintBatchEnvelope | undefined) {
  if (!envelope?.success) return undefined
  return envelope.data?.printBatch ?? undefined
}

function scanItems(envelope: BusinessConsoleBarcodeScanListEnvelope | undefined) {
  if (!envelope?.success) return []
  return envelope.data?.scans ?? []
}

function total(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}

export function useBarcodeRules(initialFilters: Partial<BarcodeRuleFilters> = {}) {
  const filters = defaultFilters<BarcodeRuleFilters>(initialFilters)
  const rulesQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleBarcodeRulesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('status', filters.status),
        ...optionalQuery('keyword', filters.keyword),
      },
    }), filters),
  )

  const saveRuleMutation = useMutation({
    ...createOrUpdateBusinessConsoleBarcodeRuleMutationOptions(),
    onSuccess() {
      void rulesQuery.refetch()
    },
  })

  return {
    filters,
    rules: computed<BusinessConsoleBarcodeRuleItem[]>(() => ruleItems(rulesQuery.data.value)),
    rulesError: rulesQuery.error,
    rulesPending: rulesQuery.isLoading,
    rulesTotal: computed(() => total(rulesQuery.data.value)),
    refreshRules: rulesQuery.refetch,
    saveRule: (body: CreateOrUpdateBusinessConsoleBarcodeRuleData['body']) =>
      saveRuleMutation.mutateAsync({ body }),
    saveRulePending: saveRuleMutation.isLoading,
    saveRuleError: saveRuleMutation.error,
  }
}

export function useBarcodeTemplates(initialFilters: Partial<BarcodeListFilters> = {}) {
  const filters = defaultFilters<BarcodeListFilters>(initialFilters)
  const templatesQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleBarcodeTemplatesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('status', filters.status),
      },
    }), filters),
  )

  const saveTemplateMutation = useMutation({
    ...createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions(),
    onSuccess() {
      void templatesQuery.refetch()
    },
  })

  return {
    filters,
    templates: computed<BusinessConsoleBarcodeTemplateItem[]>(() => templateItems(templatesQuery.data.value)),
    templatesError: templatesQuery.error,
    templatesPending: templatesQuery.isLoading,
    templatesTotal: computed(() => total(templatesQuery.data.value)),
    refreshTemplates: templatesQuery.refetch,
    saveTemplate: (body: CreateOrUpdateBusinessConsoleBarcodeTemplateData['body']) =>
      saveTemplateMutation.mutateAsync({ body }),
    saveTemplatePending: saveTemplateMutation.isLoading,
    saveTemplateError: saveTemplateMutation.error,
  }
}

export function useBarcodePrintBatches(initialFilters: Partial<BarcodePrintBatchFilters> = {}) {
  const filters = defaultFilters<BarcodePrintBatchFilters>(initialFilters)
  const printBatchesQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleBarcodePrintBatchesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('sourceDocumentType', filters.sourceDocumentType),
        ...optionalQuery('sourceDocumentId', filters.sourceDocumentId),
        ...optionalQuery('status', filters.status),
      },
    }), filters),
  )

  const printBatchDetailQuery = useQuery(() => ({
    ...withBusinessContextEnabled(getBusinessConsoleBarcodePrintBatchQueryOptions({
      path: {
        printBatchId: filters.selectedPrintBatchId ?? '',
      },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }), filters),
    enabled: withBusinessContextEnabled({}, filters).enabled && !!filters.selectedPrintBatchId,
  }))

  const createPrintBatchMutation = useMutation({
    ...createBusinessConsoleBarcodePrintBatchMutationOptions(),
    onSuccess() {
      void printBatchesQuery.refetch()
      if (filters.selectedPrintBatchId) void printBatchDetailQuery.refetch()
    },
  })

  return {
    filters,
    printBatches: computed<BusinessConsoleBarcodePrintBatchItem[]>(() => printBatchItems(printBatchesQuery.data.value)),
    printBatchesError: printBatchesQuery.error,
    printBatchesPending: printBatchesQuery.isLoading,
    printBatchesTotal: computed(() => total(printBatchesQuery.data.value)),
    printBatchDetail: computed<BusinessConsoleBarcodePrintBatchDetail | undefined>(() => printBatchDetail(printBatchDetailQuery.data.value)),
    printBatchDetailError: printBatchDetailQuery.error,
    printBatchDetailPending: printBatchDetailQuery.isLoading,
    refreshPrintBatches: printBatchesQuery.refetch,
    refreshPrintBatchDetail: printBatchDetailQuery.refetch,
    createPrintBatch: (body: CreateBusinessConsoleBarcodePrintBatchData['body']) =>
      createPrintBatchMutation.mutateAsync({ body }),
    createPrintBatchPending: createPrintBatchMutation.isLoading,
    createPrintBatchError: createPrintBatchMutation.error,
  }
}

export function useBarcodeScans(initialFilters: Partial<BarcodeScanFilters> = {}) {
  const filters = defaultFilters<BarcodeScanFilters>(initialFilters)
  const scansQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleBarcodeScansQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('deviceCode', filters.deviceCode),
        ...optionalQuery('scannedValue', filters.scannedValue),
        ...optionalQuery('sourceWorkflow', filters.sourceWorkflow),
        ...optionalQuery('sourceDocumentId', filters.sourceDocumentId),
      },
    }), filters),
  )

  const recordScanMutation = useMutation({
    ...recordBusinessConsoleBarcodeScanMutationOptions(),
    onSuccess() {
      void scansQuery.refetch()
    },
  })

  return {
    filters,
    scans: computed<BusinessConsoleBarcodeScanRecordItem[]>(() => scanItems(scansQuery.data.value)),
    scansError: scansQuery.error,
    scansPending: scansQuery.isLoading,
    scansTotal: computed(() => total(scansQuery.data.value)),
    refreshScans: scansQuery.refetch,
    recordScan: (body: RecordBusinessConsoleBarcodeScanData['body']) =>
      recordScanMutation.mutateAsync({ body }),
    recordScanPending: recordScanMutation.isLoading,
    recordScanError: recordScanMutation.error,
  }
}
