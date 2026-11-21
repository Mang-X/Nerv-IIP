import {
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  returnBusinessConsoleMesLineSideMaterialMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesLineSideInventoryBalancesQueryOptions,
  type BusinessConsoleMesConfirmLineSideReceiptRequest,
  type BusinessConsoleMesReturnLineSideMaterialRequest,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesCreateReceiptRequest,
  type BusinessConsoleMesMaterialIssueRequestListEnvelope,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesLineSideInventoryBalanceItem,
  type BusinessConsoleMesLineSideInventoryBalancesEnvelope,
  type BusinessConsoleMesReceiptRequestListEnvelope,
  type BusinessConsoleMesReceiptRequestRow,
} from '@nerv-iip/api-client'
import {
  createServerPaginationState,
  lastPageForTotal,
  reduceServerPagination,
  serverPaginationIdentity,
  useListResponseState,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import { assertLifecycleActionExecutable } from '@/composables/shared/lifecycleActionRecovery'
import {
  DEFAULT_TAKE,
  bindAuthScope,
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  exactItem,
  hasScope,
  ignoreBackgroundError,
  invalidateMesQueries,
  scopeKey,
  scopeQuery,
  toListQuery,
} from './mesShared'

export type CreateIssueInput = BusinessConsoleMesCreateMaterialIssueRequest

export type ConfirmLineSideReceiptInput = BusinessConsoleMesConfirmLineSideReceiptRequest

export type ReturnLineSideMaterialInput = BusinessConsoleMesReturnLineSideMaterialRequest

export function useMesLineSideInventoryBalances() {
  const pageSize = 200
  const scope = bindAuthScope(reactive({ organizationId: '', environmentId: '' }))
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(scope))
  const scopeIdentity = computed(() => (scopeReady.value ? scopeKey(scope) : ''))
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
  const identity = computed(() => serverPaginationIdentity(scopeIdentity.value, page.value))
  function queryOptions(targetPage: number) {
    const requestIdentity = serverPaginationIdentity(scopeIdentity.value, targetPage)
    const { key, query: executeQuery } =
      listBusinessConsoleMesLineSideInventoryBalancesQueryOptions({
        query: {
          organizationId: scope.organizationId,
          environmentId: scope.environmentId,
          page: targetPage,
          pageSize,
        },
      })
    return {
      key: [...key, `scope-page:${requestIdentity}`],
      query: async (context: Parameters<typeof executeQuery>[0]) => ({
        identity: requestIdentity,
        response: await executeQuery(context),
      }),
      enabled: scopeReady.value,
    }
  }
  const query = useQuery(() => queryOptions(page.value))
  const currentResponse = computed(() => {
    const scopedResponse = query.data.value
    if (scopedResponse?.identity !== identity.value) return undefined
    const response = scopedResponse.response
    if (response?.success === true && (response.data?.page ?? 1) !== page.value) return undefined
    return response
  })
  const responsePageMismatch = computed(() => {
    const scopedResponse = query.data.value
    if (scopedResponse?.identity !== identity.value) return false
    const response = scopedResponse.response
    return response?.success === true && (response.data?.page ?? 1) !== page.value
  })
  watch(
    currentResponse,
    (response) => {
      if (response?.success !== true) return
      const nextPagination = reduceServerPagination(pagination.value, {
        type: 'response-succeeded',
        identity: identity.value,
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
    },
    { flush: 'sync', immediate: true },
  )
  const pending = computed(() => query.isLoading.value || pagination.value.navigationPending)
  const failure = computed(() =>
    query.error.value != null
      ? query.error.value
      : responsePageMismatch.value
        ? new Error('线边库存响应页码与请求不一致，请重试。')
        : currentResponse.value !== undefined && currentResponse.value.success !== true
          ? new Error(currentResponse.value.message ?? '线边库存加载失败，请重试。')
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
          identity: identity.value,
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

  return {
    balances: computed<BusinessConsoleMesLineSideInventoryBalanceItem[]>(() =>
      envelopeItems<
        BusinessConsoleMesLineSideInventoryBalanceItem,
        BusinessConsoleMesLineSideInventoryBalancesEnvelope
      >(currentResponse.value),
    ),
    total,
    page,
    pageCount,
    hasPreviousPage,
    hasNextPage,
    pending,
    error: failure,
    ready: computed(() => currentResponse.value?.success === true),
    previousPage,
    nextPage,
    refresh: () => (scopeReady.value ? query.refetch() : Promise.resolve()),
  }
}

export function useMesMaterialIssue() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(filters))
  const scopeKey = computed(() =>
    scopeReady.value ? `${filters.organizationId}:${filters.environmentId}` : '',
  )

  const requestsQuery = useQuery(() => ({
    ...listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => requestsQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    requestsQuery.isLoading,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMesMaterialIssueRequestMutationOptions(),
    async onSuccess() {
      await invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests'])
    },
  })

  const confirmMutation = useMutation({
    ...confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests']).catch(
        ignoreBackgroundError,
      )
    },
  })

  const returnMutation = useMutation({
    ...returnBusinessConsoleMesLineSideMaterialMutationOptions(),
    async onSuccess() {
      await invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests'])
    },
  })

  return {
    filters,
    scopeReady,
    requests: computed<BusinessConsoleMesMaterialIssueRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesMaterialIssueRequestRow,
        BusinessConsoleMesMaterialIssueRequestListEnvelope
      >(currentResponse.value),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: requestsQuery.isLoading,
    error: requestsQuery.error,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (hasScope(filters) ? requestsQuery.refetch() : Promise.resolve()),
    createIssue: (workOrderId: string, body: CreateIssueInput) =>
      createMutation.mutateAsync({
        path: { workOrderId },
        query: scopeQuery(filters),
        body: { ...body } satisfies BusinessConsoleMesCreateMaterialIssueRequest,
      }),
    confirmLineSideReceipt: async (
      requestId: string,
      body: ConfirmLineSideReceiptInput,
      context: { workOrderId?: string } = {},
    ) => {
      let skip = 0
      let authoritative: BusinessConsoleMesMaterialIssueRequestRow | undefined
      while (!authoritative) {
        const { data } = await listBusinessConsoleMesMaterialIssueRequests({
          query: {
            ...scopeQuery(filters),
            ...(context.workOrderId?.trim() ? { workOrderId: context.workOrderId.trim() } : {}),
            skip,
            take: DEFAULT_TAKE,
          },
          throwOnError: true,
        })
        const envelope = data as BusinessConsoleMesMaterialIssueRequestListEnvelope | undefined
        authoritative = exactItem(
          envelope,
          (item: BusinessConsoleMesMaterialIssueRequestRow) => item.requestId === requestId,
        )
        if (authoritative) break
        const items = envelope?.success ? (envelope.data?.items ?? []) : []
        const total = envelope?.success ? (envelope.data?.total ?? 0) : 0
        if (items.length === 0 || skip + items.length >= total) break
        skip += items.length
      }
      assertLifecycleActionExecutable({
        domain: 'mes-material-issue',
        action: 'confirm-receipt',
        facts: { status: authoritative?.status },
      })
      return confirmMutation.mutateAsync({
        path: { requestId },
        query: scopeQuery(filters),
        body: { ...body } satisfies BusinessConsoleMesConfirmLineSideReceiptRequest,
      })
    },
    returnLineSideMaterial: async (
      requestId: string,
      body: ReturnLineSideMaterialInput,
      context: { workOrderId?: string } = {},
    ) => {
      let skip = 0
      let authoritative: BusinessConsoleMesMaterialIssueRequestRow | undefined
      while (!authoritative) {
        const { data } = await listBusinessConsoleMesMaterialIssueRequests({
          query: {
            ...scopeQuery(filters),
            ...(context.workOrderId?.trim() ? { workOrderId: context.workOrderId.trim() } : {}),
            skip,
            take: DEFAULT_TAKE,
          },
          throwOnError: true,
        })
        const envelope = data as BusinessConsoleMesMaterialIssueRequestListEnvelope | undefined
        authoritative = exactItem(
          envelope,
          (item: BusinessConsoleMesMaterialIssueRequestRow) => item.requestId === requestId,
        )
        if (authoritative) break
        const items = envelope?.success ? (envelope.data?.items ?? []) : []
        const total = envelope?.success ? (envelope.data?.total ?? 0) : 0
        if (items.length === 0 || skip + items.length >= total) break
        skip += items.length
      }
      if (!authoritative) {
        throw new Error('当前领料单没有可退回的线边物料。')
      }
      return returnMutation.mutateAsync({
        path: { requestId },
        query: scopeQuery(filters),
        body,
      })
    },
  }
}

