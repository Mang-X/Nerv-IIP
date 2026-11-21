import { flushPromises, mount } from '@vue/test-utils'
import { expect, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

// ---- vue-router mock（默认无 query；个别用例覆写 useRoute）---------------------
const push = vi.fn(() => Promise.resolve())
const route = reactive({ query: {} as Record<string, string> })
vi.mock('vue-router', () => ({
  useRouter: () => ({ push }),
  useRoute: () => route,
}))

// ---- useBusinessMaintenance mock ----------------------------------------------
const createWorkOrder = vi.fn(async (_input: Record<string, unknown>) => ({}))
const createPending = ref(false)
const canReadWorkOrderDetail = ref(true)
const confirmedCreatedWorkOrder = ref<Record<string, unknown>>()
const createdDetailHasSuccessfulResponse = ref(false)
const createdDetailPending = ref(false)
const createdDetailHasFailedResponse = ref(false)
const createdDeviceHasFailedResponse = ref(false)
const refreshCreatedWorkOrder = vi.fn(async () => {})
const workOrders = ref<Array<Record<string, unknown>>>([
  {
    workOrderId: '11111111-1111-1111-1111-111111111111',
    deviceAssetId: 'DEV-1001',
    priority: 'high',
    status: 'open',
    openedAtUtc: '2026-06-10T08:00:00Z',
  },
  {
    workOrderId: '22222222-2222-2222-2222-222222222222',
    deviceAssetId: 'DEV-2002',
    priority: 'planned',
    status: 'completed',
    openedAtUtc: '2026-06-09T10:30:00Z',
  },
])
const workOrdersError = ref<unknown>(null)
const workOrdersPending = ref(false)
const workOrdersRefreshing = ref(false)
const organizationId = ref('org-001')
const environmentId = ref('env-dev')
const scopeReady = ref(true)
const refreshWorkOrders = vi.fn(async () => {})
const loadMoreWorkOrders = vi.fn(async () => {})

vi.mock('@/composables/maintenance/useBusinessMaintenance', () => ({
  useBusinessMaintenance: () => ({
    workOrders,
    workOrdersTotal: computed(() => workOrders.value.length),
    workOrdersLoaded: computed(() => workOrders.value.length),
    workOrdersLoadingMore: ref(false),
    workOrdersLoadMoreError: ref<unknown>(),
    loadMoreWorkOrders,
    organizationId,
    environmentId,
    scopeReady,
    workOrdersHasSuccessfulResponse: computed(
      () => !workOrdersPending.value && !workOrdersError.value,
    ),
    workOrdersHasFailedResponse: computed(() => false),
    workOrdersPending,
    workOrdersRefreshing,
    workOrdersError,
    refreshWorkOrders,
    workOrderFilters: reactive({ skip: 0, take: 20, status: undefined, keyword: undefined }),
    createWorkOrder,
    createPending,
    canReadWorkOrderDetail,
  }),
}))

vi.mock('@/composables/maintenance', () => ({
  useMaintenanceSelfWorkOrderDetail: () => ({
    authoritativeWorkOrder: confirmedCreatedWorkOrder,
    authoritativeHasSuccessfulResponse: createdDetailHasSuccessfulResponse,
    authoritativePending: createdDetailPending,
    authoritativeHasFailedResponse: createdDetailHasFailedResponse,
    deviceHasFailedResponse: createdDeviceHasFailedResponse,
    refresh: refreshCreatedWorkOrder,
  }),
}))

vi.mock('@/components/equipment/DeviceAssetPicker.vue', () => ({
  default: {
    name: 'DeviceAssetPicker',
    props: ['open'],
    emits: ['update:open', 'select'],
    template: '<div v-if="open" data-testid="device-picker-stub" />',
  },
}))

// ---- 停机原因目录 mock（DowntimeReasonPicker 本体不打桩：抽屉里的错误态与「不登记」
//      路径正是本票要证明的行为，桩掉就只剩页面自说自话）--------------------------------
type DirectoryOption = { code: string; name: string; label: string }
// 目录码大小写是**有意义的**：Maintenance 只 trim（`MaintenanceText.Required`），
// 不改大小写（对照 `DowntimeReason.ReasonCategory` 才 `ToLowerInvariant()`），
// 所以混合大小写码是后端真造得出的数据，可以承担"原样提交"的鉴别力。
const DIRECTORY_OPTIONS: DirectoryOption[] = [
  { code: 'Hyd-Leak_01', name: '液压泄漏', label: '液压泄漏（Hyd-Leak_01）' },
  { code: 'Spindle-Noise', name: '主轴异响', label: '主轴异响（Spindle-Noise）' },
]
const reasonOptions = ref<DirectoryOption[]>([])
const reasonDirectoryState = ref('ok')
const reasonDirectoryMessage = ref('')
const reasonsTotal = ref(0)
const searchReasons = vi.fn()
const refreshReasons = vi.fn(async () => {})

vi.mock('@/composables/maintenance/useMaintenanceDowntimeReasonDirectory', () => ({
  useMaintenanceDowntimeReasonDirectory: () => ({
    reasonOptions,
    reasonsTotal,
    reasonsTruncated: computed(
      () => reasonDirectoryState.value === 'ok' && reasonsTotal.value > reasonOptions.value.length,
    ),
    state: reasonDirectoryState,
    stateMessage: reasonDirectoryMessage,
    canSelectReason: computed(() => reasonDirectoryState.value === 'ok'),
    search: searchReasons,
    refreshReasons,
  }),
}))

const createdWorkOrderId = '33333333-3333-3333-3333-333333333333'

function confirmedCreateResponse(resourceId = createdWorkOrderId) {
  return {
    success: true,
    data: {
      workOrderId: createdWorkOrderId,
      operationReceipt: {
        operationType: 'maintenance.work-order.create',
        resourceType: 'maintenance-work-order',
        resourceId,
        outcome: 'confirmed',
        stateConfirmed: true,
        accepted: false,
        idempotencyKey: 'page-composable-confirmed',
      },
    },
  }
}

async function selectPriority(wrapper: ReturnType<typeof mount>, label: '高' | '中' | '低') {
  await wrapper.get('[data-testid="priority-trigger"]').trigger('click')
  await flushPromises()
  const option = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
    (button) => button.textContent?.trim() === label,
  )
  expect(option).toBeTruthy()
  option!.click()
  await flushPromises()
}

