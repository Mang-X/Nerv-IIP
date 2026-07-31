import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import InspectionsPage from './inspections.vue'
import NcrsPage from './ncrs.vue'

// 名录解析不是这些用例的被测对象；给稳定桩（解析不出名称→页面回退显编码），
// 避免真实实现去取业务上下文 store 而要求测试装 Pinia。
vi.mock('@/composables/useSkuNames', async () => {
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
vi.mock('@/composables/useBusinessPartnerNames', async () => {
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
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
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
      deviceByCode: emptyIndex,
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

const notifySpies = vi.hoisted(() => ({
  error: vi.fn(),
  operationFailure: vi.fn(),
  success: vi.fn(),
}))
const taskActionSpies = vi.hoisted(() => ({
  startInspection: vi.fn(),
  refreshInspectionTasks: vi.fn(),
}))
const routerSpies = vi.hoisted(() => ({ push: vi.fn(), replace: vi.fn() }))
const ncrActionSpies = vi.hoisted(() => ({ closeNcr: vi.fn(), submitDisposition: vi.fn() }))
vi.mock('@/utils/notify', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/utils/notify')>()),
  notifyError: notifySpies.error,
  notifyOperationFailure: notifySpies.operationFailure,
  notifySuccess: notifySpies.success,
}))

vi.mock('@/composables/useQualityInspectionTasks', () => ({
  useQualityInspectionTaskActions: () => ({
    startInspection: taskActionSpies.startInspection,
    refreshInspectionTasks: taskActionSpies.refreshInspectionTasks,
  }),
}))

const qualityState = vi.hoisted(() => ({
  inspectionFilters: undefined as
    | { organizationId: string; environmentId: string; status?: string; keyword?: string }
    | undefined,
  inspectionContextInitiallyEmpty: false,
  recordError: undefined as unknown,
  inspectionPlans: [
    {
      id: 'PLAN-001',
      code: 'IQP-001',
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
    },
  ],
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  routeState.route = reactive({ query: {} })

  return {
    ...actual,
    RouterLink: { props: ['to'], template: '<a data-router-link><slot /></a>' },
    useRoute: () => routeState.route,
    useRouter: () => ({ push: routerSpies.push, replace: routerSpies.replace }),
  }
})

vi.mock('@/composables/usePagedList', async () => {
  const { shallowRef } = await import('vue')

  return {
    usePagedList: () => ({
      page: shallowRef(1),
      pageSize: shallowRef(100),
    }),
  }
})

// 方案 / 物料 / 单位 / 原因码一律只选不填：这些目录内部走 pinia + colada，测试整体打桩。
vi.mock('@/composables/useQualityPickerCatalog', async () => {
  const { computed, shallowRef } = await import('vue')

  return {
    useQualityInspectionPlanCatalog: () => ({
      inspectionPlans: shallowRef([{ id: 'plan-1', code: 'QP-1', skuCode: 'SKU-001' }]),
      inspectionPlansPending: shallowRef(false),
      inspectionPlanOptions: computed(() => [{ value: 'plan-1', label: 'QP-1' }]),
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

vi.mock('@/composables/useBusinessQuality', async () => {
  const { computed, reactive, shallowRef } = await import('vue')

  return {
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
        status: undefined as string | undefined,
        keyword: undefined as string | undefined,
        skip: 0,
        take: 100,
        ...initial,
      })
      qualityState.inspectionFilters = filters

      return {
        createInspectionRecord: vi.fn(),
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

const uiStubs = {
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogAction: { template: '<button><slot /></button>' },
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
  DataTable: {
    props: ['rows'],
    template:
      '<table><tbody><tr v-for="(row, i) in rows" :key="i"><td><slot name="cell-code" :row="row" /></td><td><slot name="cell-actions" :row="row" /></td></tr></tbody></table>',
  },
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
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: true,
  NvDialog: { props: ['open'], template: '<div v-if="open" data-dialog><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  SelectTrigger: { template: '<button><slot /></button>' },
  SelectValue: true,
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
  Spinner: true,
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

describe('quality route location behavior', () => {
  beforeEach(() => {
    routeState.route!.query = {}
    qualityState.inspectionFilters = undefined
    qualityState.inspectionContextInitiallyEmpty = false
    qualityState.ncrInitialContext = { organizationId: 'org-001', environmentId: 'env-dev' }
    qualityState.ncrFilters = undefined
    qualityState.recordError = undefined
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
    taskActionSpies.startInspection.mockReset()
    taskActionSpies.refreshInspectionTasks.mockReset()
    taskActionSpies.refreshInspectionTasks.mockResolvedValue(undefined)
    routerSpies.push.mockReset()
    routerSpies.replace.mockReset()
    routerSpies.replace.mockImplementation(async ({ query }: { query: Record<string, string> }) => {
      routeState.route!.query = query
    })
    ncrActionSpies.closeNcr.mockReset()
    ncrActionSpies.submitDisposition.mockReset()
  })

  it('keeps the user-selected NCR status filter when ncrId is removed from the route', async () => {
    routeState.route!.query = { ncrId: 'NCR-001' }
    mountQualityPage(NcrsPage)

    qualityState.ncrFilters!.status = 'open'
    routeState.route!.query = {}
    await nextRenderTick()

    expect(qualityState.ncrFilters!.keyword).toBeUndefined()
    expect(qualityState.ncrFilters!.status).toBe('open')
  })

  it.each(['organizationId', 'environmentId'] as const)(
    'blocks both NCR actions when %s is empty and recovers when context returns',
    async (missingField) => {
      qualityState.ncrInitialContext[missingField] = ''
      const wrapper = mountQualityPage(NcrsPage)
      const vm = wrapper.vm as unknown as {
        canCloseNcr: boolean
        canSubmitDisposition: boolean
        closeForm: { reason: string }
        dispositionForm: { dispositionType: string; attachmentFileIds: string }
        openNcr: (ncr: Record<string, unknown>) => void
        submitCloseNcr: () => Promise<void>
        submitNcrDisposition: () => Promise<void>
      }
      vm.openNcr(qualityState.ncrs[0]!)
      vm.closeForm.reason = '处置结果已核验'
      // 本用例只考察业务范围这一维门禁：选一个不需要 MRB 评审 / 中央审批链的处置类型，
      // 免得把 #1327 新增的处置前置条件混进来。
      vm.dispositionForm.dispositionType = 'sort-and-screen'
      vm.dispositionForm.attachmentFileIds = 'file-001'
      await nextRenderTick()

      expect(vm.canSubmitDisposition).toBe(false)
      expect(vm.canCloseNcr).toBe(false)
      const dispositionButton = wrapper
        .findAll('button')
        .find((button) => button.text().includes('提交处置'))
      const closeButton = wrapper
        .findAll('button')
        .find((button) => button.text().includes('关闭不合格品'))
      expect(dispositionButton?.attributes('disabled')).toBeDefined()
      expect(closeButton?.attributes('disabled')).toBeDefined()
      await vm.submitNcrDisposition()
      await vm.submitCloseNcr()
      expect(notifySpies.error).toHaveBeenNthCalledWith(1, '业务范围尚未就绪，请稍后重试。')
      expect(notifySpies.error).toHaveBeenNthCalledWith(2, '业务范围尚未就绪，请稍后重试。')
      expect(ncrActionSpies.submitDisposition).not.toHaveBeenCalled()
      expect(ncrActionSpies.closeNcr).not.toHaveBeenCalled()

      qualityState.ncrFilters![missingField] =
        missingField === 'organizationId' ? 'org-001' : 'env-dev'
      await nextRenderTick()

      expect(vm.canSubmitDisposition).toBe(true)
      expect(vm.canCloseNcr).toBe(false)
      expect(dispositionButton?.attributes('disabled')).toBeUndefined()
      expect(closeButton?.attributes('disabled')).toBeDefined()
    },
  )

  it('rechecks business context when close is confirmed after context is lost', async () => {
    const wrapper = mountQualityPage(NcrsPage)
    const vm = wrapper.vm as unknown as {
      closeForm: { reason: string }
      openNcr: (ncr: Record<string, unknown>) => void
      submitCloseNcr: () => Promise<void>
    }
    vm.openNcr(qualityState.ncrs[0]!)
    vm.closeForm.reason = '处置结果已核验'
    await nextRenderTick()
    const closeButton = wrapper
      .findAll('button')
      .find((button) => button.text().trim() === '关闭不合格品')
    expect(closeButton).toBeDefined()
    await closeButton!.trigger('click')

    qualityState.ncrFilters!.environmentId = ''
    await nextRenderTick()
    const confirmCloseButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认关闭'))

    expect(confirmCloseButton).toBeDefined()
    await confirmCloseButton!.trigger('click')
    expect(notifySpies.error).toHaveBeenCalledWith('业务范围尚未就绪，请稍后重试。')
    expect(ncrActionSpies.closeNcr).not.toHaveBeenCalled()
  })

  it('keeps existing NCR field validation silent when business context is ready', async () => {
    const wrapper = mountQualityPage(NcrsPage)
    const vm = wrapper.vm as unknown as {
      submitCloseNcr: () => Promise<void>
    }

    await vm.submitCloseNcr()

    expect(notifySpies.error).not.toHaveBeenCalled()
    expect(ncrActionSpies.closeNcr).not.toHaveBeenCalled()
  })

  it('keeps the user-selected inspection status filter when inspectionPlanId is removed from the route', async () => {
    routeState.route!.query = { inspectionPlanId: 'PLAN-001' }
    mountQualityPage(InspectionsPage)

    qualityState.inspectionFilters!.status = 'active'
    routeState.route!.query = {}
    await nextRenderTick()

    expect(qualityState.inspectionFilters!.keyword).toBeUndefined()
    expect(qualityState.inspectionFilters!.status).toBe('active')
  })

  it('does not open the inspection record dialog for a plain inspectionPlanId location route', async () => {
    routeState.route!.query = { inspectionPlanId: 'PLAN-001' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    expect(wrapper.find('[data-dialog]').exists()).toBe(false)
    expect(qualityState.inspectionFilters!.keyword).toBe('PLAN-001')
  })

  it('prefills the existing record flow from the stable inspection task query contract', async () => {
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    const form = (
      wrapper.vm as unknown as {
        recordForm: {
          sourceDocumentId: string
          skuCode: string
          inspectedQuantity: string
          resultLines: Array<{
            characteristicCode: string
            specification: string
            unitCode: string
          }>
        }
      }
    ).recordForm
    expect(form.sourceDocumentId).toBe('GR-001')
    expect(form.skuCode).toBe('SKU-RM-001')
    expect(form.inspectedQuantity).toBe('12')
    expect(form.resultLines).toEqual([
      expect.objectContaining({
        characteristicCode: 'DIM-01',
        specification: '9.8–10.2 mm',
        unitCode: 'mm',
      }),
    ])
  })

  it('accepts whole-number quantities prefilled by an inspection task', async () => {
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '1200',
      action: 'create',
    }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    // 待检任务流：来源单据 / 物料 / 检验数量全部由任务带出，只读呈现而非输入框。
    const carried = wrapper.get('[data-slot="carried-context"]')
    expect(carried.text()).toContain('1200')
    expect(carried.text()).toContain('SKU-RM-001')
    expect(carried.text()).toContain('GR-001')
    expect(wrapper.find('#record-quantity').exists()).toBe(false)
  })

  it('enables task submission after business context arrives asynchronously', async () => {
    qualityState.inspectionContextInitiallyEmpty = true
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ observedValue: string }> }
      canCreateRecord: boolean
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await nextRenderTick()

    expect(vm.canCreateRecord).toBe(false)
    await vm.submitInspectionRecord()
    expect(notifySpies.error).toHaveBeenCalledWith('业务范围尚未就绪，请稍后重试。')
    expect(taskActionSpies.startInspection).not.toHaveBeenCalled()

    qualityState.inspectionFilters!.organizationId = 'org-001'
    qualityState.inspectionFilters!.environmentId = 'env-dev'
    await nextRenderTick()

    expect(vm.canCreateRecord).toBe(true)
    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledOnce()
    expect(taskActionSpies.startInspection.mock.calls[0]?.[1]).not.toHaveProperty('inspectorUserId')
  })

  it('preserves inspector input when plan characteristics arrive asynchronously', async () => {
    qualityState.planCharacteristics = []
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const form = (
      wrapper.vm as unknown as {
        recordForm: {
          resultLines: Array<{ characteristicCode: string; observedValue: string }>
        }
      }
    ).recordForm
    form.resultLines[0]!.characteristicCode = 'MANUAL-01'
    form.resultLines[0]!.observedValue = '10.1'
    qualityState.planCharacteristicsRef!.value = [
      { characteristicCode: 'DIM-01', lowerSpecLimit: 9.8, upperSpecLimit: 10.2 },
    ]
    await nextRenderTick()

    expect(form.resultLines).toEqual([
      expect.objectContaining({ characteristicCode: 'MANUAL-01', observedValue: '10.1' }),
    ])
  })

  it('clears the routed inspection task and stale form context after a lifecycle conflict', async () => {
    const { LifecycleStateChangedError } = await import('@/composables/lifecycleAction')
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      batchNo: 'LOT-7',
      action: 'create',
    }
    taskActionSpies.startInspection.mockRejectedValueOnce(
      new LifecycleStateChangedError('conflict'),
    )

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordSheetOpen: boolean
      recordForm: {
        inspectionPlanId: string
        sourceDocumentId: string
        skuCode: string
        batchNo: string
        resultLines: Array<{ observedValue: string }>
      }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await vm.submitInspectionRecord()
    await nextRenderTick()

    expect(routerSpies.replace).toHaveBeenCalledWith({ query: {} })
    expect(routeState.route!.query.inspectionTaskId).toBeUndefined()
    expect(vm.recordSheetOpen).toBe(false)
    expect(vm.recordForm).toMatchObject({
      inspectionPlanId: '',
      sourceDocumentId: '',
      skuCode: '',
      batchNo: '',
    })
    expect(vm.recordForm.resultLines).toEqual([
      expect.objectContaining({ characteristicCode: '', observedValue: '' }),
    ])
    expect(taskActionSpies.refreshInspectionTasks).toHaveBeenCalledOnce()
    expect(notifySpies.error).toHaveBeenCalledWith('状态已被其他操作更新')
  })

  it('preserves the routed inspection task and form input after an ordinary validation error', async () => {
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    taskActionSpies.startInspection.mockRejectedValueOnce({
      success: false,
      statusCode: 422,
      message: '实测值不符合格式',
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordSheetOpen: boolean
      recordForm: {
        inspectionPlanId: string
        sourceDocumentId: string
        resultLines: Array<{ observedValue: string }>
      }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await vm.submitInspectionRecord()
    await nextRenderTick()

    expect(routerSpies.replace).not.toHaveBeenCalled()
    expect(routeState.route!.query.inspectionTaskId).toBe('TASK-001')
    expect(vm.recordSheetOpen).toBe(true)
    expect(vm.recordForm).toMatchObject({
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
    })
    expect(vm.recordForm.resultLines[0]!.observedValue).toBe('10.1')
    expect(taskActionSpies.refreshInspectionTasks).not.toHaveBeenCalled()
  })

  // #1326：计量型（variable）特性必须提交数值 measuredValue，缺失时后端会以领域 400 拒绝。
  it('submits measuredValue for variable characteristics and gates submit on a numeric value', async () => {
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'DIM-01',
        name: '长度',
        characteristicType: 'variable',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    taskActionSpies.startInspection.mockResolvedValueOnce({
      data: { inspectionRecordId: 'REC-001' },
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: {
        resultLines: Array<{
          characteristicCode: string
          characteristicType: string
          measuredValue: string
          observedValue: string
        }>
      }
      canCreateRecord: boolean
      submitInspectionRecord: () => Promise<void>
    }

    // 自动带出的计量特性行：未录测量值前禁止提交（不再靠文本 observedValue 假放行）。
    expect(vm.recordForm.resultLines[0]).toMatchObject({
      characteristicCode: 'DIM-01',
      characteristicType: 'variable',
    })
    expect(vm.canCreateRecord).toBe(false)

    vm.recordForm.resultLines[0]!.measuredValue = '10.1'
    await nextRenderTick()
    expect(vm.canCreateRecord).toBe(true)

    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledWith(
      'TASK-001',
      expect.objectContaining({
        resultLines: [
          expect.objectContaining({
            characteristicCode: 'DIM-01',
            measuredValue: 10.1,
            observedValue: '10.1',
            unitCode: 'mm',
          }),
        ],
      }),
    )
  })

  it('keeps attribute characteristics on text observedValue without measuredValue', async () => {
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'APP-01',
        name: '外观',
        characteristicType: 'attribute',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    taskActionSpies.startInspection.mockResolvedValueOnce({
      data: { inspectionRecordId: 'REC-002' },
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ observedValue: string }> }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '外观无划痕'
    await nextRenderTick()

    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledWith(
      'TASK-001',
      expect.objectContaining({
        resultLines: [
          expect.objectContaining({
            characteristicCode: 'APP-01',
            observedValue: '外观无划痕',
            measuredValue: undefined,
          }),
        ],
      }),
    )
  })

  it('routes the backend measured-value rejection through operation failure passthrough', async () => {
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'DIM-01',
        name: '长度',
        characteristicType: 'variable',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    const rejection = {
      success: false,
      statusCode: 400,
      message: '计量型特性“dim-01”需要填写测量值，请录入数值后重新提交。',
    }
    taskActionSpies.startInspection.mockRejectedValueOnce(rejection)

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ measuredValue: string }> }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.measuredValue = '10.1'
    await nextRenderTick()

    await vm.submitInspectionRecord()
    // 台账 #52：领域拒绝理由必须走分层透传（notifyOperationFailure），不能吞成兜底文案。
    expect(notifySpies.operationFailure).toHaveBeenCalledWith(
      '检验记录提交失败',
      rejection,
      '检验记录提交失败，请稍后重试。',
    )
  })

  it('locates a source inspection record: opens read-only record detail from inspectionRecordId', async () => {
    routeState.route!.query = { inspectionRecordId: 'INSP-REC-9' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    // 记录详情真实消费 inspectionRecordId → 展示该记录的判定结论与特性实测值（定位到具体记录，非仅方案）。
    const text = wrapper.text()
    expect(text).toContain('INSP-REC-9')
    expect(text).toContain('rejected')
    expect(text).toContain('DIM-01')
  })

  it('toasts + offers retry (not “未找到”) when the record detail request fails', async () => {
    qualityState.recordError = new Error('403 forbidden')
    routeState.route!.query = { inspectionRecordId: 'INSP-REC-X' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    // 请求失败不再误报为空：走 toast + 可重试，不显示“未找到”。
    expect(notifySpies.error).toHaveBeenCalled()
    expect(wrapper.text()).toContain('检验记录加载失败')
    expect(wrapper.text()).not.toContain('未找到该检验记录')
  })
})

async function nextRenderTick() {
  const { nextTick } = await import('vue')
  await nextTick()
  await nextTick()
}
