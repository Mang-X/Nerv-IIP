import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { vi } from 'vitest'
import { computed, nextTick, reactive, ref } from 'vue'

type OperationTaskFixture = Omit<BusinessConsoleMesOperationTaskRow, 'status'> & { status?: string }

const push = vi.fn()
const replace = vi.fn()
const andonSdk = vi.hoisted(() => ({ raise: vi.fn() }))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  raiseBusinessConsoleMesAndonCall: andonSdk.raise,
}))

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const routeGuardState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))
const routeState = vi.hoisted(() => ({
  query: {} as Record<string, string | undefined>,
  replaceQuery: undefined as ((query: Record<string, string | undefined>) => void) | undefined,
}))
vi.mock('vue-router', async () => {
  const { reactive } = await import('vue')
  const route = reactive({ query: routeState.query })
  routeState.replaceQuery = (query) => {
    route.query = query
    routeState.query = query
  }
  return {
    onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
      routeGuardState.guard = guard
    }),
    useRoute: () => route,
    useRouter: () => ({ push, replace }),
  }
})

// --- composable mock: 2 operation tasks with different statuses ---
type OperationActionContext = {
  principalId: string
  organizationId: string
  environmentId: string
  scopeKind: string
  scopeId: string
  action: 'claim' | 'start' | 'pause' | 'resume' | 'complete'
  workOrderId: string
  operationTaskId: string
}
type ActionOptions = {
  reasonCode?: string
  idempotencyKey: string
  context: OperationActionContext
}
const completeTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const startTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const pauseTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const resumeTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const claimTask = vi.fn(
  async (_workOrderId: string, _operationTaskId: string, _options: ActionOptions) => {},
)
const captureOperationActionContext = vi.fn(
  (action: OperationActionContext['action'], workOrderId: string, operationTaskId: string) => ({
    principalId: 'principal-001',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    scopeKind: 'work-center',
    scopeId: 'WC-A',
    action,
    workOrderId,
    operationTaskId,
  }),
)
const refresh = vi.fn(async () => {})
const refreshSops = vi.fn()
const openSopFileContent = vi.fn(async () => {})
// 页面现在直接从 business-core 取 openSopFileContent（#3314 删掉了 composable 里的转发包装），
// 所以失败注入点从 composable 搬到这里。business-core 的其余导出保持真实实现。
vi.mock('@nerv-iip/business-core', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/business-core')>()),
  openSopFileContent: (...args: unknown[]) => openSopFileContent(...(args as [])),
}))

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  workOrderId: undefined as string | undefined,
  operationTaskId: undefined as string | undefined,
})
const tasksErrorRef = ref<unknown>(null)
const sopsErrorRef = ref<unknown>(null)
const operationScopeMessageRef = ref('')
const operationScopeReadyRef = ref(true)
const operationListScopeRef = ref({
  kind: 'work-center',
  id: 'WC-A',
  displayName: '精加工一线',
})
const operationListContextIdentityRef = ref(
  'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A',
)
const operationActionContextIdentityRef = ref(
  'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A',
)

function isOperationActionContextCurrent(context: OperationActionContext) {
  return (
    operationActionContextIdentityRef.value ===
    [
      context.principalId,
      context.organizationId,
      context.environmentId,
      context.scopeKind,
      context.scopeId,
    ].join('\u0000')
  )
}

const defaultTasks: [OperationTaskFixture, OperationTaskFixture] = [
  {
    operationTaskId: 'OP-1',
    workOrderId: 'WO-2026-0001',
    workOrderNo: 'MO-2026-0001',
    operationTaskNo: null,
    status: 'InProgress',
    operationSequence: 10,
    operationCode: 'OP-CODE-1',
    workCenterId: 'WC-A',
    allowedActions: ['pause', 'complete'],
    blockReasons: [] as string[],
    evaluatedAtUtc: '2026-08-02T08:30:00.000Z',
  },
  {
    operationTaskId: 'OP-2',
    workOrderId: 'WO-2026-0002',
    workOrderNo: 'MO-2026-0002',
    operationTaskNo: null,
    status: 'Queued',
    operationSequence: 20,
    workCenterId: 'WC-B',
    allowedActions: ['start'],
    blockReasons: [] as string[],
    evaluatedAtUtc: '2026-08-02T08:31:00.000Z',
  },
]
const operationTasksRef = ref<OperationTaskFixture[]>(defaultTasks)
const tasksPendingRef = ref(false)
const tasksRefreshingRef = ref(false)
const tasksSuccessfulRef = ref(true)
const currentSopsRef = ref<Array<Record<string, unknown>>>([])
const workScopeOptionsRef = ref([
  { label: '精加工一线（工作中心）', value: 'work-center:WC-A' },
  { label: '精加工二线（工作中心）', value: 'work-center:WC-B' },
])
const workScopeSelectionRef = ref<string | undefined>('work-center:WC-A')