function lastCreateBody(): Record<string, unknown> {
  const call = createWorkOrder.mock.calls.at(-1)
  expect(call).toBeTruthy()
  return call![0]
}

/** 打开停机原因抽屉并按可见名称选中一行（抽屉经 teleport 挂在 body 上）。 */
async function selectReason(wrapper: ReturnType<typeof mount>, visibleText: string) {
  await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
  await flushPromises()
  const row = [
    ...document.body.querySelectorAll<HTMLElement>('[data-slot="mobile-sheet-content"] [data-row]'),
  ].find((element) => element.textContent?.includes(visibleText))
  expect(row, `未找到停机原因行：${visibleText}`).toBeTruthy()
  row!.click()
  await flushPromises()
}

/**
 * 按 generated client 失败时交给页面的形态包装一个网关响应体：
 * `client.gen.ts` 把响应文本 `JSON.parse` 后直接 throw，
 * `configureApiClient` 的 error 拦截器再把原始 `Response` 以**非枚举**属性挂上去。
 */
function asThrownByGeneratedClient(rawBody: string): unknown {
  const error = JSON.parse(rawBody) as Record<string, unknown>
  Object.defineProperty(error, 'response', {
    configurable: true,
    enumerable: false,
    value: { status: 400 },
  })
  return error
}

const resetFixture = () => {
  push.mockClear()
  reasonOptions.value = [...DIRECTORY_OPTIONS]
  reasonDirectoryState.value = 'ok'
  reasonDirectoryMessage.value = ''
  reasonsTotal.value = DIRECTORY_OPTIONS.length
  searchReasons.mockClear()
  refreshReasons.mockClear()
  createWorkOrder.mockClear()
  createWorkOrder.mockResolvedValue(confirmedCreateResponse())
  refreshWorkOrders.mockClear()
  route.query = {}
  createPending.value = false
  canReadWorkOrderDetail.value = true
  confirmedCreatedWorkOrder.value = undefined
  createdDetailHasSuccessfulResponse.value = false
  createdDetailPending.value = false
  createdDetailHasFailedResponse.value = false
  createdDeviceHasFailedResponse.value = false
  refreshCreatedWorkOrder.mockReset()
  workOrdersError.value = null
  workOrdersPending.value = false
  workOrdersRefreshing.value = false
}

export {
  route,
  createWorkOrder,
  createPending,
  workOrders,
  workOrdersError,
  workOrdersRefreshing,
  selectPriority,
  selectReason,
  resetFixture,
  push,
  canReadWorkOrderDetail,
  confirmedCreatedWorkOrder,
  createdDetailHasSuccessfulResponse,
  createdDeviceHasFailedResponse,
  refreshCreatedWorkOrder,
  asThrownByGeneratedClient,
  DIRECTORY_OPTIONS,
  reasonOptions,
  reasonDirectoryState,
  reasonDirectoryMessage,
  reasonsTotal,
  searchReasons,
  refreshReasons,
  lastCreateBody,
}
