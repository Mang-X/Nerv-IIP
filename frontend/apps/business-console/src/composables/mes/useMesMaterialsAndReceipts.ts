import {
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesLineSideInventoryBalancesQueryOptions,
  listBusinessConsoleMesReceivableProducedLotsQueryOptions,
  listBusinessConsoleMesHistoricalProducedLotsQueryOptions,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesCreateReceiptRequest,
  type BusinessConsoleMesMaterialIssueRequestListEnvelope,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesLineSideInventoryBalanceItem,
  type BusinessConsoleMesLineSideInventoryBalancesEnvelope,
  type BusinessConsoleMesReceivableProducedLotListEnvelope,
  type BusinessConsoleMesReceivableProducedLotRow,
  type BusinessConsoleMesHistoricalProducedLotRow,
  type BusinessConsoleMesHistoricalProducedLotListEnvelope,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
} from '@nerv-iip/api-client'
import {
  createServerPaginationState,
  lastPageForTotal,
  reduceServerPagination,
  serverPaginationIdentity,
} from '@nerv-iip/business-core'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import {
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import { businessReadState } from '../shared/businessReadState'
import {
  defaultContext,
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  ignoreBackgroundError,
  invalidateMesQueries,
  isNonEmpty,
  makeIdempotencyKey,
  toListQuery,
  type MesContextFilters,
} from './mesShared'

export function useMesMaterialIssueRequests() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()

  const requestsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )

  const createRequestMutation = useMutation({
    ...createBusinessConsoleMesMaterialIssueRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesMaterialIssueRequests',
        'getBusinessConsoleMesMaterialReadiness',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    createMaterialIssueRequest: (
      workOrderId: string,
      context: MesContextFilters,
      body: BusinessConsoleMesCreateMaterialIssueRequest,
    ) =>
      createRequestMutation.mutateAsync({
        path: { workOrderId },
        query: { organizationId: context.organizationId, environmentId: context.environmentId },
        body,
      }),
    createMaterialIssueRequestPending: createRequestMutation.isLoading,
    filters,
    materialIssueRequests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(requestsQuery.data.value),
    ),
    materialIssueRequestsError: requestsQuery.error,
    materialIssueRequestsPending: requestsQuery.isLoading,
    materialIssueRequestsState: businessReadState(requestsQuery, () => hasBusinessContext(filters)),
    materialIssueRequestsTotal: computed(() => envelopeTotal(requestsQuery.data.value)),
    refreshMaterialIssueRequests: () => refetchWithBusinessContext(filters, requestsQuery),
  }
}

