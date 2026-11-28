import type { BusinessConsoleQualityNcrItem } from '@nerv-iip/api-client'
import { mount } from '@vue/test-utils'
import { vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/master-data/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/master-data/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/master-data/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const routeState = vi.hoisted(() => ({
  route: undefined as { query: Record<string, string> } | undefined,
}))
const authState = vi.hoisted(() => ({
  permissionCodes: [
    'business.quality.inspection-records.create',
    'business.quality.ncr.read',
    'business.quality.ncr.manage',
    'business.mes.work-orders.read',
  ],
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      principalId: 'qa-user-001',
      loginName: 'qa-user',
      permissionCodes: authState.permissionCodes,
    },
  }),
}))

const notifySpies = vi.hoisted(() => ({
  error: vi.fn(),
  operationFailure: vi.fn(),
  success: vi.fn(),
}))
const taskActionSpies = vi.hoisted(() => ({
  startInspection: vi.fn(),
  refreshInspectionTasks: vi.fn(),
}))
const createRecordSpy = vi.hoisted(() => vi.fn())
const qualityTaskRead = vi.hoisted(() => vi.fn())
const evidenceApi = vi.hoisted(() => ({
  create: vi.fn(),
  head: vi.fn(),
  patch: vi.fn(),
  complete: vi.fn(),
  read: vi.fn(),
  download: vi.fn(),
}))
const routerSpies = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn() }))
const ncrActionSpies = vi.hoisted(() => ({ closeNcr: vi.fn(), submitDisposition: vi.fn() }))
vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifyError: notifySpies.error,
  notifyOperationFailure: notifySpies.operationFailure,
  notifySuccess: notifySpies.success,
}))

vi.mock('@/composables/quality/useQualityInspectionTasks', () => ({
  useQualityInspectionTaskActions: () => ({
    startInspection: taskActionSpies.startInspection,
    refreshInspectionTasks: taskActionSpies.refreshInspectionTasks,
  }),
}))
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleQualityInspectionTasks: qualityTaskRead,
  createBusinessConsoleQualityEvidenceUploadSession: evidenceApi.create,
  getBusinessConsoleQualityEvidenceTusOffset: evidenceApi.head,
  patchBusinessConsoleQualityEvidenceTusUpload: evidenceApi.patch,
  completeBusinessConsoleQualityEvidenceUpload: evidenceApi.complete,
  downloadBusinessConsoleQualityEvidenceContent: evidenceApi.download,
  getBusinessConsoleQualityNcrQueryOptions: (options: unknown) => ({
    query: () => evidenceApi.read(options),
  }),
}))

const operationContextState = vi.hoisted(() => ({
  detail: undefined as { value: Record<string, unknown> | undefined } | undefined,
  pending: undefined as { value: boolean } | undefined,
  error: undefined as { value: string } | undefined,
  refresh: vi.fn(),
}))
vi.mock('@/composables/quality/useQualityOperationContext', async () => {
  const { shallowRef } = await import('vue')
  return {
    useQualityOperationContext: () => {
      operationContextState.detail = shallowRef()
      operationContextState.pending = shallowRef(true)
      operationContextState.error = shallowRef('')
      return {
        detail: operationContextState.detail,
        pending: operationContextState.pending,
        errorMessage: operationContextState.error,
        refresh: operationContextState.refresh,
      }
    },
  }
})

