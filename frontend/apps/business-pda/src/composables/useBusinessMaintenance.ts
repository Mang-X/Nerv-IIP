import {
  createBusinessConsoleMaintenanceWorkOrderMutationOptions,
  confirmBusinessConsoleOperation,
  listBusinessConsoleMaintenanceWorkOrders,
  listBusinessConsoleMaintenanceInspectionsQueryOptions,
  listBusinessConsoleMaintenancePlansQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
  type BusinessConsoleCreateMaintenanceWorkOrderRequest as CreateMaintenanceWorkOrderRequest,
  type BusinessConsoleMaintenanceInspectionItem as MaintenanceInspectionItem,
  type BusinessConsoleMaintenanceWorkOrderItem as MaintenanceWorkOrderItem,
  type BusinessConsoleRecordMaintenanceInspectionRequest as RecordMaintenanceInspectionRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  completePendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { canAccessMaintenanceWorkOrderReadModel } from '@/permissions/maintenanceReadModelAccess'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { confirmedMaintenanceCreateWorkOrderId } from './maintenanceCreateReceipt'
import { useTaskListPagination } from './useTaskListPagination'

const WORK_ORDER_PAGE_SIZE = 20
const AUXILIARY_LIST_TAKE = 100
export interface MaintenanceListFilters {
  status?: string
  keyword?: string
  skip: number
  take: number
}

/**
 * 调用方传入的报修工单入参——org/env/openedBy 由 composable 注入，调用方不可覆盖
 * （`Omit` 收窄 + 注入后置，见 `createWorkOrder`）。
 */
export type CreateWorkOrderInput = Omit<
  CreateMaintenanceWorkOrderRequest,
  'organizationId' | 'environmentId' | 'openedBy'
>

/**
 * 调用方传入的点检入参——org/env/inspector/inspectedAtUtc 由 composable 注入，调用方不可覆盖。
 */
export type RecordInspectionInput = Omit<
  RecordMaintenanceInspectionRequest,
  'organizationId' | 'environmentId' | 'inspector' | 'inspectedAtUtc'
>

function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  if (!envelope?.success) {
    return []
  }
  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean; data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }
  return envelope.data?.total ?? 0
}

type ListEnvelope<TItem> =
  | { success?: boolean; data?: { items?: TItem[]; total?: number } | null }
  | undefined

/**
 * 设备运维（CMMS）数据封装：报修工单 create/list + 点检 record/list + 保养计划 list。
 *
 * - org/env 取登录主体 `useAuthStore().principal`（PDA 无 business-context store）；
 *   scope 空（未登录 / 缺 org/env）时所有 list 不发请求（`enabled:false`）。
 * - `openedBy`/`inspector` = `principal.loginName`；`inspectedAtUtc` = 提交时刻。
 * - 报修建单由页面铸造意图级 `idempotencyKey`，超时重试复用；只有服务端 confirmed
 *   receipt 才算成功，HTTP 200 本身不会结束该意图。
 * - 注入字段（org/env/openedBy/inspector/inspectedAtUtc）后置展开 + `Omit` 收窄入参，
 *   调用方无法覆盖（见各 create body）。
 */
