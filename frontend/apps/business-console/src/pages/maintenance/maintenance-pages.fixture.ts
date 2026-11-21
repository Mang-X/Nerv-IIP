import { flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { beforeEach, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { i18n } from '@/i18n'

// Hoisted mutable state — safe to reference from vi.mock factories (unlike const locals).
const state = vi.hoisted(() => ({
  query: { deviceAssetId: 'DEV-PRESS-01', sourceAlarmId: 'ALARM-9001' } as Record<string, string>,
  inspections: [] as Array<Record<string, unknown>>,
  workOrders: [] as Array<Record<string, unknown>>,
  plans: [] as Array<Record<string, unknown>>,
  remainingByPlanId: {} as Record<string, { status: string; hours?: number }>,
  remainingPending: false,
  maintenanceFilters: { organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 },
  directory: { state: 'ok', message: '' },
  createWorkOrder: vi.fn(async (_body: Record<string, unknown>) => ({})),
  expectedRestoreScopes: [] as Array<{ kind: string; id: string; displayName?: string }>,
  updateExpectedRestore: vi.fn(async () => ({})),
  createReason: vi.fn(async (_body: Record<string, unknown>): Promise<unknown> => ({})),
  completeWorkOrder: vi.fn(async (_id: string, _body: Record<string, unknown>) => ({})),
  recordInspection: vi.fn(async (_body: Record<string, unknown>) => ({})),
  createPlan: vi.fn(async (_body: Record<string, unknown>) => ({})),
  updatePlan: vi.fn(async (_planId: string, _body: Record<string, unknown>) => ({})),
  generateDue: vi.fn(async (_payload: Record<string, unknown>) => ({
    data: { generatedCount: 0 },
  })),
  refreshPlans: vi.fn(async () => {}),
  refreshRemaining: vi.fn(async () => {}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  // 物料编码 → 基本单位；用例里改它来模拟物料列表晚一步刷新回来。
  baseUomBySku: undefined as unknown as { value: Map<string, string> },
}))

vi.mock('@nerv-iip/ui', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/ui')>()
  return {
    ...actual,
    toast: {
      ...actual.toast,
      success: state.toastSuccess,
      error: state.toastError,
    },
  }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  return {
    ...actual,
    useRoute: () => reactive({ query: state.query }),
  }
})

vi.mock('@/composables/maintenance/useBusinessMaintenance', () => ({
  useMaintenanceWorkOrders: () => ({
    filters: state.maintenanceFilters,
    workOrders: computed(() => state.workOrders),
    workOrdersError: shallowRef(),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: computed(() => 0),
    refreshWorkOrders: vi.fn(),
    createWorkOrder: state.createWorkOrder,
    createWorkOrderPending: shallowRef(false),
    createWorkOrderError: shallowRef(),
    readExpectedRestoreScopes: vi.fn(async () => state.expectedRestoreScopes),
    updateExpectedRestore: state.updateExpectedRestore,
    expectedRestorePending: shallowRef(false),
    completeWorkOrder: state.completeWorkOrder,
    completeWorkOrderPending: shallowRef(false),
    completeWorkOrderError: shallowRef(),
  }),
  useMaintenanceInspections: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    inspections: computed(() => state.inspections),
    inspectionsError: shallowRef(),
    inspectionsPending: shallowRef(false),
    inspectionsTotal: computed(() => state.inspections.length),
    refreshInspections: vi.fn(),
    recordInspection: state.recordInspection,
    recordInspectionPending: shallowRef(false),
    recordInspectionError: shallowRef(),
  }),
  useMaintenancePlans: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    plans: computed(() => state.plans),
    plansError: shallowRef(),
    plansPending: shallowRef(false),
    plansTotal: computed(() => state.plans.length),
    refreshPlans: state.refreshPlans,
    createPlan: state.createPlan,
    createPlanPending: shallowRef(false),
    createPlanError: shallowRef(),
    updatePlan: state.updatePlan,
    updatePlanPending: shallowRef(false),
    updatePlanError: shallowRef(),
    generateDue: state.generateDue,
    generateDuePending: shallowRef(false),
    generateDueError: shallowRef(),
  }),
}))

vi.mock('@/composables/maintenance/useMaintenanceDowntimeReasonDirectory', () => ({
  useMaintenanceDowntimeReasonDirectory: () => ({
    keyword: shallowRef(''),
    reasons: computed(() =>
      state.directory.state === 'ok'
        ? [
            {
              reasonCode: 'Line-A.Spindle',
              description: '主轴检修',
              reasonCategory: 'breakdown',
              lossCategory: 'availability',
            },
          ]
        : [],
    ),
    options: computed(() =>
      state.directory.state === 'ok' ? [{ value: 'Line-A.Spindle', label: '主轴检修' }] : [],
    ),
    state: computed(() => state.directory.state),
    message: computed(() => state.directory.message),
    total: computed(() => 1),
    refresh: vi.fn(),
  }),
}))

