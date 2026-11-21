import {
  createBusinessConsoleEngineeringItemRevisionMutationOptions,
  getBusinessConsoleEngineeringChangeQueryOptions,
  getBusinessConsoleEngineeringDocumentQueryOptions,
  getBusinessConsoleEngineeringItemQueryOptions,
  listBusinessConsoleEngineeringChangesQueryOptions,
  listBusinessConsoleEngineeringDocumentsQueryOptions,
  listBusinessConsoleEngineeringItemsQueryOptions,
  registerBusinessConsoleEngineeringDocumentMutationOptions,
  previewBusinessConsoleEngineeringChangeImpactMutationOptions,
  releaseBusinessConsoleEngineeringChangeMutationOptions,
  type BusinessConsoleCreateEngineeringItemRevisionRequest,
  type BusinessConsoleEngineeringChangeItem,
  type BusinessConsoleEngineeringChangeListEnvelope,
  type BusinessConsoleEngineeringDocumentItem,
  type BusinessConsoleEngineeringDocumentListEnvelope,
  type BusinessConsoleEngineeringItemRevisionItem,
  type BusinessConsoleEngineeringItemListEnvelope,
  type BusinessConsoleRegisterEngineeringDocumentRequest,
  type BusinessConsoleReleaseEngineeringChangeRequest,
  type BusinessConsoleEngineeringChangeImpactPreviewRequest,
  type BusinessConsoleEngineeringChangeImpactPreviewResponse,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseMutationOptions } from '@pinia/colada'
