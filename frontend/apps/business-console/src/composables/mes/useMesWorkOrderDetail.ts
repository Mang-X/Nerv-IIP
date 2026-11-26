import {
  BusinessOperationUnconfirmedError,
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  returnBusinessConsoleMesLineSideMaterialMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  getBusinessConsoleMesMaterialReadinessQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequests,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  type BusinessConsoleMesConfirmLineSideReceiptRequest,
  type BusinessConsoleMesReturnLineSideMaterialRequest,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesMaterialIssueRequestListEnvelope,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesMaterialReadinessEnvelope,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
  type BusinessConsoleMesWorkOrderDetailEnvelope,
  type BusinessConsoleMesWorkOrderDetailResponse,
} from '@nerv-iip/api-client'
import { useScopeBoundListResponse } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, shallowRef, watch, toValue, type MaybeRefOrGetter } from 'vue'
import { hasBusinessContext } from '../shared/businessContextBinding'
import { businessReadState } from '../shared/businessReadState'
import {
  CANCEL_PREVIEW_PAGE_SIZE,
  MES_WORK_ORDERS_MANAGE_PERMISSION,
  MES_WORK_ORDERS_READ_PERMISSION,
  defaultWorkOrderContext,
  envelopeItems,
  fetchAllCompensationItems,
  ignoreBackgroundError,
  invalidateMesQueries,
  isNonEmpty,
  toContextQuery,
  unwrapData,
} from './mesShared'
import { useMesPrincipalWorkScope } from './useMesWorkScope'
import { useMesWorkOrderLifecycle } from './useMesWorkOrderLifecycle'

