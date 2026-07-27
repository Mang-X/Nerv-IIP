import {
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleQualityInspectionTasksQueryOptions,
  listBusinessConsoleWmsCountExecutionsQueryOptions,
  listBusinessConsoleWmsInboundOrdersQueryOptions,
  listBusinessConsoleWmsPickingTasksQueryOptions,
  listBusinessConsoleWmsPutawayTasksQueryOptions,
  listBusinessConsoleWorkersQueryOptions,
  type BusinessConsoleMesDispatchTaskRow,
  type BusinessConsoleQualityInspectionTaskItem,
  type BusinessConsoleWorkerDirectoryItem,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'

import { useAuthStore } from '@/stores/auth'

/**
 * 工作台首页数据封装：按登录人权限裁剪各域摘要。
 *
 * - 权限判定用 `principal.permissionCodes`（真实授权来自网关逐请求校验，这里只决定
 *   首页渲染哪些板块，避免无权限板块发起注定 403 的请求）。
 * - 「我的任务」走派工读面 `assignedUserId = principalId` 服务端过滤（非客户端筛页，
 *   不受 take 截断漏计）。
 * - 各域空 scope（未登录 / principal 缺 org/env）时一律不发请求。
 */

/** 首页各板块的权限门槛（与 BusinessGateway 各 facade 实际要求一致）。 */
export const HOME_PERMISSIONS = {
  myTasks: 'business.mes.dispatch.read',
  workerProfile: 'business.masterdata.resources.read',
  wmsReceipts: 'business.wms.receipts.read',
  wmsShipments: 'business.wms.shipments.read',
  quality: 'business.quality.inspection-records.read',
  alarms: 'business.iiot.alarms.read',
} as const

/** 视为「进行中/待处理」的派工任务状态（终态 Completed/Cancelled 不上首页）。 */
const OPEN_DISPATCH_STATUSES = new Set(['Queued', 'InProgress', 'Paused', 'ScheduleInvalidated'])

const HOME_TAKE = 100

/** 与 useBusinessMes 同款：以宽对象注入可选查询参数（生成类型的 status 联合是文档性收窄）。 */
function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

export function usePdaIdentity() {
  const auth = useAuthStore()
  const principalId = computed(() => auth.principal?.principalId ?? '')
  const loginName = computed(() => auth.principal?.loginName ?? '')
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const permissionCodes = computed(() => new Set(auth.principal?.permissionCodes ?? []))
  const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
  const can = (code: string) => permissionCodes.value.has(code)

  // 工人档案（姓名/工号/岗位/班组）来自 MasterData 员工目录；无档案（如 admin）时回落登录名。
  const workerQuery = useQuery(() => ({
    ...listBusinessConsoleWorkersQueryOptions({
      query: {
        organizationId: organizationId.value,
        environmentId: environmentId.value,
        userId: principalId.value,
        pageIndex: 1,
        pageSize: 1,
      },
    }),
    enabled: hasScope.value && Boolean(principalId.value) && can(HOME_PERMISSIONS.workerProfile),
  }))

  const worker = computed<BusinessConsoleWorkerDirectoryItem | undefined>(() => {
    const envelope = workerQuery.data.value
    if (!envelope?.success) return undefined
    return envelope.data?.items?.[0]
  })

  return {
    principalId,
    loginName,
    organizationId,
    environmentId,
    hasScope,
    can,
    worker,
    displayName: computed(() => worker.value?.displayName || loginName.value),
  }
}

export function useMyDispatchTasks() {
  const identity = usePdaIdentity()
  const enabled = computed(
    () =>
      identity.hasScope.value &&
      Boolean(identity.principalId.value) &&
      identity.can(HOME_PERMISSIONS.myTasks),
  )

  // 按状态分查（Queued / InProgress / Paused 各一查）：列表默认按最早开始时间升序，
  // 工人名下的历史已完成任务量很大（L1 引擎按班组回填），无状态过滤时 take 窗口会被
  // 旧完成任务淹没、在办任务反而查不到；分查后计数直接用服务端 total（准确不受截断影响）。
  const statusQuery = (status: string) =>
    useQuery(() => ({
      ...listBusinessConsoleMesDispatchTasksQueryOptions({
        query: {
          organizationId: identity.organizationId.value,
          environmentId: identity.environmentId.value,
          assignedUserId: identity.principalId.value,
          ...optionalQuery('status', status),
          skip: 0,
          take: HOME_TAKE,
        },
      }),
      enabled: enabled.value,
    }))

  const queuedQuery = statusQuery('Queued')
  const inProgressQuery = statusQuery('InProgress')
  const pausedQuery = statusQuery('Paused')

  // 运行时状态是 MES 侧 `Status.ToString()` 的 PascalCase 直传（生成类型上的 camelCase
  // 联合是文档性收窄，与线上负载不符），这里统一宽化为 string 比较——与 mes/operation 页同惯例。
  const statusOf = (task: BusinessConsoleMesDispatchTaskRow): string => task.status ?? ''

  const openTasks = computed<BusinessConsoleMesDispatchTaskRow[]>(() =>
    [
      ...listItems<BusinessConsoleMesDispatchTaskRow>(inProgressQuery.data.value),
      ...listItems<BusinessConsoleMesDispatchTaskRow>(pausedQuery.data.value),
      ...listItems<BusinessConsoleMesDispatchTaskRow>(queuedQuery.data.value),
    ]
      // 服务端 assignedUserId 过滤为主；行级再校验一次（旧网关忽略未知参数时不误显他人任务）。
      .filter((task) => task.assignedUserId === identity.principalId.value)
      .filter((task) => OPEN_DISPATCH_STATUSES.has(statusOf(task)))
      .sort((a, b) => {
        // 进行中/暂停靠前，其后按排产时间升序。
        const rank = (t: BusinessConsoleMesDispatchTaskRow) =>
          statusOf(t) === 'InProgress' ? 0 : statusOf(t) === 'Paused' ? 1 : 2
        const byRank = rank(a) - rank(b)
        if (byRank !== 0) return byRank
        return (a.scheduledAtUtc ?? a.plannedStartUtc ?? '').localeCompare(
          b.scheduledAtUtc ?? b.plannedStartUtc ?? '',
        )
      }),
  )

  return {
    enabled,
    openTasks,
    queuedCount: computed(() => listTotal(queuedQuery.data.value)),
    inProgressCount: computed(
      () => listTotal(inProgressQuery.data.value) + listTotal(pausedQuery.data.value),
    ),
    pending: computed(
      () =>
        queuedQuery.isLoading.value ||
        inProgressQuery.isLoading.value ||
        pausedQuery.isLoading.value,
    ),
    error: queuedQuery.error,
    refresh: () =>
      Promise.all([queuedQuery.refetch(), inProgressQuery.refetch(), pausedQuery.refetch()]),
  }
}

export interface WarehouseSummaryEntry {
  key: 'inbound' | 'putaway' | 'pick' | 'count'
  label: string
  route: string
  count: number
}

export function useWarehouseSummary() {
  const identity = usePdaIdentity()
  const canReceipts = computed(
    () => identity.hasScope.value && identity.can(HOME_PERMISSIONS.wmsReceipts),
  )
  const canShipments = computed(
    () => identity.hasScope.value && identity.can(HOME_PERMISSIONS.wmsShipments),
  )
  const enabled = computed(() => canReceipts.value || canShipments.value)

  const scopeQuery = () => ({
    organizationId: identity.organizationId.value,
    environmentId: identity.environmentId.value,
    status: 'Open',
    skip: 0,
    take: 1,
  })

  const inboundQuery = useQuery(() => ({
    ...listBusinessConsoleWmsInboundOrdersQueryOptions({ query: scopeQuery() }),
    enabled: canReceipts.value,
  }))
  const putawayQuery = useQuery(() => ({
    ...listBusinessConsoleWmsPutawayTasksQueryOptions({ query: scopeQuery() }),
    enabled: canReceipts.value,
  }))
  const pickingQuery = useQuery(() => ({
    ...listBusinessConsoleWmsPickingTasksQueryOptions({ query: scopeQuery() }),
    enabled: canShipments.value,
  }))
  const countQuery = useQuery(() => ({
    ...listBusinessConsoleWmsCountExecutionsQueryOptions({ query: scopeQuery() }),
    enabled: canReceipts.value,
  }))

  const entries = computed<WarehouseSummaryEntry[]>(() => {
    const result: WarehouseSummaryEntry[] = []
    if (canReceipts.value) {
      result.push({
        key: 'inbound',
        label: '待收货',
        route: '/wms/inbound',
        count: listTotal(inboundQuery.data.value),
      })
      result.push({
        key: 'putaway',
        label: '待上架',
        route: '/wms/putaway',
        count: listTotal(putawayQuery.data.value),
      })
    }
    if (canShipments.value) {
      result.push({
        key: 'pick',
        label: '待拣货',
        route: '/wms/pick',
        count: listTotal(pickingQuery.data.value),
      })
    }
    if (canReceipts.value) {
      result.push({
        key: 'count',
        label: '待盘点',
        route: '/wms/count',
        count: listTotal(countQuery.data.value),
      })
    }
    return result
  })

  return {
    enabled,
    entries,
    pending: computed(
      () =>
        inboundQuery.isLoading.value ||
        putawayQuery.isLoading.value ||
        pickingQuery.isLoading.value ||
        countQuery.isLoading.value,
    ),
  }
}

export function usePendingInspectionSummary() {
  const identity = usePdaIdentity()
  const enabled = computed(() => identity.hasScope.value && identity.can(HOME_PERMISSIONS.quality))

  const tasksQuery = useQuery(() => ({
    ...listBusinessConsoleQualityInspectionTasksQueryOptions({
      query: {
        organizationId: identity.organizationId.value,
        environmentId: identity.environmentId.value,
        status: 'pending',
        skip: 0,
        take: HOME_TAKE,
      },
    }),
    enabled: enabled.value,
  }))

  return {
    enabled,
    tasks: computed<BusinessConsoleQualityInspectionTaskItem[]>(() =>
      listItems<BusinessConsoleQualityInspectionTaskItem>(tasksQuery.data.value),
    ),
    total: computed(() => listTotal(tasksQuery.data.value)),
    pending: tasksQuery.isLoading,
  }
}
