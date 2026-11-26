import {
  confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions,
  returnBusinessConsoleMesLineSideMaterialMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesMaterialIssueRequestMutationOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequests,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesReceivableProducedLots,
  listBusinessConsoleMesMaterialIssueRequests,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesLineSideInventoryBalancesQueryOptions,
  type BusinessConsoleMesConfirmLineSideReceiptRequest,
  type BusinessConsoleMesReturnLineSideMaterialRequest,
  type BusinessConsoleMesCreateMaterialIssueRequest,
  type BusinessConsoleMesCreateReceiptRequest,
  type BusinessConsoleMesCreateReceiptResponse,
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
import { useAuthStore } from '@/stores/auth'
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
  const auth = useAuthStore()
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(filters))
  const scopeKey = computed(() =>
    scopeReady.value ? `${filters.organizationId}:${filters.environmentId}` : '',
  )

  const receiveScopeIdentity = computed(() => {
    const principal = auth.principal
    if (
      !scopeReady.value ||
      !auth.sessionId ||
      !principal?.principalId ||
      principal.organizationId !== filters.organizationId ||
      principal.environmentId !== filters.environmentId ||
      !principal.permissionCodes?.includes('business.mes.materials.manage')
    )
      return ''
    return JSON.stringify([
      auth.sessionId,
      principal.principalId,
      filters.organizationId,
      filters.environmentId,
    ])
  })

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

  const confirmMutation = useMutation(
    confirmBusinessConsoleMesLineSideMaterialReceiptMutationOptions(),
  )

  const returnMutation = useMutation({
    ...returnBusinessConsoleMesLineSideMaterialMutationOptions(),
    async onSuccess() {
      await invalidateMesQueries(queryCache, ['listBusinessConsoleMesMaterialIssueRequests'])
    },
  })

  async function readMaterialIssue(
    requestId: string,
    workOrderId: string | undefined,
    scope: ReturnType<typeof scopeQuery>,
  ) {
    let skip = 0
    let authoritative: BusinessConsoleMesMaterialIssueRequestRow | undefined
    while (!authoritative) {
      const { data } = await listBusinessConsoleMesMaterialIssueRequests({
        query: {
          ...scope,
          ...(workOrderId ? { workOrderId } : {}),
          skip,
          take: DEFAULT_TAKE,
        },
        throwOnError: true,
      })
      const envelope = data as BusinessConsoleMesMaterialIssueRequestListEnvelope | undefined
      authoritative = exactItem(
        envelope,
        (item: BusinessConsoleMesMaterialIssueRequestRow) =>
          item.requestId === requestId && (!workOrderId || item.workOrderId === workOrderId),
      )
      if (authoritative) break
      const items = envelope?.success ? (envelope.data?.items ?? []) : []
      const total = envelope?.success ? (envelope.data?.total ?? 0) : 0
      if (items.length === 0 || skip + items.length >= total) break
      skip += items.length
    }
    return authoritative
  }

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
    receiveScopeIdentity,
    confirmLineSideReceipt: async (
      requestId: string,
      body: ConfirmLineSideReceiptInput & { idempotencyKey: string },
      context: {
        workOrderId?: string
        replay?: boolean
        isCurrent?: () => boolean
        beforeDispatch?: () => void
      } = {},
    ) => {
      const identity = receiveScopeIdentity.value
      const scope = scopeQuery(filters)
      const workOrderId = context.workOrderId?.trim()
      function assertCurrentScope() {
        if (
          !identity ||
          receiveScopeIdentity.value !== identity ||
          context.isCurrent?.() === false
        ) {
          throw new Error('当前作业范围已变化，请重新打开领料申请。')
        }
      }

      assertCurrentScope()
      if (!context.replay) {
        const current = await readMaterialIssue(requestId, workOrderId, scope)
        assertCurrentScope()
        assertLifecycleActionExecutable({
          domain: 'mes-material-issue',
          action: 'confirm-receipt',
          facts: { status: current?.status },
        })
      }

      // MES 按键恢复原回执，即使该领料单已进入过账中或已收，重放仍由网关重新授权。
      context.beforeDispatch?.()
      const response = await confirmMutation.mutateAsync({
        path: { requestId },
        query: scope,
        body,
      })
      assertCurrentScope()
      const reference = response?.data?.downstreamDocumentId?.trim()
      if (!response?.success || response.data?.accepted !== true || !reference) {
        throw new Error(response?.message || '尚未取得线边接收回执，请重试原操作。')
      }

      const current = await readMaterialIssue(requestId, workOrderId, scope)
      assertCurrentScope()
      if (!current) throw new Error('收料已受理，暂未读到原领料申请，请重试核实。')
      await invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesMaterialIssueRequests',
        'listBusinessConsoleMesLineSideInventoryBalances',
      ]).catch(ignoreBackgroundError)
      assertCurrentScope()
      return { reference, current }
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
  const auth = useAuthStore()
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(filters))
  const scopeKey = computed(() =>
    scopeReady.value
      ? JSON.stringify([auth.principal?.principalId, filters.organizationId, filters.environmentId])
      : '',
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
    receiptScopeIdentity: scopeKey,
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
    async readProducedLots(workOrderId: string, signal: AbortSignal) {
      const { data } = await listBusinessConsoleMesReceivableProducedLots({
        path: { workOrderId },
        query: scopeQuery(filters),
        signal,
        throwOnError: true,
      })
      if (!data?.success || !data.data?.items) throw new Error('无法读取产出批次，请重试。')
      return data.data.items.filter(
        (lot): lot is typeof lot & { producedLotNo: string; remainingQuantity: number } =>
          Boolean(lot.producedLotNo?.trim()) &&
          typeof lot.remainingQuantity === 'number' &&
          Number.isFinite(lot.remainingQuantity) &&
          lot.remainingQuantity > 0.000001,
      )
    },
    async readReceipt(workOrderId: string, receipt: BusinessConsoleMesCreateReceiptResponse) {
      const scope = scopeQuery(filters)
      const take = 20
      let skip = 0
      while (true) {
        const { data } = await listBusinessConsoleMesFinishedGoodsReceiptRequests({
          query: { ...scope, workOrderId, keyword: receipt.requestNo, skip, take },
          throwOnError: true,
        })
        if (!data?.success || !data.data?.items) return undefined
        const { items, total } = data.data
        const match = items.find(
          (row) =>
            row.workOrderId === workOrderId &&
            row.receiptRequestId === receipt.finishedGoodsReceiptRequestId &&
            row.requestNo === receipt.requestNo,
        )
        if (match) return match
        skip += items.length
        if (items.length === 0 || skip >= (total ?? 0)) return undefined
      }
    },
    createReceipt: (input: CreateReceiptInput, requestedAtUtc = new Date().toISOString()) => {
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
          // Scope comes from the principal. The intent timestamp stays fixed on replay.
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          requestedAtUtc,
        } satisfies BusinessConsoleMesCreateReceiptRequest,
      })
    },
  }
}