export function useMesWorkOrderDetail(workOrderId?: MaybeRefOrGetter<string>) {
  const filters = defaultWorkOrderContext()
  // 路由入口在查询创建前提供对象身份，避免同一详情再进入时先命中空 ID 再切到缓存。
  if (workOrderId !== undefined) {
    watch(
      () => toValue(workOrderId),
      (value) => {
        filters.workOrderId = value
      },
      { immediate: true },
    )
  }
  const workOrderReadScope = useMesPrincipalWorkScope(filters, MES_WORK_ORDERS_READ_PERMISSION)
  const auth = useAuthStore()
  const workOrderManageScope = useMesPrincipalWorkScope(
    filters,
    MES_WORK_ORDERS_MANAGE_PERMISSION,
    () => auth.principal?.permissionCodes?.includes(MES_WORK_ORDERS_MANAGE_PERMISSION) === true,
  )
  const queryCache = useQueryCache()
  const detailEnabled = computed(
    () =>
      hasBusinessContext(filters) &&
      workOrderReadScope.scopeReady.value &&
      isNonEmpty(filters.workOrderId),
  )
  const detailIdentity = computed(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    return [
      workOrderReadScope.principalIdentity.value,
      filters.organizationId.trim(),
      filters.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
      filters.workOrderId.trim(),
    ].join(':')
  })

  // 完工入库请求预览只在打开「取消工单」补偿预览时才拉取，避免每次进详情页多打一次列表请求。
  const cancelPreviewRequested = shallowRef(false)
  const receiptPreviewEnabled = computed(() => detailEnabled.value && cancelPreviewRequested.value)

  const detailQuery = useQuery(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    const options = getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: {
        ...toContextQuery(filters),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${detailIdentity.value}`],
      enabled: detailEnabled.value,
    }
  })
  const detailResponse = useScopeBoundListResponse(
    () => detailQuery.data.value,
    detailIdentity,
    detailEnabled,
  )

  // 服务端按 workOrderId 过滤（facade/底层 MES 均支持）+ 完整分页取全。取消 handler 会处理该工单的全部
  // 关联单据，预览与 toast 也必须取全，故不能固定 take/只取一页——否则单工单 >一页 时仍会少算。
  const receiptQuery = useQuery(() => {
    const scope = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      workOrderId: filters.workOrderId,
    }
    return {
      key: listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
        query: { ...scope, skip: 0, take: CANCEL_PREVIEW_PAGE_SIZE },
      }).key,
      query: async () => {
        const items = await fetchAllCompensationItems<BusinessConsoleMesReceiptRequestRow>(
          async (skip, take) => {
            const { data } = await listBusinessConsoleMesFinishedGoodsReceiptRequests({
              query: { ...scope, skip, take },
              throwOnError: true,
            })
            // throwOnError 只处理非 2xx；服务可能以 HTTP 200 返回 success:false 信封。此时必须抛错，
            // 让 useQuery.error → cancelPreviewError 生效并禁用确认，而非合成空页 + success:true 放行破坏性取消。
            if (data?.success !== true || !data.data) {
              throw new Error(data?.message ?? '完工入库补偿预览请求失败')
            }
            return data.data.items ?? []
          },
        )
        return {
          success: true,
          data: { items, total: items.length },
        } as BusinessConsoleMesReceiptRequestListEnvelope
      },
      enabled: receiptPreviewEnabled.value,
    }
  })

  // 领料申请是取消补偿的权威来源：取消 handler 遍历本工单的领料申请——已收料→退料指引，未收料→释放，
  // 与齐套快照（material_requirements，仅在有已发布 MBOM 时才有）解耦，无 MBOM 的工单也能正确汇总。
  const materialIssueQuery = useQuery(() => {
    const scope = {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      workOrderId: filters.workOrderId,
    }
    return {
      key: listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
        query: { ...scope, skip: 0, take: CANCEL_PREVIEW_PAGE_SIZE },
      }).key,
      query: async () => {
        const items = await fetchAllCompensationItems<BusinessConsoleMesMaterialIssueRequestRow>(
          async (skip, take) => {
            const { data } = await listBusinessConsoleMesMaterialIssueRequests({
              query: { ...scope, skip, take },
              throwOnError: true,
            })
            // 同上：HTTP 200 + success:false 也必须抛错，避免在失败/空预览上放行破坏性取消。
            if (data?.success !== true || !data.data) {
              throw new Error(data?.message ?? '领料补偿预览请求失败')
            }
            return data.data.items ?? []
          },
        )
        return {
          success: true,
          data: { items, total: items.length },
        } as BusinessConsoleMesMaterialIssueRequestListEnvelope
      },
      // 领料申请不再只服务取消补偿：工单详情的「领料与收料」区常驻读这份清单（#1324），
      // 所以随详情读面一起启用，而不是等到打开取消预览才拉。
      enabled: detailEnabled.value,
    }
  })

  // 收料回执只表示受理；库存 CAP 回执前仍是 ReceiptPosting，不能用此时的旧齐套当终态。
  const receiptReadbacks = shallowRef<Array<{ identity: string; requestId: string }>>([])
  const receiptReadbackActive = computed(() =>
    receiptReadbacks.value.some((item) => item.identity === detailIdentity.value),
  )
  const materialQuery = useQuery(() => {
    const identity = detailIdentity.value
    const options = getBusinessConsoleMesMaterialReadinessQueryOptions({
      path: { workOrderId: filters.workOrderId },
      query: toContextQuery(filters),
    })
    return {
      ...options,
      enabled: detailEnabled.value,
      query: async (context: Parameters<typeof options.query>[0]) => {
        const readbacks = receiptReadbacks.value.filter((item) => item.identity === identity)
        let posting = false
        if (readbacks.length) {
          await materialIssueQuery.refetch(true)
          posting = readbacks
            .map((readback) => {
              const row = materialIssueQuery.data.value?.data?.items?.find(
                (item) => item.requestId === readback.requestId,
              )
              if (!row) throw new Error('未取得本次收料结果，请重试读取。')
              return row.status === 'ReceiptPosting'
            })
            .some(Boolean)
        }
        const result = await options.query(context)
        if (result?.success !== true || !result.data) {
          throw new Error(result?.message || '齐套读取失败，请重试。')
        }
        if (readbacks.length && !posting)
          receiptReadbacks.value = receiptReadbacks.value.filter(
            (item) => !readbacks.includes(item),
          )
        return result
      },
      // 沿用应用现有 auto-refetch 插件；只等待本次收料过账，读取失败停止，交由用户重试读取。
      autoRefetch: (state: { status: string }) =>
        receiptReadbackActive.value && state.status !== 'error' ? 1000 : false,
    }
  })

  const createMaterialIssueMutation = useMutation(
    createBusinessConsoleMesMaterialIssueRequestMutationOptions(),
  )
  const confirmLineSideReceiptMutation = useMutation(
    confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions(),
  )
  const returnLineSideMaterialMutation = useMutation(
    returnBusinessConsoleMesLineSideMaterialMutationOptions(),
  )
  const refreshMaterialIssueQueries = (refetchActive = true) =>
    invalidateMesQueries(
      queryCache,
      [
        'listBusinessConsoleMesMaterialIssueRequests',
        'getBusinessConsoleMesMaterialReadiness',
        'getBusinessConsoleMesWorkOrderDetail',
      ],
      refetchActive,
    )

  const lifecycle = useMesWorkOrderLifecycle(filters, workOrderManageScope, queryCache)

  return {
    activateCancelPreview: () => {
      cancelPreviewRequested.value = true
    },
    ...lifecycle,
    // 补偿预览两项查询的加载/失败/就绪态，供破坏性确认按钮门禁：两项都成功拿到数据前禁用确认，失败可重试。
    cancelPreviewPending: computed(
      () =>
        receiptPreviewEnabled.value &&
        (receiptQuery.isLoading.value || materialIssueQuery.isLoading.value),
    ),
    cancelPreviewError: computed(() => receiptQuery.error.value ?? materialIssueQuery.error.value),
    cancelPreviewReady: computed(
      () =>
        receiptPreviewEnabled.value &&
        !receiptQuery.isLoading.value &&
        !materialIssueQuery.isLoading.value &&
        receiptQuery.error.value == null &&
        materialIssueQuery.error.value == null &&
        receiptQuery.data.value !== undefined &&
        materialIssueQuery.data.value !== undefined,
    ),
    retryCancelPreview: () => {
      void receiptQuery.refetch()
      void materialIssueQuery.refetch()
    },
    detail: computed<BusinessConsoleMesWorkOrderDetailResponse | undefined>(() =>
      unwrapData<
        BusinessConsoleMesWorkOrderDetailResponse,
        BusinessConsoleMesWorkOrderDetailEnvelope
      >(detailResponse.value),
    ),
    detailError: detailQuery.error,
    detailPending: detailQuery.isLoading,
    detailState: businessReadState(detailQuery, () => detailEnabled.value),
    filters,
    workOrderReadScope: workOrderReadScope.selectedScope,
    workOrderReadScopeMessage: workOrderReadScope.scopeMessage,
    workOrderReadScopePending: workOrderReadScope.scopePending,
    workOrderReadScopeReady: workOrderReadScope.scopeReady,
    workOrderManageScope: workOrderManageScope.selectedScope,
    workOrderManageScopeMessage: workOrderManageScope.scopeMessage,
    workOrderManageScopePending: workOrderManageScope.scopePending,
    workOrderManageScopeReady: workOrderManageScope.scopeReady,
    // 按关联单据前端汇总：该工单下未终结的完工入库请求（后端暂无取消预览端点，PR 已注明降级实现）
    finishedGoodsReceiptRequests: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(receiptQuery.data.value),
    ),
    // 本工单的领料申请（补偿预览的预留释放/退料指引权威来源，PR 已注明降级实现）
    materialIssueRequests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(materialIssueQuery.data.value),
    ),
    materialReadiness: computed(() =>
      unwrapData<
        NonNullable<BusinessConsoleMesMaterialReadinessEnvelope['data']>,
        BusinessConsoleMesMaterialReadinessEnvelope
      >(materialQuery.data.value),
    ),
    materialReadinessError: materialQuery.error,
    materialReadinessPending: computed(
      () => materialQuery.isLoading.value || receiptReadbackActive.value,
    ),
    materialReadinessState: businessReadState(materialQuery, () => detailEnabled.value),
    refreshDetail: () => (detailEnabled.value ? detailQuery.refetch() : Promise.resolve()),
    refreshMaterialReadiness: () =>
      detailEnabled.value ? materialQuery.refetch() : Promise.resolve(),
    refreshMaterialIssueRequests: () =>
      detailEnabled.value ? materialIssueQuery.refetch() : Promise.resolve(),
    materialIssueRequestsPending: materialIssueQuery.isLoading,
    materialIssueRequestsError: materialIssueQuery.error,
    // 发起领料 / 线边收料：与 PDA 同一组网关面（#1324），PC 形态放在工单详情的齐套区。
    createMaterialIssueRequest: async (body: BusinessConsoleMesCreateMaterialIssueRequest) => {
      const result = await createMaterialIssueMutation.mutateAsync({
        path: { workOrderId: filters.workOrderId },
        query: toContextQuery(filters),
        body,
      })
      await refreshMaterialIssueQueries()
      return result
    },
    createMaterialIssueRequestPending: createMaterialIssueMutation.isLoading,
    confirmLineSideReceipt: async (
      requestId: string,
      body: BusinessConsoleMesConfirmLineSideReceiptRequest,
    ) => {
      const identity = detailIdentity.value
      const result = await confirmLineSideReceiptMutation.mutateAsync({
        path: { requestId },
        query: toContextQuery(filters),
        body,
      })
      if (result?.success !== true || result.data?.accepted !== true) {
        throw new BusinessOperationUnconfirmedError(result?.message || '未取得收料受理回执。')
      }
      receiptReadbacks.value = [...receiptReadbacks.value, { identity, requestId }]
      // 受理结果与读回结果分开：失败保留 query.error，用户只需重试读取。
      await refreshMaterialIssueQueries(false).catch(ignoreBackgroundError)
      if (detailIdentity.value === identity) {
        void materialQuery.refetch().catch(ignoreBackgroundError)
        void detailQuery.refetch().catch(ignoreBackgroundError)
      }
      return result
    },
    confirmLineSideReceiptPending: confirmLineSideReceiptMutation.isLoading,
    returnLineSideMaterial: async (
      requestId: string,
      body: BusinessConsoleMesReturnLineSideMaterialRequest,
    ) => {
      const result = await returnLineSideMaterialMutation.mutateAsync({
        path: { requestId },
        query: toContextQuery(filters),
        body,
      })
      await refreshMaterialIssueQueries()
      return result
    },
    returnLineSideMaterialPending: returnLineSideMaterialMutation.isLoading,
  }
}