import { computed, reactive, shallowRef } from 'vue'
import {
  bindBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../businessContextBinding'
import {
  DEFAULT_TAKE,
  ignoreBackgroundError,
  isBusinessQuery,
  optionalQuery,
  runQueryOption,
  unwrapDetail,
  unwrapItems,
  unwrapTotal,
} from './engineeringShared'

export interface EngineeringItemListFilters {
  organizationId: string
  environmentId: string
  itemCode?: string
  status?: string
  skip: number
  take: number
}

export interface EngineeringDocumentListFilters {
  organizationId: string
  environmentId: string
  itemCode?: string
  documentType?: string
  skip: number
  take: number
}

export interface EngineeringChangeListFilters {
  organizationId: string
  environmentId: string
  status?: string
  skip: number
  take: number
}

/**
 * 工程物料（EngineeringItem，修订链）列表（list + filters）+ 新建修订（create-revision）+ 按需取修订明细（get-by-id）。
 *
 * 工程数据语义：物料不是直接编辑，而是从已发布修订「派生新修订」。`createRevision` 带 `release` 标志
 * 决定新修订是否立即发布；不发布则为草稿（Draft）。物料编码 `itemCode` 留空由后端自动编码，
 * 仅在补登历史已知编码时才填（默认不收，遵守自动编码约束）。
 * 状态枚举用后端真值 Draft/Published/Archived（EngineeringVersionStatus）。
 */
export function useEngineeringItems() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<EngineeringItemListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      itemCode: undefined,
      status: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleEngineeringItemsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('itemCode', filters.itemCode),
          ...optionalQuery('status', filters.status),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringItems') })
      .catch(ignoreBackgroundError)
  }

  const createRevisionMutation = useMutation({
    ...createBusinessConsoleEngineeringItemRevisionMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    items: computed<BusinessConsoleEngineeringItemRevisionItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleEngineeringItemListEnvelope | undefined),
    ),
    itemsError: listQuery.error,
    itemsPending: listQuery.isLoading,
    itemsTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleEngineeringItemListEnvelope | undefined),
    ),
    refresh: () => refetchWithBusinessContext(filters, listQuery),

    createItemRevision: (body: BusinessConsoleCreateEngineeringItemRevisionRequest) =>
      (createRevisionMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        body,
      }),
    createPending: createRevisionMutation.isLoading,
    createError: createRevisionMutation.error,

    // 按需取某物料修订明细，用于「查看」。失败抛错由调用方处理。
    fetchItemDetail: async (itemCode: string, revision: string) => {
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringItemQueryOptions({
          path: { itemCode, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringItemRevisionItem>(envelope)
    },
  }
}

/**
 * 工程文档（EngineeringDocument，按修订登记）列表（list + filters）+ 登记文档（register）+ 按需取明细（get-by-id）。
 *
 * register 须带 documentNumber/revision/fileId/fileName/contentType/documentType；后端无文件上传通道，
 * fileId 先作文件引用 ID 文本输入（页面标注「文件上传待接入」）。文档号 documentNumber 由用户定义。
 */
export function useEngineeringDocuments() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<EngineeringDocumentListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      itemCode: undefined,
      documentType: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleEngineeringDocumentsQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('itemCode', filters.itemCode),
          ...optionalQuery('documentType', filters.documentType),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringDocuments') })
      .catch(ignoreBackgroundError)
  }

  const registerMutation = useMutation({
    ...registerBusinessConsoleEngineeringDocumentMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)

  return {
    filters,
    documents: computed<BusinessConsoleEngineeringDocumentItem[]>(() =>
      unwrapItems(
        listQuery.data.value as BusinessConsoleEngineeringDocumentListEnvelope | undefined,
      ),
    ),
    documentsError: listQuery.error,
    documentsPending: listQuery.isLoading,
    documentsTotal: computed(() =>
      unwrapTotal(
        listQuery.data.value as BusinessConsoleEngineeringDocumentListEnvelope | undefined,
      ),
    ),
    refresh: () => refetchWithBusinessContext(filters, listQuery),

    registerDocument: (body: BusinessConsoleRegisterEngineeringDocumentRequest) =>
      (registerMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    registerPending: registerMutation.isLoading,
    registerError: registerMutation.error,

    // 按需取某文档修订明细，用于「查看」。失败抛错由调用方处理。
    fetchDocumentDetail: async (documentNumber: string, revision: string) => {
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringDocumentQueryOptions({
          path: { documentNumber, revision },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringDocumentItem>(envelope)
    },
  }
}

/**
 * 工程变更（EngineeringChange/ECO）列表（list + filters）+ 发布变更（release）+ 按需取明细（get-by-id 看受影响版本）。
 *
 * **后端是一步发布**（Open→Approve→Release 一气呵成），没有多步审批工作流、没有草稿/待审状态——
 * 页面只做「发布变更」向导，不假造审批态。release 须带 reason/approvalReferenceId/effectiveDate/affectedVersions[]；
 * 变更号 changeNumber 留空由后端自动编码（默认不收）。
 */
export function useEngineeringChanges() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<EngineeringChangeListFilters>({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
      status: undefined,
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleEngineeringChangesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          ...optionalQuery('status', filters.status),
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )

  function invalidateList() {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleEngineeringChanges') })
      .catch(ignoreBackgroundError)
  }

  const releaseMutation = useMutation({
    ...releaseBusinessConsoleEngineeringChangeMutationOptions(),
    onSuccess: invalidateList,
  } as unknown as UseMutationOptions)
  const previewMutation = useMutation({
    ...previewBusinessConsoleEngineeringChangeImpactMutationOptions(),
  } as unknown as UseMutationOptions)
  const impactPreview = shallowRef<BusinessConsoleEngineeringChangeImpactPreviewResponse>()

  return {
    filters,
    changes: computed<BusinessConsoleEngineeringChangeItem[]>(() =>
      unwrapItems(listQuery.data.value as BusinessConsoleEngineeringChangeListEnvelope | undefined),
    ),
    changesError: listQuery.error,
    changesPending: listQuery.isLoading,
    changesTotal: computed(() =>
      unwrapTotal(listQuery.data.value as BusinessConsoleEngineeringChangeListEnvelope | undefined),
    ),
    refresh: () => refetchWithBusinessContext(filters, listQuery),

    releaseChange: (body: BusinessConsoleReleaseEngineeringChangeRequest) =>
      (releaseMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({ body }),
    releasePending: releaseMutation.isLoading,
    releaseError: releaseMutation.error,

    previewImpact: async (body: BusinessConsoleEngineeringChangeImpactPreviewRequest) => {
      const envelope = await (
        previewMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>
      )({ body })
      impactPreview.value = unwrapDetail<BusinessConsoleEngineeringChangeImpactPreviewResponse>(
        envelope as
          | {
              success?: boolean
              data?: BusinessConsoleEngineeringChangeImpactPreviewResponse | null
            }
          | undefined,
      )
      return impactPreview.value
    },
    previewPending: previewMutation.isLoading,
    previewError: previewMutation.error,
    impactPreview,
    clearImpactPreview: () => {
      impactPreview.value = undefined
    },

    // 按需取某变更明细（含受影响版本），用于「查看」。失败抛错由调用方处理。
    fetchChangeDetail: async (changeNumber: string) => {
      const envelope = await runQueryOption(
        getBusinessConsoleEngineeringChangeQueryOptions({
          path: { changeNumber },
          query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        }),
      )
      return unwrapDetail<BusinessConsoleEngineeringChangeItem>(envelope)
    },
  }
}