export function useMesLineSideInventoryBalances() {
  const pageSize = 200
  const filters = defaultContext()
  const queryCache = useQueryCache()
  const scopeIdentity = computed(() =>
    hasBusinessContext(filters)
      ? `${filters.organizationId.trim()}:${filters.environmentId.trim()}`
      : '',
  )
  const pagination = shallowRef(createServerPaginationState(pageSize, scopeIdentity.value))
  const page = computed(() => pagination.value.page)
  watch(
    scopeIdentity,
    (identity) => {
      pagination.value = reduceServerPagination(pagination.value, {
        type: 'scope-changed',
        scopeIdentity: identity,
      })
    },
    { flush: 'sync' },
  )
  const responseIdentity = computed(() => serverPaginationIdentity(scopeIdentity.value, page.value))
  function queryOptions(targetPage: number) {
    const requestIdentity = serverPaginationIdentity(scopeIdentity.value, targetPage)
    const { key, query: executeQuery } =
      listBusinessConsoleMesLineSideInventoryBalancesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          page: targetPage,
          pageSize,
        },
      })
    return withBusinessContextEnabled(
      {
        key: [...key, `scope-page:${requestIdentity}`],
        query: async (context: Parameters<typeof executeQuery>[0]) => ({
          identity: requestIdentity,
          response: await executeQuery(context),
        }),
      },
      filters,
    )
  }
  const query = useQuery(() => queryOptions(page.value))
  const currentResponse = computed(() => {
    const scopedResponse = query.data.value
    if (scopedResponse?.identity !== responseIdentity.value) return undefined
    const response = scopedResponse.response
    if (response?.success === true && (response.data?.page ?? 1) !== page.value) return undefined
    return response
  })
  const responsePageMismatch = computed(() => {
    const scopedResponse = query.data.value
    if (scopedResponse?.identity !== responseIdentity.value) return false
    const response = scopedResponse.response
    return response?.success === true && (response.data?.page ?? 1) !== page.value
  })
  watch(
    currentResponse,
    (response) => {
      if (response?.success === true) {
        const nextPagination = reduceServerPagination(pagination.value, {
          type: 'response-succeeded',
          identity: responseIdentity.value,
          responsePage: response.data?.page ?? 1,
          total: response.data?.totalCount ?? 0,
        })
        if (nextPagination.page !== pagination.value.page) {
          // A correction can return to a still-fresh cached page. Mark only that target stale,
          // then let the reactive key transition remain the single fetch trigger.
          void queryCache.invalidateQueries(
            { key: queryOptions(nextPagination.page).key, exact: true },
            false,
          )
        }
        pagination.value = nextPagination
      }
    },
    { flush: 'sync', immediate: true },
  )
  const pending = computed(() => query.isLoading.value || pagination.value.navigationPending)
  const state = businessReadState(
    { data: currentResponse, error: query.error, isLoading: pending },
    () => hasBusinessContext(filters),
  )
  const failure = computed(() =>
    query.error.value != null
      ? query.error.value
      : responsePageMismatch.value
        ? new Error('线边库存响应页码与请求不一致，请重试。')
        : currentResponse.value !== undefined && currentResponse.value.success !== true
          ? new Error(currentResponse.value.message ?? '线边库存服务未返回成功结果，请重试。')
          : null,
  )
  const total = computed(() => pagination.value.lastSuccessfulTotal)
  const pageCount = computed(() => lastPageForTotal(total.value, pageSize))
  const hasPreviousPage = computed(() => page.value > 1)
  const hasNextPage = computed(
    () => currentResponse.value?.success === true && page.value < pageCount.value,
  )

  watch(
    [page, currentResponse, responsePageMismatch, () => query.error.value],
    ([currentPage, response, pageMismatch, error]) => {
      if (
        error != null ||
        pageMismatch ||
        (response !== undefined &&
          (response.success !== true || (response.data?.page ?? 1) === currentPage))
      ) {
        pagination.value = reduceServerPagination(pagination.value, {
          type: 'response-failed',
          identity: responseIdentity.value,
        })
      }
    },
    { flush: 'sync' },
  )

  function previousPage() {
    if (pending.value || !hasPreviousPage.value) return
    pagination.value = reduceServerPagination(pagination.value, {
      type: 'navigate',
      targetPage: page.value - 1,
      pageCount: pageCount.value,
    })
  }

  function nextPage() {
    if (pending.value || !hasNextPage.value) return
    pagination.value = reduceServerPagination(pagination.value, {
      type: 'navigate',
      targetPage: page.value + 1,
      pageCount: pageCount.value,
    })
  }

  function goToPage(targetPage: number) {
    if (
      pending.value ||
      !Number.isInteger(targetPage) ||
      targetPage < 1 ||
      targetPage > pageCount.value ||
      targetPage === page.value
    )
      return
    pagination.value = reduceServerPagination(pagination.value, {
      type: 'navigate',
      targetPage,
      pageCount: pageCount.value,
    })
  }

  return {
    lineSideInventoryBalances: computed<BusinessConsoleMesLineSideInventoryBalanceItem[]>(() =>
      envelopeItems<
        BusinessConsoleMesLineSideInventoryBalanceItem,
        BusinessConsoleMesLineSideInventoryBalancesEnvelope
      >(currentResponse.value),
    ),
    lineSideInventoryTotal: total,
    lineSideInventoryPage: page,
    lineSideInventoryPageSize: pageSize,
    lineSideInventoryPageCount: pageCount,
    lineSideInventoryHasPreviousPage: hasPreviousPage,
    lineSideInventoryHasNextPage: hasNextPage,
    lineSideInventoryPending: pending,
    lineSideInventoryError: failure,
    lineSideInventoryReady: computed(() => state.value === 'ready'),
    previousLineSideInventoryPage: previousPage,
    nextLineSideInventoryPage: nextPage,
    goToLineSideInventoryPage: goToPage,
    refreshLineSideInventory: () => refetchWithBusinessContext(filters, query),
  }
}