vi.mock('@/composables/maintenance/useMaintenanceDowntimeReasonMutations', () => ({
  useMaintenanceDowntimeReasonMutations: () => ({
    createReason: state.createReason,
    updateReason: vi.fn(),
    deleteReason: vi.fn(),
    saving: shallowRef(false),
    deleting: shallowRef(false),
  }),
}))

// Remaining runtime hours are derived client-side via the runtime-hours facade; mock the derivation so
// the plans list shows deterministic remaining values without a live telemetry backend.
vi.mock('@/composables/equipment/useBusinessTelemetry', () => ({
  useMaintenancePlanRuntimeRemaining: () => ({
    remainingByPlanId: computed(() => state.remainingByPlanId),
    remainingPending: computed(() => state.remainingPending),
    refreshRemaining: state.refreshRemaining,
  }),
}))

// Worker directory (technician selector) + device-asset resources (device combobox)
// — both used by the work-orders create sheet.
vi.mock('@/composables/master-data', () => ({
  useBusinessWorkers: () => ({
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      pageIndex: 1,
      pageSize: 100,
    }),
    refresh: vi.fn(),
    workers: computed(() => [
      { userId: 'user-admin', displayName: 'admin', employeeNo: 'A000', status: 'active' },
      { userId: 'user-1', displayName: '张工', employeeNo: 'E001', status: 'active' },
      { userId: 'user-2', displayName: '李工', employeeNo: 'E002', status: 'active' },
    ]),
    workersError: shallowRef(),
    workersPending: shallowRef(false),
    workersTotal: computed(() => 3),
  }),
  useBusinessMasterDataResources: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    refreshResources: vi.fn(),
    resources: computed(() => [
      { resourceType: 'device-asset', code: 'DEV-SMT-01', displayName: '贴片机 01', active: true },
    ]),
    resourcesError: shallowRef(),
    resourcesPending: shallowRef(false),
    resourcesTotal: computed(() => 1),
  }),
}))