export function useBusinessMaintenance() {
  const auth = useAuthStore()

  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const loginName = computed(() => auth.principal?.loginName ?? '')
  const permissionCodes = computed(() => new Set(auth.principal?.permissionCodes ?? []))
  const canReadWorkOrderDetail = computed(() =>
    canAccessMaintenanceWorkOrderReadModel(permissionCodes.value),
  )
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))
  const scopeKey = computed(() => `${organizationId.value.trim()}:${environmentId.value.trim()}`)

  const workOrderFilters = reactive<MaintenanceListFilters>({ skip: 0, take: WORK_ORDER_PAGE_SIZE })
  const inspectionFilters = reactive<MaintenanceListFilters>({ skip: 0, take: AUXILIARY_LIST_TAKE })
  const planFilters = reactive<MaintenanceListFilters>({ skip: 0, take: AUXILIARY_LIST_TAKE })

  const scopedQuery = (filters: MaintenanceListFilters) => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
    skip: filters.skip,
    take: filters.take,
  })

  const workOrdersQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenanceWorkOrdersQueryOptions({
      query: {
        ...scopedQuery(workOrderFilters),
        status: workOrderFilters.status || undefined,
        keyword: workOrderFilters.keyword || undefined,
      },
    }),
    enabled: scopeReady.value,
  }))

  const inspectionsQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenanceInspectionsQueryOptions({
      query: scopedQuery(inspectionFilters),
    }),
    enabled: scopeReady.value,
  }))

  const plansQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenancePlansQueryOptions({ query: scopedQuery(planFilters) }),
    enabled: scopeReady.value,
  }))
  const workOrdersResponse = useScopeBoundListResponse(
    () => workOrdersQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const inspectionsResponse = useScopeBoundListResponse(
    () => inspectionsQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const plansResponse = useScopeBoundListResponse(() => plansQuery.data.value, scopeKey, scopeReady)
  const workOrdersLastUpdatedAt = useListFreshness(workOrdersResponse, scopeReady)
  const inspectionsLastUpdatedAt = useListFreshness(inspectionsResponse, scopeReady)
  const plansLastUpdatedAt = useListFreshness(plansResponse, scopeReady)
  const {
    hasSuccessfulResponse: workOrdersHasSuccessfulResponse,
    hasFailedResponse: workOrdersHasFailedResponse,
  } = useListResponseState(workOrdersResponse, scopeReady, workOrdersQuery.isLoading)
  const {
    hasSuccessfulResponse: inspectionsHasSuccessfulResponse,
    hasFailedResponse: inspectionsHasFailedResponse,
  } = useListResponseState(inspectionsResponse, scopeReady, inspectionsQuery.isLoading)
  const {
    hasSuccessfulResponse: plansHasSuccessfulResponse,
    hasFailedResponse: plansHasFailedResponse,
  } = useListResponseState(plansResponse, scopeReady, plansQuery.isLoading)

  const plansTotal = computed(() => listTotal(plansResponse.value as ListEnvelope<unknown>))
  const workOrderIdentity = computed(
    () => `${scopeKey.value}:${workOrderFilters.status ?? ''}:${workOrderFilters.keyword ?? ''}`,
  )
  const firstWorkOrderPage = computed(() => {
    const envelope = workOrdersResponse.value as ListEnvelope<MaintenanceWorkOrderItem>
    if (envelope?.success !== true) return undefined
    return { items: envelope.data?.items ?? [], total: envelope.data?.total ?? 0 }
  })
  const workOrderPager = useTaskListPagination<MaintenanceWorkOrderItem>({
    identity: workOrderIdentity,
    firstPage: firstWorkOrderPage,
    pageSize: WORK_ORDER_PAGE_SIZE,
    itemKey: (item) => item.workOrderId ?? '',
    fetchPage: async ({ skip, take }) => {
      const { data } = await listBusinessConsoleMaintenanceWorkOrders({
        query: {
          organizationId: organizationId.value,
          environmentId: environmentId.value,
          status: workOrderFilters.status || undefined,
          keyword: workOrderFilters.keyword || undefined,
          skip,
          take,
        },
        throwOnError: true,
      })
      const envelope = data as ListEnvelope<MaintenanceWorkOrderItem>
      if (envelope?.success !== true) throw new Error('维修工单下一页加载失败，请重试。')
      return { items: envelope.data?.items ?? [], total: envelope.data?.total ?? 0 }
    },
    refreshFirstPage: workOrdersQuery.refetch,
  })

  const createMutation = useMutation({
    ...createBusinessConsoleMaintenanceWorkOrderMutationOptions(),
    onSuccess() {
      void workOrdersQuery.refetch()
    },
  })

  const recordMutation = useMutation({
    ...recordBusinessConsoleMaintenanceInspectionMutationOptions(),
    onSuccess() {
      void inspectionsQuery.refetch()
    },
  })

  async function createWorkOrder(input: CreateWorkOrderInput) {
    // scope 未就绪（未登录 / 缺 org/env）时绝不发请求：否则 org/env='' 会被
    // BusinessGateway 拒为 400 或落到错误租户。调用页已 try/catch，抛错即可呈现。
    if (!scopeReady.value) {
      throw new Error('登录态未就绪，请稍后重试')
    }
    // 注入后置：即使调用方（`as never`）混入 org/env/openedBy，也被这里覆盖。
    const { idempotencyKey: suppliedKey, ...intent } = input
    const scope = {
      principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      operationType: 'maintenance.work-order.create',
      payloadFingerprint: JSON.stringify(intent),
    }
    const { idempotencyKey } = acquirePendingBusinessIntent(
      scope,
      () =>
        suppliedKey?.trim() ||
        globalThis.crypto?.randomUUID?.() ||
        `maintenance-create-${Date.now()}-${Math.random()}`,
    )
    const body = {
      ...input,
      idempotencyKey,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      openedBy: loginName.value,
    } satisfies CreateMaintenanceWorkOrderRequest
    return completePendingBusinessIntent(scope, async () => {
      const response = await createMutation.mutateAsync({ body })
      const workOrderId = confirmedMaintenanceCreateWorkOrderId(response)
      const receipt = response.data!.operationReceipt!
      const normalizedResponse = {
        ...response,
        data: {
          ...response.data!,
          workOrderId,
          operationReceipt: { ...receipt, resourceId: workOrderId },
        },
      }
      return confirmBusinessConsoleOperation(normalizedResponse, {
        expectedOperationType: 'maintenance.work-order.create',
        expectedIdempotencyKey: idempotencyKey,
        expectedResourceIdSelector: () => workOrderId,
      })
    })
  }

  async function recordInspection(input: RecordInspectionInput) {
    if (!scopeReady.value) {
      throw new Error('登录态未就绪，请稍后重试')
    }
    const body = {
      ...input,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      inspector: loginName.value,
      inspectedAtUtc: new Date().toISOString(),
    } satisfies RecordMaintenanceInspectionRequest
    return recordMutation.mutateAsync({ body })
  }

  // 保养计划无服务端 keyword/device 过滤（仅 org/env/skip/take），inspect 页
  // 客户端扫码过滤；当扫码命中第一页之外时，调用方据 plansTotal 加载更多页。
  function loadMorePlans() {
    if (planFilters.take < plansTotal.value) {
      planFilters.take += AUXILIARY_LIST_TAKE
    }
  }

  return {
    organizationId,
    environmentId,
    scopeReady,
    canReadWorkOrderDetail,
    workOrders: workOrderPager.items,
    workOrdersTotal: workOrderPager.total,
    workOrdersLoaded: workOrderPager.loaded,
    workOrdersHasMore: workOrderPager.hasMore,
    workOrdersLoadingMore: workOrderPager.loadingMore,
    workOrdersRefreshing: workOrderPager.refreshing,
    workOrdersLoadMoreError: workOrderPager.loadMoreError,
    loadMoreWorkOrders: workOrderPager.loadMore,
    workOrdersPending: workOrdersQuery.isLoading,
    workOrdersError: workOrdersQuery.error,
    workOrdersLastUpdatedAt,
    workOrdersHasSuccessfulResponse,
    workOrdersHasFailedResponse,
    refreshWorkOrders: () => (scopeReady.value ? workOrderPager.refresh() : Promise.resolve()),
    workOrderFilters,
    createWorkOrder,
    createPending: createMutation.isLoading,

    inspections: computed<MaintenanceInspectionItem[]>(() =>
      listItems<MaintenanceInspectionItem>(
        inspectionsResponse.value as ListEnvelope<MaintenanceInspectionItem>,
      ),
    ),
    inspectionsTotal: computed(() =>
      listTotal(inspectionsResponse.value as ListEnvelope<MaintenanceInspectionItem>),
    ),
    inspectionsPending: inspectionsQuery.isLoading,
    inspectionsError: inspectionsQuery.error,
    inspectionsLastUpdatedAt,
    inspectionsHasSuccessfulResponse,
    inspectionsHasFailedResponse,
    refreshInspections: () => (scopeReady.value ? inspectionsQuery.refetch() : Promise.resolve()),
    inspectionFilters,
    recordInspection,
    recordPending: recordMutation.isLoading,

    plans: computed(() => listItems(plansResponse.value as ListEnvelope<unknown>)),
    plansTotal,
    plansPending: plansQuery.isLoading,
    plansError: plansQuery.error,
    plansLastUpdatedAt,
    plansHasSuccessfulResponse,
    plansHasFailedResponse,
    refreshPlans: () => (scopeReady.value ? plansQuery.refetch() : Promise.resolve()),
    planFilters,
    loadMorePlans,
  }
}
