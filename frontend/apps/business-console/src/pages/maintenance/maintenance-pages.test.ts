import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import InspectionsPage from './inspections.vue'
import WorkOrdersPage from './work-orders.vue'

// Hoisted mutable state — safe to reference from vi.mock factories (unlike const locals).
const state = vi.hoisted(() => ({
  query: { deviceAssetId: 'DEV-PRESS-01', sourceAlarmId: 'ALARM-9001' } as Record<string, string>,
  inspections: [] as Array<Record<string, unknown>>,
  workOrders: [] as Array<Record<string, unknown>>,
  createWorkOrder: vi.fn(async (_body: Record<string, unknown>) => ({})),
  completeWorkOrder: vi.fn(async (_id: string, _body: Record<string, unknown>) => ({})),
  recordInspection: vi.fn(async (_body: Record<string, unknown>) => ({})),
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  return {
    ...actual,
    useRoute: () => reactive({ query: state.query }),
  }
})

vi.mock('@/composables/useBusinessMaintenance', () => ({
  useMaintenanceWorkOrders: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 100 }),
    workOrders: computed(() => state.workOrders),
    workOrdersError: shallowRef(),
    workOrdersPending: shallowRef(false),
    workOrdersTotal: computed(() => 0),
    refreshWorkOrders: vi.fn(),
    createWorkOrder: state.createWorkOrder,
    createWorkOrderPending: shallowRef(false),
    createWorkOrderError: shallowRef(),
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
}))

// Worker directory (technician selector) + device-asset resources (device combobox)
// — both used by the work-orders create sheet.
vi.mock('@/composables/useBusinessMasterData', () => ({
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

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
}

// 页面用 useAuthStore 拿当前用户默认「点检人 / 开单人」——需要真 pinia + 登录 principal。
let pinia: Pinia
function mountOptions() {
  return { attachTo: document.body, global: { stubs, plugins: [pinia] } }
}

beforeEach(() => {
  document.body.innerHTML = ''
  state.createWorkOrder.mockClear()
  state.completeWorkOrder.mockClear()
  state.recordInspection.mockClear()
  state.query = { deviceAssetId: 'DEV-PRESS-01', sourceAlarmId: 'ALARM-9001' }
  state.inspections = []
  // 默认一张在保工单：既覆盖保修列渲染（在保 / 供应商），也让列表非空。
  state.workOrders = [
    {
      workOrderId: '11111111-2222-3333-4444-55555555abcd',
      deviceAssetId: 'DEV-PRESS-01',
      priority: 'high',
      status: 'open',
      openedAtUtc: '2026-07-06T00:00:00Z',
      warrantyStatus: 'in-warranty',
      warrantyExpiresOn: '2027-01-14',
      supplierPartnerCode: 'SUP-ACME',
    },
  ]
  pinia = createPinia()
  setActivePinia(pinia)
  useAuthStore().$patch({
    principal: { principalType: 'user', principalId: 'user-admin', loginName: 'admin' },
  })
})

describe('maintenance work orders page', () => {
  it('prefills maintenance work order creation from equipment alarm context', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    expect(document.body.textContent).toContain('新建维护工单')
    expect(document.body.textContent).toContain('在保')
    expect(document.body.textContent).toContain('SUP-ACME')
    expect(document.body.querySelector<HTMLInputElement>('#mwo-device')?.value).toBe('DEV-PRESS-01')
    expect(document.body.querySelector<HTMLInputElement>('#mwo-alarm')?.value).toBe('ALARM-9001')
  })

  it('offers a technician selector and estimated labor on the create sheet', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    // 指派技师 + 预估工时进入建单表单（技师是可靠性按技师聚合的前提）。
    expect(document.body.textContent).toContain('指派技师')
    expect(document.body.querySelector('#mwo-est-labor')).not.toBeNull()
  })

  // 回归：number 输入框经 v-model 可能回传 number；预估工时校验若对 number 调用
  // .trim() 会抛异常，令 submitCreate 静默失败、不发请求（真机走查发现）。
  it('submits create with a numeric estimated-labor value (no silent .trim() crash)', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    function setInput(selector: string, value: number | string) {
      const el = document.body.querySelector<HTMLInputElement>(selector)!
      el.value = String(value)
      el.dispatchEvent(new Event('input', { bubbles: true }))
    }
    setInput('#mwo-device', 'DEV-SMT-01')
    // 开单人不再自由输入：默认当前登录用户（admin），无需填写。
    // 直接以 number 赋值，复现 number 型 v-model 回传。
    setInput('#mwo-est-labor', 45)
    await flushPromises()

    const submit = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button[type="submit"]'),
    ].find((b) => b.textContent?.includes('创建维护工单'))!
    submit.click()
    await flushPromises()

    expect(state.createWorkOrder).toHaveBeenCalledTimes(1)
    const body = state.createWorkOrder.mock.calls[0][0]
    expect(body.deviceAssetId).toBe('DEV-SMT-01')
    expect(body.estimatedLaborMinutes).toBe(45)
    // 开单人默认解析为当前用户显示名（验证「默认当前用户」）。
    expect(body.openedBy).toBe('admin')
  })

  // 审核：备件成本人工覆盖为负/非法时必须拦截，不得发出负成本或静默丢字段。
  it('rejects a negative / non-numeric spare-part cost override on complete', async () => {
    state.query = {} // 不自动打开建单抽屉
    state.workOrders = [
      {
        workOrderId: 'wo-1',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
      },
    ]
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    // 通过行操作打开「完成工单」抽屉。
    const rowAction = document.body.querySelector<HTMLButtonElement>(
      '[aria-label^="维护工单操作"]',
    )!
    rowAction.click()
    await flushPromises()
    const completeItem = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (el) => el.textContent?.includes('完成工单'),
    )!
    completeItem.click()
    await flushPromises()

    const costInput = document.body.querySelector<HTMLInputElement>('#mwo-spare-cost')!
    costInput.value = '-1'
    costInput.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const form = costInput.closest('form')!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.completeWorkOrder).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('备件成本汇总需为非负数')
  })
})