const qualityState = vi.hoisted(() => ({
  inspectionFilters: undefined as
    | {
        organizationId: string
        environmentId: string
        category?: string
        status?: string
        keyword?: string
      }
    | undefined,
  inspectionContextInitiallyEmpty: false,
  recordError: undefined as unknown,
  inspectionPlans: [
    {
      id: 'PLAN-001',
      code: 'IQP-001',
      category: undefined as string | undefined,
      skuCode: 'SKU-001',
      status: 'active',
    },
  ],
  planCharacteristics: [
    {
      characteristicCode: 'DIM-01',
      name: '长度',
      characteristicType: undefined as string | undefined,
      lowerSpecLimit: 9.8,
      upperSpecLimit: 10.2,
      unitCode: 'mm',
    },
  ],
  planCharacteristicsRef: undefined as { value: Array<Record<string, unknown>> } | undefined,
  ncrInitialContext: { organizationId: 'org-001', environmentId: 'env-dev' },
  ncrFilters: undefined as
    | { organizationId: string; environmentId: string; status?: string; keyword?: string }
    | undefined,
  ncrs: [
    {
      id: 'NCR-001',
      code: 'NCR-001',
      status: 'open',
      reworkWorkOrderCreationStatus: 'created',
      reworkWorkOrderId: 'RW-001',
    },
  ] as BusinessConsoleQualityNcrItem[],
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  routeState.route = reactive({ query: {} })

  return {
    ...actual,
    RouterLink: { name: 'RouterLink', props: ['to'], template: '<a data-router-link><slot /></a>' },
    useRoute: () => routeState.route,
    useRouter: () => ({ push: routerSpies.push, replace: routerSpies.replace }),
  }
})

vi.mock('@/composables/shared/usePagedList', async () => {
  const { shallowRef } = await import('vue')

  return {
    usePagedList: () => ({
      page: shallowRef(1),
      pageSize: shallowRef(100),
    }),
  }
})

// 方案 / 物料 / 单位 / 原因码一律只选不填：这些目录内部走 pinia + colada，测试整体打桩。
vi.mock('@/composables/quality/useQualityPickerCatalog', async () => {
  const { computed, shallowRef } = await import('vue')

  return {
    useQualityInspectionPlanCatalog: () => ({
      inspectionPlans: shallowRef([{ id: 'plan-1', code: 'QP-1', skuCode: 'SKU-001' }]),
      inspectionPlansPending: shallowRef(false),
      inspectionPlanOptions: computed(() => [{ value: 'plan-1', label: 'QP-1' }]),
      // 只读回显要把方案标识翻成人读方案号（#1418），桩要跟真实目录的契约一致。
      inspectionPlanCodeById: computed(() => new Map([['plan-1', 'QP-1']])),
    }),
    useQualitySkuCatalog: () => ({
      skuOptions: computed(() => [{ value: 'SKU-001', label: '示例物料' }]),
      skusPending: shallowRef(false),
      skuNameByCode: computed(() => new Map([['SKU-001', '示例物料']])),
    }),
    useQualityUomCatalog: () => ({
      uomOptions: computed(() => [{ value: 'EA', label: '个' }]),
      uomsPending: shallowRef(false),
    }),
    useQualityReasonCatalog: () => ({
      reasonsPending: shallowRef(false),
      defectReasonOptions: computed(() => [{ value: 'DEF-01', label: '尺寸超差' }]),
      dispositionReasonOptions: computed(() => [{ value: '让步接收', label: '让步接收' }]),
      reasonGroupSuggestions: computed(() => []),
    }),
  }
})

vi.mock('@/composables/quality/useBusinessQuality', async () => {
  const { computed, reactive, shallowRef } = await import('vue')

  return {
    useQualityFirstArticlePlanActions: () => ({
      activateFirstArticlePlan: vi.fn(),
      activateFirstArticlePlanPending: shallowRef(false),
      createAndActivateFirstArticlePlan: vi.fn(),
      createFirstArticlePlanPending: shallowRef(false),
    }),
    useQualityFirstArticleInspections: () => ({
      firstArticleRecords: shallowRef([]),
      firstArticleRecordsError: shallowRef(),
      firstArticleRecordsPending: shallowRef(false),
      firstArticleRecordsTotal: shallowRef(0),
      recordFilters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: undefined,
        result: undefined,
        skip: 0,
        take: 100,
      }),
      refreshFirstArticleRecords: vi.fn(),
    }),
    useQualityInspectionPlanCharacteristics: (source: () => { inspectionPlanId: string }) => {
      const planCharacteristics = shallowRef(
        source().inspectionPlanId ? qualityState.planCharacteristics : [],
      )
      qualityState.planCharacteristicsRef = planCharacteristics
      return {
        planCharacteristics,
        planCharacteristicsError: shallowRef(),
        planCharacteristicsPending: shallowRef(false),
        refreshPlanCharacteristics: vi.fn(),
      }
    },
    useQualityInspectionPlans: (initial = {}) => {
      const filters = reactive({
        organizationId: qualityState.inspectionContextInitiallyEmpty ? '' : 'org-001',
        environmentId: qualityState.inspectionContextInitiallyEmpty ? '' : 'env-dev',
        category: undefined as string | undefined,
        status: undefined as string | undefined,
        keyword: undefined as string | undefined,
        skip: 0,
        take: 100,
        ...initial,
      })
      qualityState.inspectionFilters = filters

      return {
        createInspectionRecord: createRecordSpy,
        createInspectionRecordError: shallowRef(),
        createInspectionRecordPending: shallowRef(false),
        filters,
        inspectionPlans: computed(() => qualityState.inspectionPlans),
        inspectionPlansError: shallowRef(),
        inspectionPlansPending: shallowRef(false),
        inspectionPlansTotal: computed(() => qualityState.inspectionPlans.length),
        refreshInspectionPlans: vi.fn(),
      }
    },
    useQualityInspectionRecordDetail: (source: () => { inspectionRecordId: string }) => ({
      record: computed(() =>
        source().inspectionRecordId === 'INSP-REC-9'
          ? {
              inspectionRecordId: 'INSP-REC-9',
              skuCode: 'SKU-001',
              sourceDocumentId: 'WO-1',
              result: 'rejected',
              inspectedQuantity: 3,
              dispositionReason: '尺寸超差',
              resultLines: [{ characteristicCode: 'DIM-01', measuredValue: 12.6 }],
            }
          : undefined,
      ),
      recordPending: shallowRef(false),
      recordError: computed(() => qualityState.recordError),
      refreshRecord: vi.fn(),
    }),
    useQualityNcrs: (initial = {}) => {
      const filters = reactive({
        ...qualityState.ncrInitialContext,
        status: undefined as string | undefined,
        keyword: undefined as string | undefined,
        skip: 0,
        take: 100,
        ...initial,
      })
      qualityState.ncrFilters = filters

      return {
        closeNcr: ncrActionSpies.closeNcr,
        closeNcrError: shallowRef(),
        closeNcrPending: shallowRef(false),
        filters,
        ncrActionGate: (
          _ncrId: string,
          status: string | undefined,
          dispositionType: string | undefined,
          action: 'submit-disposition' | 'close',
        ) => {
          const normalizedStatus = status?.toLowerCase()
          const executable =
            (action === 'submit-disposition' && normalizedStatus === 'open') ||
            (action === 'close' &&
              normalizedStatus === 'disposition-in-progress' &&
              Boolean(dispositionType))
          return { executable }
        },
        ncrs: computed(() => qualityState.ncrs),
        ncrsError: shallowRef(),
        ncrsPending: shallowRef(false),
        ncrsTotal: computed(() => qualityState.ncrs.length),
        refreshNcrs: vi.fn(),
        submitDisposition: ncrActionSpies.submitDisposition,
        submitDispositionError: shallowRef(),
        submitDispositionPending: shallowRef(false),
      }
    },
  }
})

