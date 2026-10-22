import { flushPromises, mount, type VueWrapper } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { nextTick, reactive, ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useAuthStore } from '@/stores/auth'
import WorkOrdersListPage from './index.vue'

const rushState = vi.hoisted(() => ({
  filters: undefined as unknown as { organizationId: string; environmentId: string },
}))
const createRushWorkOrder = vi.hoisted(() => vi.fn())

vi.mock('vue-router', () => ({
  useRoute: () => ({ query: {} }),
  useRouter: () => ({ push: vi.fn() }),
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}))

vi.mock('@/utils/notify', () => ({
  inlineErrorMessage: () => '',
  notifyOperationFailure: vi.fn(),
  notifySuccess: vi.fn(),
}))

vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({
    byReference: ref(new Map()),
    error: ref(undefined),
    refresh: vi.fn(),
  }),
}))

vi.mock('@/composables/mes/useMesDisplayNames', () => ({
  useMesDisplayNames: () => ({
    resolveSku: (value?: string | null) => value ?? '无',
    resolveWorkCenter: (value?: string | null) => value ?? '无',
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: () => ({ resources: ref([]) }),
  useBusinessSkus: () => ({ skus: ref([]) }),
}))

vi.mock('@/composables/useMesPickerCatalog', () => ({
  useMesMaterialVersionCatalog: () => ({
    productionVersionOptions: () => [{ value: 'PV-1', label: '主版本' }],
    productionVersionsPending: ref(false),
  }),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  describeMesReadinessReason: (reason: string) => ({
    code: reason,
    detail: reason,
    label: reason,
    nextStep: '',
  }),
  useMesWorkScopeSelection: () => ({
    scopeOptions: ref([]),
    scopeSelectionValue: ref(undefined),
    scopeReady: ref(true),
    scopeMessage: ref(''),
    scopePending: ref(false),
    scopeUnavailable: ref(false),
    selectedScope: ref(undefined),
    principalIdentity: ref('principal-test'),
    requireSelectedScope: vi.fn(),
  }),
  useMesOperationTasks: () => ({
    operationTasks: ref([]),
    operationTasksPending: ref(false),
    refreshOperationTasks: vi.fn(),
  }),
  useMesWorkOrders: () => ({
    createRushWorkOrder,
    createRushWorkOrderError: ref(undefined),
    createRushWorkOrderPending: ref(false),
    filters: rushState.filters,
    refreshWorkOrders: vi.fn(),
    readWorkOrderForRelease: vi.fn(),
    releaseWorkOrder: vi.fn(),
    releaseWorkOrderError: ref(undefined),
    releaseWorkOrderPending: ref(false),
    workOrders: ref([]),
    workOrdersError: ref(undefined),
    workOrdersHasFailedResponse: ref(false),
    workOrdersHasSuccessfulResponse: ref(true),
    workOrdersLastUpdatedAt: ref('2026-09-28T00:00:00.000Z'),
    workOrdersPending: ref(false),
    workOrdersTotal: ref(0),
    workOrderManageScope: ref({ kind: 'work-center', id: 'WC-1', displayName: '一号线' }),
    workOrderManageScopeMessage: ref(''),
    workOrderManageScopePending: ref(false),
    workOrderManageScopeReady: ref(true),
    workOrderReadScope: ref({ kind: 'work-center', id: 'WC-1', displayName: '一号线' }),
    workOrderReadScopeMessage: ref(''),
    workOrderReadScopeReady: ref(true),
  }),
  useMesWorkOrderTransformations: () => ({
    splitWorkOrder: vi.fn(),
    mergeWorkOrders: vi.fn(),
    readTransformation: vi.fn(),
    splitWorkOrderPending: ref(false),
    mergeWorkOrdersPending: ref(false),
  }),
}))

const modelInput = {
  props: ['modelValue', 'id'],
  emits: ['update:modelValue'],
  template:
    '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
}

const uiStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  MesWorkScopeSelect: true,
  OrderUrgencyBadge: true,
  ProductionReportDialog: true,
  UrgencyDisplayModeSelect: true,
  WorkOrderDetailSheet: true,
  NvPageHeader: { template: '<header><slot name="actions" /></header>' },
  NvToolbar: { template: '<div><slot name="filters" /><slot name="actions" /></div>' },
  NvDataTable: true,
  NvDialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  NvDialogContent: { template: '<section><slot /></section>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvButton: {
    props: ['disabled', 'type'],
    template: '<button :type="type || \'button\'" :disabled="disabled"><slot /></button>',
  },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  DirectoryPicker: modelInput,
  NvEntityPicker: modelInput,
  NvInput: modelInput,
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectTrigger: { template: '<button type="button"><slot /></button>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<div><slot /></div>' },
  NvSelectValue: true,
  NvStatusBadge: true,
  NvSpinner: true,
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

function mountPage() {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: {
      principalId: 'user-1',
      principalType: 'user',
      organizationId: 'org-1',
      environmentId: 'prod',
      loginName: 'planner',
      permissionCodes: ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
    },
  })
  return mount(WorkOrdersListPage, { global: { plugins: [pinia], stubs: uiStubs } })
}

function submitButton(wrapper: VueWrapper) {
  const target = wrapper.find('footer button[type="submit"]')
  expect(target.text()).toContain('创建急单')
  return target
}

async function openAndFill(wrapper: VueWrapper, fields: { version?: string }) {
  const opener = wrapper.findAll('header button').find((item) => item.text().includes('创建急单'))
  await opener!.trigger('click')
  await wrapper.get('#rush-sku').setValue('FG-1')
  await nextTick()
  if (fields.version) await wrapper.get('#rush-version').setValue(fields.version)
  await wrapper.get('#rush-work-center').setValue('WC-1')
  await nextTick()
}

describe('创建急单', () => {
  beforeEach(() => {
    createRushWorkOrder.mockReset()
    createRushWorkOrder.mockResolvedValue({ data: { workOrderId: 'WO-20260928-000001' } })
    // 整页直开：壳层还没把组织与环境绑定到 filters，页面初始化时两者都是空串。
    rushState.filters = reactive({ organizationId: '', environmentId: '', skip: 0, take: 20 })
  })

  // #3858：表单初始化时拷贝了当时还是空串的组织与环境，之后绑定到位也不跟进，按钮一直不可点。
  it('picks up the business context bound after the page opened', async () => {
    const wrapper = mountPage()
    rushState.filters.organizationId = 'org-1'
    rushState.filters.environmentId = 'prod'
    await openAndFill(wrapper, { version: 'PV-1' })

    expect(submitButton(wrapper).attributes('disabled')).toBeUndefined()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(createRushWorkOrder).toHaveBeenCalledWith(
      expect.objectContaining({
        organizationId: 'org-1',
        environmentId: 'prod',
        skuId: 'FG-1',
        productionVersionId: 'PV-1',
        workCenterId: 'WC-1',
      }),
    )
  })

  // #3858：急单与计划工单一样要按生产版本出物料清单、冻结齐套需求，没选版本不让提交。
  it('requires a production version before submitting', async () => {
    const wrapper = mountPage()
    rushState.filters.organizationId = 'org-1'
    rushState.filters.environmentId = 'prod'
    await openAndFill(wrapper, {})

    expect(submitButton(wrapper).attributes('disabled')).toBeDefined()
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(createRushWorkOrder).not.toHaveBeenCalled()
  })
})
