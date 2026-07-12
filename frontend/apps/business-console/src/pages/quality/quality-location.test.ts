import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import InspectionsPage from './inspections.vue'
import NcrsPage from './ncrs.vue'

const routeState = vi.hoisted(() => ({
  route: undefined as { query: Record<string, string> } | undefined,
}))

const qualityState = vi.hoisted(() => ({
  inspectionFilters: undefined as { status?: string, keyword?: string } | undefined,
  inspectionPlans: [
    {
      id: 'PLAN-001',
      code: 'IQP-001',
      skuCode: 'SKU-001',
      status: 'active',
    },
  ],
  ncrFilters: undefined as { status?: string, keyword?: string } | undefined,
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
    useQualityInspectionPlans: (initial = {}) => {
      const filters = reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
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
    template: '<table><tbody><tr v-for="(row, i) in rows" :key="i"><td><slot name="cell-code" :row="row" /></td><td><slot name="cell-actions" :row="row" /></td></tr></tbody></table>',
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
  Input: { template: '<input />' },
  PageHeader: {
    props: ['title', 'count'],
    template: '<header><h1>{{ title }}</h1><p>{{ count }}</p><slot name="actions" /></header>',
  },
  RowActions: { template: '<div><slot /></div>' },
  SectionCard: { props: ['description', 'value'], template: '<div>{{ description }} {{ value }}</div>' },
  SectionCards: { template: '<div><slot /></div>' },
  Select: { template: '<div><slot /></div>' },
  SelectContent: { template: '<div><slot /></div>' },
  SelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { props: ['value'], template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button><slot /></button>' },
  NvSelectValue: true,
  SelectTrigger: { template: '<button><slot /></button>' },
  SelectValue: true,
  Sheet: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  SheetContent: { template: '<div><slot /></div>' },
  SheetDescription: { template: '<p><slot /></p>' },
  SheetFooter: { template: '<div><slot /></div>' },
  SheetHeader: { template: '<div><slot /></div>' },
  SheetTitle: { template: '<h2><slot /></h2>' },
  Spinner: true,
  StatusBadge: { props: ['value'], template: '<span>{{ value }}</span>' },
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
    qualityState.ncrFilters = undefined
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
})

async function nextRenderTick() {
  const { nextTick } = await import('vue')
  await nextTick()
  await nextTick()
}