const dataTableStub = {
  props: ['rows', 'columns'],
  methods: {
    summary(
      this: {
        columns?: Array<{
          key: string
          accessor?: (row: Record<string, unknown>) => unknown
        }>
      },
      row: Record<string, unknown>,
    ) {
      return this.columns?.find((column) => column.key === 'summary')?.accessor?.(row) ?? ''
    },
  },
  template:
    '<table><tbody><tr v-for="(row, i) in rows" :key="i"><td><slot name="cell-code" :row="row" /></td><td data-plan-summary>{{ summary(row) }}</td><td><slot name="cell-actions" :row="row" /></td></tr></tbody></table>',
}

const uiStubs = {
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogCancel: { template: '<button><slot /></button>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogTrigger: { template: '<div><slot /></div>' },
  BusinessLayout: { template: '<main><slot /></main>' },
  BusinessDocumentApprovalPanel: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<section data-testid="approval-panel" />',
  },
  Button: { template: '<button><slot /></button>' },
  DataTable: dataTableStub,
  NvDataTable: dataTableStub,
  DataTablePagination: { props: ['page', 'pageSize', 'totalItems'], template: '<nav />' },
  Dialog: { props: ['open'], template: '<div v-if="open" data-dialog><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogDescription: { template: '<p><slot /></p>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DropdownMenuItem: { template: '<button><slot /></button>' },
  Field: { template: '<div><slot /></div>' },
  FieldDescription: { template: '<p><slot /></p>' },
  FieldGroup: { template: '<div><slot /></div>' },
  FieldLabel: { template: '<label><slot /></label>' },
  Input: { props: ['modelValue'], template: '<input :value="modelValue" />' },
  PageHeader: {
    props: ['title', 'count'],
    template: '<header><h1>{{ title }}</h1><p>{{ count }}</p><slot name="actions" /></header>',
  },
  RowActions: { template: '<div><slot /></div>' },
  SectionCard: {
    props: ['description', 'value'],
    template: '<div>{{ description }} {{ value }}</div>',
  },
  SectionCards: { template: '<div><slot /></div>' },
  Select: { template: '<div><slot /></div>' },
  SelectContent: { template: '<div><slot /></div>' },
  SelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<div><select aria-label="检验类别" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value="all">全部类别</option><option value="first-article">首件检验</option></select><div data-select-options><slot /></div></div>',
  },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: {
    props: ['value'],
    template: '<div data-select-item :data-value="value"><slot /></div>',
  },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: true,
  NvDialog: { props: ['open'], template: '<div v-if="open" data-dialog><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  SelectTrigger: { template: '<button><slot /></button>' },
  Sheet: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  SheetContent: { template: '<div><slot /></div>' },
  SheetDescription: { template: '<p><slot /></p>' },
  SheetFooter: { template: '<div><slot /></div>' },
  SheetHeader: { template: '<div><slot /></div>' },
  SheetTitle: { template: '<h2><slot /></h2>' },
  NvSheet: { props: ['open'], template: '<div v-if="open" data-record-sheet><slot /></div>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSpinner: true,
  StatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
  NvStatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
  Toolbar: { template: '<div><slot name="filters" /></div>' },
}

function mountQualityPage(component: unknown) {
  return mount(component, {
    global: {
      stubs: uiStubs,
    },
  })
}

async function nextRenderTick() {
  const { nextTick } = await import('vue')
  await nextTick()
  await nextTick()
}

export {
  routeState,
  authState,
  notifySpies,
  taskActionSpies,
  createRecordSpy,
  qualityTaskRead,
  evidenceApi,
  routerSpies,
  ncrActionSpies,
  operationContextState,
  qualityState,
  mountQualityPage,
  nextRenderTick,
}

export function resetQualityLocationFixture() {
  evidenceApi.create.mockReset().mockResolvedValue({
    data: {
      success: true,
      data: {
        uploadSessionId: 'session-1',
        fileId: 'reserved-file',
        expiresAtUtc: '2099-01-01T00:00:00Z',
        uploadUrl: '/api/business-console/v1/files/quality-evidence/tus/session-1',
        uploadHeaders: {},
      },
    },
  })
  evidenceApi.head
    .mockReset()
    .mockResolvedValue({ response: new Response(null, { headers: { 'Upload-Offset': '0' } }) })
  evidenceApi.patch.mockReset().mockResolvedValue({ response: new Response(null, { status: 204 }) })
  evidenceApi.complete
    .mockReset()
    .mockResolvedValue({ data: { success: true, data: { fileId: 'completed-file' } } })
  evidenceApi.download.mockReset()
  evidenceApi.read.mockReset().mockResolvedValue({
    success: true,
    data: {
      id: 'NCR-001',
      code: 'NCR-001',
      status: 'disposition-in-progress',
      attachmentFileIds: ['completed-file'],
    },
  })
  setActivePinia(createPinia())
  routeState.route!.query = {}
  qualityState.inspectionFilters = undefined
  qualityState.inspectionContextInitiallyEmpty = false
  qualityState.ncrInitialContext = { organizationId: 'org-001', environmentId: 'env-dev' }
  qualityState.ncrFilters = undefined
  qualityState.ncrs = [
    {
      id: 'NCR-001',
      code: 'NCR-001',
      status: 'open',
      reworkWorkOrderCreationStatus: 'created',
      reworkWorkOrderId: 'RW-001',
    },
  ]
  authState.permissionCodes = [
    'business.quality.inspection-records.create',
    'business.quality.ncr.read',
    'business.quality.ncr.manage',
    'business.mes.work-orders.read',
  ]
  qualityState.recordError = undefined
  qualityState.inspectionPlans = [
    {
      id: 'PLAN-001',
      code: 'IQP-001',
      category: undefined,
      skuCode: 'SKU-001',
      status: 'active',
    },
  ]
  qualityState.planCharacteristics = [
    {
      characteristicCode: 'DIM-01',
      name: '长度',
      characteristicType: undefined,
      lowerSpecLimit: 9.8,
      upperSpecLimit: 10.2,
      unitCode: 'mm',
    },
  ]
  qualityState.planCharacteristicsRef = undefined
  notifySpies.error.mockReset()
  notifySpies.operationFailure.mockReset()
  notifySpies.success.mockReset()
  createRecordSpy.mockReset()
  taskActionSpies.startInspection.mockReset()
  taskActionSpies.refreshInspectionTasks.mockReset()
  taskActionSpies.refreshInspectionTasks.mockResolvedValue(undefined)
  qualityTaskRead.mockReset()
  qualityTaskRead.mockResolvedValue({
    data: {
      success: true,
      data: {
        items: [
          {
            inspectionTaskId: 'TASK-001',
            status: 'in-progress',
            allowedActions: ['submit-inspection'],
          },
        ],
        total: 1,
      },
    },
  })
  routerSpies.push.mockReset()
  routerSpies.replace.mockReset()
  routerSpies.replace.mockImplementation(async ({ query }: { query: Record<string, string> }) => {
    routeState.route!.query = query
  })
  ncrActionSpies.closeNcr.mockReset()
  ncrActionSpies.submitDisposition.mockReset()
}
