import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import InspectionsPage from './inspections.vue'
import NcrsPage from './ncrs.vue'

const routeState = vi.hoisted(() => ({
  route: undefined as { query: Record<string, string> } | undefined,
}))

const notifySpies = vi.hoisted(() => ({ error: vi.fn(), success: vi.fn() }))
const taskActionSpies = vi.hoisted(() => ({ startInspection: vi.fn() }))
vi.mock('@/utils/notify', () => ({
  notifyError: notifySpies.error,
  notifySuccess: notifySpies.success,
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { principalId: 'qa-user-001' } }),
}))

vi.mock('@/composables/useQualityInspectionTasks', () => ({
  useQualityInspectionTaskActions: () => ({
    startInspection: taskActionSpies.startInspection,
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
      lowerSpecLimit: 9.8,
      upperSpecLimit: 10.2,
      unitCode: 'mm',
    },
  ],
  planCharacteristicsRef: undefined as { value: Array<Record<string, unknown>> } | undefined,
  ncrFilters: undefined as { status?: string; keyword?: string } | undefined,
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
    useRouter: () => ({ push: vi.fn() }),
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
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: undefined as string | undefined,
        keyword: undefined as string | undefined,
        skip: 0,
        take: 100,
        ...initial,
      })
      qualityState.ncrFilters = filters

      return {
        closeNcr: vi.fn(),
        closeNcrError: shallowRef(),
        closeNcrPending: shallowRef(false),
        filters,
        ncrs: computed(() => qualityState.ncrs),
        ncrsError: shallowRef(),
        ncrsPending: shallowRef(false),
        ncrsTotal: computed(() => qualityState.ncrs.length),
        refreshNcrs: vi.fn(),
        submitDisposition: vi.fn(),
        submitDispositionError: shallowRef(),
        submitDispositionPending: shallowRef(false),
      }
    },
  }
})

const uiStubs = {
  AlertDialog: { template: '<div><slot /></div>' },
  AlertDialogAction: { template: '<button><slot /></button>' },
  AlertDialogCancel: { template: '<button><slot /></button>' },
  AlertDialogContent: { template: '<div><slot /></div>' },
  AlertDialogDescription: { template: '<p><slot /></p>' },
  AlertDialogFooter: { template: '<div><slot /></div>' },
  AlertDialogHeader: { template: '<div><slot /></div>' },
  AlertDialogTitle: { template: '<h2><slot /></h2>' },
  AlertDialogTrigger: { template: '<div><slot /></div>' },
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
    qualityState.ncrFilters = undefined
    qualityState.recordError = undefined
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'DIM-01',
        name: '长度',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    qualityState.planCharacteristicsRef = undefined
    notifySpies.error.mockReset()
    notifySpies.success.mockReset()
    taskActionSpies.startInspection.mockReset()
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

    expect(wrapper.get('#record-quantity').attributes('step')).toBe('any')
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