export interface MesWorkOrderProducedLot {
  producedLotNo: string
  reportNo?: string
  goodQuantity: number
  // 剩余可入库量（读面已过滤耗尽批次）：Console 据此提示并按剩余限制登记数量。
  remainingQuantity: number
  serialNo?: string
}

// 追溯目录读取历史产出事实，不按剩余可入库量筛选；入库表单继续使用下方候选查询。
export function useMesWorkOrderHistoricalProducedLots(workOrderId: () => string) {
  const filters = defaultFilters()
  const enabled = () => hasBusinessContext(filters) && isNonEmpty(workOrderId().trim())
  const query = useQuery(() => ({
    ...listBusinessConsoleMesHistoricalProducedLotsQueryOptions({
      path: { workOrderId: workOrderId().trim() },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: enabled(),
  }))
  const producedLotsState = businessReadState(query, enabled)
  const producedLots = computed(() =>
    producedLotsState.value === 'ready'
      ? envelopeItems<
          BusinessConsoleMesHistoricalProducedLotRow,
          BusinessConsoleMesHistoricalProducedLotListEnvelope
        >(query.data.value)
      : [],
  )
  return {
    producedLots,
    producedLotsState,
    producedLotsError: query.error,
    producedLotsPending: query.isLoading,
    refreshProducedLots: () => refetchWithBusinessContext(filters, query),
  }
}

// 工单的真实产出批次来源：完工入库创建端点强制引用 MES 已生成的产出批次
// （CreateFinishedGoodsReceiptRequestCommandHandler 在数量校验之前即拒绝空/不存在的 producedLotNo，并校验其存在于
// OutputLotGenealogies）。故直接消费权威端点 listBusinessConsoleMesReceivableProducedLots（读同一张 OutputLotGenealogies、
// 与完工入库同域 receipts.read 权限）：①报工冲销会删除对应 genealogy → 已冲销批次天然不出现，不会选中后端已判定不存在的批次；
// ②权限与本页/创建一致，避免入库操作员因缺 reporting.read 而 403。页面据此从工单真实产出中选择，不伪造批次号。
export function useMesWorkOrderProducedLots(workOrderId: () => string) {
  const filters = defaultFilters()

  const producedLotsQuery = useQuery(() => {
    const workOrderIdValue = workOrderId().trim()
    return {
      ...listBusinessConsoleMesReceivableProducedLotsQueryOptions({
        path: { workOrderId: workOrderIdValue },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
      }),
      enabled: hasBusinessContext(filters) && isNonEmpty(workOrderIdValue),
    }
  })

  const producedLots = computed<MesWorkOrderProducedLot[]>(() => {
    if (!isNonEmpty(workOrderId().trim())) return []
    const rows = envelopeItems<
      BusinessConsoleMesReceivableProducedLotRow,
      BusinessConsoleMesReceivableProducedLotListEnvelope
    >(producedLotsQuery.data.value)
    // 端点已按工单服务端过滤、产出批次在 (org,env) 内唯一，故无需前端去重/过滤。
    return rows
      .filter((row) => isNonEmpty(row.producedLotNo ?? ''))
      .map((row) => ({
        producedLotNo: (row.producedLotNo ?? '').trim(),
        reportNo: row.reportNo ?? undefined,
        goodQuantity: row.quantity ?? 0,
        remainingQuantity: row.remainingQuantity ?? 0,
        serialNo: row.serialNo?.trim() || undefined,
      }))
  })

  return {
    producedLots,
    producedLotsError: producedLotsQuery.error,
    producedLotsPending: producedLotsQuery.isLoading,
    producedLotsState: businessReadState(
      producedLotsQuery,
      () => hasBusinessContext(filters) && isNonEmpty(workOrderId().trim()),
    ),
    refreshProducedLots: () => refetchWithBusinessContext(filters, producedLotsQuery),
  }
}

export function useMesFinishedGoodsReceipts(
  initial: { workOrderId?: string; requestNo?: string } = {},
) {
  const filters = defaultFilters()
  filters.workOrderId = initial.workOrderId
  filters.keyword = initial.requestNo
  const queryCache = useQueryCache()

  const receiptsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
        query: { ...toListQuery(filters), workOrderId: filters.workOrderId || undefined },
      }),
      filters,
    ),
  )

  const createReceiptMutation = useMutation({
    ...createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
        'listBusinessConsoleMesReceivableProducedLots',
        'getBusinessConsoleMesOverview',
      ]).catch(ignoreBackgroundError)
    },
  })

  // 完工入库失败重试（#833 facade）：只对 InventoryPostingFailed 单据重投库存过账意图。
  const retryMutation = useMutation({
    ...retryBusinessConsoleMesFinishedGoodsReceiptInventoryPostingMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
        'getBusinessConsoleMesOverview',
        // 跨域（A1 §4.2）：重投成功后库存移动过账，库存可用量读面失效
        'getBusinessConsoleInventoryAvailability',
      ]).catch(ignoreBackgroundError)
    },
  })
  // 正在重试的单据号集合（支持并发重试）：spinner/禁用按单据号各自作用，A 在途时重试 B 不会互相清空状态。
  const retryingRequestNos = reactive(new Set<string>())

  return {
    createReceiptRequest: (body: BusinessConsoleMesCreateReceiptRequest) => {
      // 只组装公共写契约字段，旧运行时对象中的未知字段不会进入请求体。
      const safeBody: BusinessConsoleMesCreateReceiptRequest = {
        organizationId: body.organizationId,
        environmentId: body.environmentId,
        workOrderId: body.workOrderId,
        skuId: body.skuId,
        quantity: body.quantity,
        uomCode: body.uomCode,
        requestedAtUtc: body.requestedAtUtc,
        idempotencyKey: body.idempotencyKey,
        producedLotNo: body.producedLotNo,
        serialNo: body.serialNo,
      }
      return createReceiptMutation.mutateAsync({ body: safeBody })
    },
    createReceiptRequestError: createReceiptMutation.error,
    createReceiptRequestPending: createReceiptMutation.isLoading,
    retryInventoryPosting: async (requestNo: string) => {
      retryingRequestNos.add(requestNo)
      try {
        await retryMutation.mutateAsync({
          path: { requestNo },
          query: {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
          },
          body: { idempotencyKey: makeIdempotencyKey('receipt-retry') },
        })
      } finally {
        // 只删当前单据：并发重试时不会误清其他仍在途单据的状态。
        retryingRequestNos.delete(requestNo)
      }
    },
    retryInventoryPostingError: retryMutation.error,
    isRetrying: (requestNo: string) => retryingRequestNos.has(requestNo),
    filters,
    receiptRequests: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(receiptsQuery.data.value),
    ),
    receiptRequestsError: receiptsQuery.error,
    receiptRequestsPending: receiptsQuery.isLoading,
    receiptRequestsState: businessReadState(receiptsQuery, () => hasBusinessContext(filters)),
    receiptRequestsTotal: computed(() => envelopeTotal(receiptsQuery.data.value)),
    refreshReceiptRequests: () => refetchWithBusinessContext(filters, receiptsQuery),
  }
}

/** 复用 FGR 来源读面；调用方仅在具有入库及库存台账读取权限时挂载。 */
export function useMesReceiptInventorySource(
  receipt: () => { requestNo?: string | null; workOrderId?: string | null },
) {
  const filters = defaultFilters()
  const query = useQuery(() => {
    const selected = receipt()
    const options = withBusinessContextEnabled(
      getBusinessConsoleMesFinishedGoodsReceiptInventoryLinkQueryOptions({
        path: { requestNo: selected.requestNo ?? '' },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          workOrderId: selected.workOrderId ?? undefined,
        },
      }),
      filters,
    )
    return {
      ...options,
      enabled: options.enabled && !!selected.requestNo && !!selected.workOrderId,
    }
  })
  return {
    source: computed(() => query.data.value?.data),
    sourcePending: query.isLoading,
    sourceError: query.error,
  }
}