vi.mock('@/composables/mes', () => ({
  useMesOperationTasks: () => ({
    filters,
    operationTasks: computed(() => operationTasksRef.value),
    total: computed(() => operationTasksRef.value.length),
    hasSuccessfulResponse: computed(() => tasksSuccessfulRef.value && !tasksErrorRef.value),
    hasFailedResponse: computed(() => false),
    pending: tasksPendingRef,
    refreshing: tasksRefreshingRef,
    error: tasksErrorRef,
    refresh,
    startTask,
    pauseTask,
    resumeTask,
    completeTask,
    claimTask,
    actionPending: ref(false),
    operationListScope: operationListScopeRef,
    operationActionScope: operationListScopeRef,
    operationListContextIdentity: operationListContextIdentityRef,
    operationActionContextIdentity: operationActionContextIdentityRef,
    operationListScopeMessage: ref(''),
    operationListScopeReady: ref(true),
    operationScopeMessage: operationScopeMessageRef,
    operationScopePending: ref(false),
    operationScopeReady: operationScopeReadyRef,
    captureOperationActionContext,
    isOperationActionContextCurrent,
  }),
  useMesCurrentOperationSops: () => ({
    filters: {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      operationCode: '',
      workCenterCode: '',
    },
    currentSops: currentSopsRef,
    pending: ref(false),
    error: sopsErrorRef,
    refresh: refreshSops,
  }),
  // 作业范围选择入口自带一个独立实例（#1297）：页面挂载时必须能解析到它。
  useMesWorkScopeSelection: () => ({
    scopeOptions: computed(() => workScopeOptionsRef.value),
    scopeSelectionValue: workScopeSelectionRef,
  }),
}))

function dispatchBeforeUnload() {
  const event = new Event('beforeunload', { cancelable: true })
  window.dispatchEvent(event)
  return event
}

type PageDriftKind = 'route' | 'principal' | 'organization-environment' | 'manage-scope'
const pageDriftCases: Array<{ drift: PageDriftKind; label: string }> = [
  { drift: 'route', label: 'route pair' },
  { drift: 'principal', label: 'principal' },
  { drift: 'organization-environment', label: 'organization/environment' },
  { drift: 'manage-scope', label: 'manage scope' },
]
const staleOutcomes = ['success', 'error'] as const

async function applyPageDrift(drift: PageDriftKind) {
  if (drift === 'route') {
    routeState.replaceQuery?.({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-2',
    })
  } else if (drift === 'principal') {
    operationListContextIdentityRef.value =
      'principal-002\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A'
    operationActionContextIdentityRef.value =
      'principal-002\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A'
  } else if (drift === 'organization-environment') {
    filters.organizationId = 'org-002'
    filters.environmentId = 'env-qa'
    operationListContextIdentityRef.value =
      'principal-001\u0000org-002\u0000env-qa\u0000work-center\u0000WC-A'
    operationActionContextIdentityRef.value =
      'principal-001\u0000org-002\u0000env-qa\u0000work-center\u0000WC-A'
  } else {
    operationActionContextIdentityRef.value =
      'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-B'
  }
  await nextTick()
}

async function beginCompleteAction(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('[data-row]')[0].trigger('click')
  await flushPromises()
  document.body.querySelector<HTMLElement>('[data-testid="action-complete"]')!.click()
  await nextTick()
  document.body.querySelector<HTMLElement>('[data-testid="confirm-complete"]')!.click()
  await nextTick()
}

const resetFixture = () => {
  andonSdk.raise.mockReset().mockImplementation(async ({ body }) => ({
    data: {
      success: true,
      data: {
        ...body,
        id: 'andon-created-1',
        callerId: 'principal-001',
        status: 'open',
        raisedAtUtc: '2026-09-20T08:00:00Z',
      },
    },
  }))
  completeTask.mockReset().mockResolvedValue(undefined)
  startTask.mockReset().mockResolvedValue(undefined)
  pauseTask.mockReset().mockResolvedValue(undefined)
  resumeTask.mockReset().mockResolvedValue(undefined)
  claimTask.mockReset().mockResolvedValue(undefined)
  refresh.mockClear()
  refreshSops.mockClear()
  tasksErrorRef.value = null
  tasksRefreshingRef.value = false
  sopsErrorRef.value = null
  operationScopeMessageRef.value = ''
  operationScopeReadyRef.value = true
  operationListScopeRef.value = {
    kind: 'work-center',
    id: 'WC-A',
    displayName: '精加工一线',
  }
  operationListContextIdentityRef.value =
    'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A'
  operationActionContextIdentityRef.value =
    'principal-001\u0000org-001\u0000env-dev\u0000work-center\u0000WC-A'
  operationTasksRef.value = defaultTasks
  tasksPendingRef.value = false
  tasksSuccessfulRef.value = true
  currentSopsRef.value = []
  openSopFileContent.mockClear()
  push.mockReset().mockResolvedValue(undefined)
  replace.mockReset().mockImplementation(async (to: { query?: Record<string, string> }) => {
    routeState.replaceQuery?.(to.query ?? {})
  })
  routeGuardState.guard = undefined
  filters.keyword = undefined
  filters.organizationId = 'org-001'
  filters.environmentId = 'env-dev'
  filters.workOrderId = undefined
  filters.operationTaskId = undefined
  routeState.replaceQuery?.({})
}

export {
  routeGuardState,
  completeTask,
  claimTask,
  filters,
  defaultTasks,
  operationTasksRef,
  tasksRefreshingRef,
  dispatchBeforeUnload,
  resetFixture,
  routeState,
  operationScopeMessageRef,
  operationScopeReadyRef,
  operationListScopeRef,
  operationListContextIdentityRef,
  operationActionContextIdentityRef,
  tasksPendingRef,
  tasksSuccessfulRef,
  push,
  deferred,
  pauseTask,
  refresh,
  pageDriftCases,
  staleOutcomes,
  applyPageDrift,
  beginCompleteAction,
  andonSdk,
  startTask,
  refreshSops,
  openSopFileContent,
  tasksErrorRef,
  sopsErrorRef,
  currentSopsRef,
}