// 设备 / 班组 / 单位 / 物料 / 单据目录都走真实读面（useQuery）；单测给确定目录，只验页面提交行为。
vi.mock('@/composables/equipment/useEquipmentPickerCatalog', () => ({
  useEquipmentAlarmCatalog: () => ({
    alarmOptions: computed(() => [{ value: 'ALARM-1', label: 'OVERHEAT' }]),
    alarmsPending: shallowRef(false),
  }),
  useEquipmentDeviceCatalog: () => ({
    deviceOptions: computed(() => [
      { value: 'DEV-SMT-01', label: '贴片机 01' },
      { value: 'DEV-PRESS-01', label: '压装机 01' },
    ]),
    devicesPending: shallowRef(false),
  }),
  useEquipmentSkuCatalog: () => {
    state.baseUomBySku ??= shallowRef(new Map([['BRG-6205', 'pcs']]))
    return { baseUomBySku: state.baseUomBySku }
  },
  useEquipmentTeamCatalog: () => ({
    teamOptions: computed(() => [{ value: '设备保全班', label: '设备保全班', hint: 'TEAM-PM' }]),
    teamsPending: shallowRef(false),
  }),
  useEquipmentUomCatalog: () => ({
    uomOptions: computed(() => [
      { value: 'pcs', label: '个' },
      { value: 'CEL', label: '摄氏度' },
    ]),
    uomsPending: shallowRef(false),
  }),
  maintenanceWorkOrderNoLabel: (no?: string | null) =>
    no && !/^[0-9a-f]{8}-[0-9a-f]{4}-/i.test(no) ? no : '维修工单',
  // 设备所在工厂：DEV-1 登记在 SITE-001，其余设备没有所属工厂。
  useDeviceSiteLookup: () => ({
    deviceSiteCode: (device?: string | null) => (device === 'DEV-1' ? 'SITE-001' : ''),
    siteLabel: (code: string) => (code === 'SITE-001' ? '一号工厂' : code),
    devicesPending: shallowRef(false),
  }),
  useMaintenanceDocumentCatalog: () => ({
    planOptions: computed(() => [{ value: 'plan-1', label: 'PM-SMT-01-M' }]),
    plansPending: shallowRef(false),
    workOrderOptions: computed(() => [{ value: 'wo-1', label: 'WO-00000001' }]),
    workOrdersPending: shallowRef(false),
  }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  // 实体选择弹窗是 reka portal；单测只关心取值，替成同 id 的输入位。
  NvEntityPicker: {
    props: ['modelValue', 'id', 'options', 'loading', 'disabled'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  // reka Tabs 的点击在 jsdom 下不更新 v-model；用 CustomEvent 冒泡的轻量 stub 驱动触发模式切换。
  // trigger 冒泡 settab 事件到 NvTabs 根节点，再 $emit update:modelValue，避开 provide/inject 的 slot 作用域问题。
  NvTabs: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: `<div @settab="$emit('update:modelValue', $event.detail)"><slot /></div>`,
  },
  NvTabsList: { template: '<div><slot /></div>' },
  NvTabsTrigger: {
    props: ['value'],
    methods: {
      emitSet() {
        ;(this as unknown as { $el: HTMLElement }).$el.dispatchEvent(
          new CustomEvent('settab', {
            bubbles: true,
            detail: (this as unknown as { value: string }).value,
          }),
        )
      },
    },
    template: `<button type="button" role="tab" :data-tab="value" @click="emitSet"><slot /></button>`,
  },
}

// 页面用 useAuthStore 拿当前用户默认「点检人 / 开单人」——需要真 pinia + 登录 principal。
let pinia: Pinia
function mountOptions() {
  return { attachTo: document.body, global: { stubs, plugins: [pinia] } }
}

beforeEach(() => {
  i18n.global.locale.value = 'zh-CN'
  state.maintenanceFilters = reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skip: 0,
    take: 100,
  })
  state.directory = reactive({ state: 'ok', message: '' })
  document.body.innerHTML = ''
  state.expectedRestoreScopes = [{ kind: 'self', id: 'user-admin' }]
  state.updateExpectedRestore.mockReset()
  state.updateExpectedRestore.mockResolvedValue({})
  state.createWorkOrder.mockClear()
  state.completeWorkOrder.mockClear()
  state.createReason.mockReset()
  state.createReason.mockResolvedValue({})
  state.recordInspection.mockClear()
  state.createPlan.mockClear()
  state.updatePlan.mockClear()
  state.updatePlan.mockResolvedValue({})
  state.generateDue.mockClear()
  state.refreshPlans.mockClear()
  state.refreshRemaining.mockClear()
  state.toastSuccess.mockClear()
  state.toastError.mockClear()
  state.query = { deviceAssetId: 'DEV-PRESS-01', sourceAlarmId: 'ALARM-9001' }
  state.inspections = []
  state.plans = []
  state.remainingByPlanId = {}
  state.remainingPending = false
  // 默认一张在保工单：既覆盖保修列渲染（在保 / 供应商），也让列表非空。
  state.workOrders = [
    {
      workOrderId: '11111111-2222-3333-4444-55555555abcd',
      workOrderNo: 'MWO-20260706-000001',
      deviceAssetId: 'DEV-PRESS-01',
      priority: 'high',
      status: 'open',
      openedAtUtc: '2026-07-06T00:00:00Z',
      warrantyStatus: 'in-warranty',
      warrantyExpiresOn: '2027-01-14',
      supplierPartnerCode: 'SUP-ACME',
      // 建单指派技师（#897：完工「实际技师」默认沿用它）。
      assignedTechnicianUserId: 'user-1',
    },
  ]
  pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().$patch({
    principal: {
      principalType: 'user',
      principalId: 'user-admin',
      loginName: 'admin',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.maintenance.work-orders.manage',
        'business.maintenance.plans.read',
        'business.maintenance.plans.manage',
      ],
    },
  })
})

/** 完工抽屉的必填项：停机原因从目录选、每条备件选领出库位（#3855 / #3902）。 */
async function fillCompleteReasonAndLocation(locationCode = 'loc-spare-01') {
  const reason = document.body.querySelector<HTMLInputElement>('#mwo-reason')!
  reason.value = 'Line-A.Spindle'
  reason.dispatchEvent(new Event('input', { bubbles: true }))
  for (const location of document.body.querySelectorAll<HTMLInputElement>(
    '[id^="spare-location-"]',
  )) {
    location.value = locationCode
    location.dispatchEvent(new Event('input', { bubbles: true }))
  }
  await flushPromises()
}

async function openCompleteSheet() {
  document.body.querySelector<HTMLButtonElement>('[aria-label^="维护工单操作"]')!.click()
  await flushPromises()
  ;[...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
    .find((el) => el.textContent?.includes('完成工单'))!
    .click()
  await flushPromises()
}

/** 表头里有没有「操作」这一列。 */
function hasActionsColumn() {
  return [...document.body.querySelectorAll('th')].some((th) => th.textContent?.trim() === '操作')
}

/** 只读角色（如财务专员）：两块维护读权限都有，manage 一个都没有。 */
function signInAsMaintenanceReader() {
  useAuthStore().$patch({
    principal: {
      principalType: 'user',
      principalId: 'user-reader',
      loginName: 'reader',
      permissionCodes: ['business.maintenance.work-orders.read', 'business.maintenance.plans.read'],
    },
  })
}

export {
  state,
  mountOptions,
  openCompleteSheet,
  hasActionsColumn,
  signInAsMaintenanceReader,
  fillCompleteReasonAndLocation,
}
