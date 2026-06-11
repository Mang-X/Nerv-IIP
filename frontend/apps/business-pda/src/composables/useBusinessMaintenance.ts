import {
  createBusinessConsoleMaintenanceWorkOrderMutationOptions,
  listBusinessConsoleMaintenanceInspectionsQueryOptions,
  listBusinessConsoleMaintenancePlansQueryOptions,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
  type BusinessConsoleCreateMaintenanceWorkOrderRequest as CreateMaintenanceWorkOrderRequest,
  type BusinessConsoleMaintenanceInspectionItem as MaintenanceInspectionItem,
  type BusinessConsoleMaintenanceWorkOrderItem as MaintenanceWorkOrderItem,
  type BusinessConsoleRecordMaintenanceInspectionRequest as RecordMaintenanceInspectionRequest,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface MaintenanceListFilters {
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

function listItems<TItem>(envelope: { success?: boolean, data?: { items?: TItem[] } | null } | undefined) {
  if (!envelope?.success) {
    return []
  }
  return envelope.data?.items ?? []
}

function listTotal(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) {
    return 0
  }
  return envelope.data?.total ?? 0
}

type ListEnvelope<TItem> = { success?: boolean, data?: { items?: TItem[], total?: number } | null } | undefined

/**
 * 设备运维（CMMS）数据封装：报修工单 create/list + 点检 record/list + 保养计划 list。
 *
 * - org/env 取登录主体 `useAuthStore().principal`（PDA 无 business-context store）；
 *   scope 空（未登录 / 缺 org/env）时所有 list 不发请求（`enabled:false`）。
 * - `openedBy`/`inspector` = `principal.loginName`；`inspectedAtUtc` = 提交时刻。
 * - Maintenance 端点**无服务端幂等**，故不携带 idempotencyKey；防重交由 UI 层（pending 禁用）。
 * - 注入字段（org/env/openedBy/inspector/inspectedAtUtc）后置展开 + `Omit` 收窄入参，
 *   调用方无法覆盖（见各 create body）。
 */
export function useBusinessMaintenance() {
  const auth = useAuthStore()

  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const loginName = computed(() => auth.principal?.loginName ?? '')
  const scopeReady = computed(() => Boolean(organizationId.value && environmentId.value))

  const workOrderFilters = reactive<MaintenanceListFilters>({ skip: 0, take: DEFAULT_TAKE })
  const inspectionFilters = reactive<MaintenanceListFilters>({ skip: 0, take: DEFAULT_TAKE })
  const planFilters = reactive<MaintenanceListFilters>({ skip: 0, take: DEFAULT_TAKE })

  const scopedQuery = (filters: MaintenanceListFilters) => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
    skip: filters.skip,
    take: filters.take,
  })

  const workOrdersQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenanceWorkOrdersQueryOptions({ query: scopedQuery(workOrderFilters) }),
    enabled: scopeReady.value,
  }))

  const inspectionsQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenanceInspectionsQueryOptions({ query: scopedQuery(inspectionFilters) }),
    enabled: scopeReady.value,
  }))

  const plansQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenancePlansQueryOptions({ query: scopedQuery(planFilters) }),
    enabled: scopeReady.value,
  }))

  const plansTotal = computed(() => listTotal(plansQuery.data.value as ListEnvelope<unknown>))

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
    const body = {
      ...input,
      organizationId: organizationId.value,
      environmentId: environmentId.value,
      openedBy: loginName.value,
    } satisfies CreateMaintenanceWorkOrderRequest
    return createMutation.mutateAsync({ body })
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
      planFilters.take += DEFAULT_TAKE
    }
  }

  return {
    workOrders: computed<MaintenanceWorkOrderItem[]>(() =>
      listItems<MaintenanceWorkOrderItem>(workOrdersQuery.data.value as ListEnvelope<MaintenanceWorkOrderItem>),
    ),
    workOrdersTotal: computed(() => listTotal(workOrdersQuery.data.value as ListEnvelope<MaintenanceWorkOrderItem>)),
    workOrdersPending: workOrdersQuery.isLoading,
    workOrdersError: workOrdersQuery.error,
    refreshWorkOrders: () => (scopeReady.value ? workOrdersQuery.refetch() : Promise.resolve()),
    workOrderFilters,
    createWorkOrder,
    createPending: createMutation.isLoading,

    inspections: computed<MaintenanceInspectionItem[]>(() =>
      listItems<MaintenanceInspectionItem>(inspectionsQuery.data.value as ListEnvelope<MaintenanceInspectionItem>),
    ),
    inspectionsTotal: computed(() => listTotal(inspectionsQuery.data.value as ListEnvelope<MaintenanceInspectionItem>)),
    inspectionsPending: inspectionsQuery.isLoading,
    inspectionsError: inspectionsQuery.error,
    refreshInspections: () => (scopeReady.value ? inspectionsQuery.refetch() : Promise.resolve()),
    inspectionFilters,
    recordInspection,
    recordPending: recordMutation.isLoading,

    plans: computed(() => listItems(plansQuery.data.value as ListEnvelope<unknown>)),
    plansTotal,
    plansPending: plansQuery.isLoading,
    plansError: plansQuery.error,
    refreshPlans: () => (scopeReady.value ? plansQuery.refetch() : Promise.resolve()),
    planFilters,
    loadMorePlans,
  }
}