export type CreateReceiptInput = Omit<
  BusinessConsoleMesCreateReceiptRequest,
  'organizationId' | 'environmentId' | 'requestedAtUtc'
>

export function useMesReceipts() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(filters))
  const scopeKey = computed(() =>
    scopeReady.value ? `${filters.organizationId}:${filters.environmentId}` : '',
  )

  const receiptsQuery = useQuery(() => ({
    ...listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => receiptsQuery.data.value,
    scopeKey,
    scopeReady,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    receiptsQuery.isLoading,
  )

  const createMutation = useMutation({
    ...createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    scopeReady,
    receipts: computed<BusinessConsoleMesReceiptRequestRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesReceiptRequestRow,
        BusinessConsoleMesReceiptRequestListEnvelope
      >(currentResponse.value),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: receiptsQuery.isLoading,
    error: receiptsQuery.error,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (hasScope(filters) ? receiptsQuery.refetch() : Promise.resolve()),
    createReceipt: (input: CreateReceiptInput) => {
      // 只组装公共写契约字段，旧运行时对象中的未知字段不会进入请求体。
      const safeInput: CreateReceiptInput = {
        workOrderId: input.workOrderId,
        skuId: input.skuId,
        quantity: input.quantity,
        uomCode: input.uomCode,
        idempotencyKey: input.idempotencyKey,
        producedLotNo: input.producedLotNo,
        serialNo: input.serialNo,
      }
      return createMutation.mutateAsync({
        body: {
          ...safeInput,
          // org/env + timestamp injected LAST from principal scope — never the caller.
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          requestedAtUtc: new Date().toISOString(),
        } satisfies BusinessConsoleMesCreateReceiptRequest,
      })
    },
  }
}