describe('maintenance inspections page', () => {
  it('marks out-of-spec measurements on the list and surfaces them in the detail drawer', async () => {
    state.inspections = [
      {
        inspectionId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0001',
        planId: 'PLAN-1',
        workOrderId: null,
        inspector: '设备保全班',
        result: 'failed',
        inspectedAtUtc: '2026-06-10T08:00:00Z',
        measurements: [
          {
            characteristicCode: '轴承温度',
            measuredValue: 85,
            uomCode: '℃',
            lowerSpecLimit: 0,
            upperSpecLimit: 70,
            isWithinSpec: false,
          },
          {
            characteristicCode: '振动',
            measuredValue: 2,
            uomCode: 'mm/s',
            lowerSpecLimit: 0,
            upperSpecLimit: 5,
            isWithinSpec: true,
          },
        ],
      },
    ]
    const wrapper = mount(InspectionsPage, mountOptions())
    await flushPromises()

    // 列表上可见超差标记（1 项超差）。
    expect(wrapper.text()).toContain('1 项超差')

    // 打开记录详情 → 逐特性 pass/超差标记可见（验收①）。
    const trigger = document.body.querySelector<HTMLButtonElement>('[data-testid^="measurements-"]')
    expect(trigger).not.toBeNull()
    trigger!.click()
    await flushPromises()

    const outOfSpec = document.body.querySelector('[data-testid="detail-measurement-0"]')
    expect(outOfSpec?.getAttribute('data-out-of-spec')).toBe('true')
    expect(document.body.textContent).toContain('轴承温度')
    expect(document.body.textContent).toContain('共 1 项测量值超差')
  })

  it('shows no measurement chip when an inspection carries no measurements', async () => {
    state.inspections = [
      {
        inspectionId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0002',
        planId: 'PLAN-2',
        workOrderId: null,
        inspector: '巡检员',
        result: 'passed',
        inspectedAtUtc: '2026-06-11T08:00:00Z',
        measurements: [],
      },
    ]
    const wrapper = mount(InspectionsPage, mountOptions())
    await flushPromises()
    expect(wrapper.find('[data-testid^="measurements-"]').exists()).toBe(false)
  })

  it('defaults the inspector to the current user and makes characteristic a select (not free text)', async () => {
    mount(InspectionsPage, mountOptions())
    await flushPromises()

    const openBtn = [...document.body.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('记录点检'),
    )!
    openBtn.click()
    await flushPromises()

    // 点检人默认当前登录用户（admin），且是选择器按钮而非自由输入框。
    const inspector = document.body.querySelector('#insp-inspector')!
    expect(inspector.tagName).toBe('BUTTON')
    expect(inspector.textContent).toContain('admin')

    // 测量特性是下拉选择（button 触发），不再是自由输入。
    const characteristic = document.body.querySelector('[id^="m-char-"]')!
    expect(characteristic.tagName).toBe('BUTTON')
  })
})
