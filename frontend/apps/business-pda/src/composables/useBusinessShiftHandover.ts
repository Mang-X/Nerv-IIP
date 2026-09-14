/**
 * PDA 班次交接（交班录入 + 接班确认）的数据层。
 *
 * 口径与 PDA 其余域一致：
 * - 组织/环境范围只来自登录 principal（PDA 没有业务上下文选择器），空范围一律不发请求；
 * - 权限码只决定「渲染不渲染 / 发不发请求」，真实授权由网关逐请求校验；
 * - 交班人 / 接班人身份**不在请求体里**——网关从认证 principal 注入
 *   （`CreateBusinessConsoleMesShiftHandoverEndpoint` / `AcceptBusinessConsoleMesShiftHandoverEndpoint`），
 *   所以本文件永远不组装 outgoingUserId / incomingUserId。
 */
import {
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  completeBusinessConsoleShiftHandoverAttachmentUpload,
  downloadBusinessConsoleShiftHandoverAttachmentContent,
  createBusinessConsoleMesShiftHandoverMutationOptions,
  createBusinessConsoleShiftHandoverAttachmentUploadSession,
  getBusinessConsoleMesShiftHandoverQueryOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  type BusinessConsoleMesCreateShiftHandoverRequest,
  type BusinessConsoleMesShiftHandoverAttachment,
  type BusinessConsoleMesShiftHandoverDetail,
  type BusinessConsoleMesShiftHandoverDetailEnvelope,
  type BusinessConsoleMesShiftHandoverListEnvelope,
  type BusinessConsoleMesShiftHandoverOpenIssue,
  type BusinessConsoleMesShiftHandoverRow,
  type BusinessConsoleMesShiftHandoverUnfinishedWorkOrder,
  type BusinessConsoleMesShiftHandoverWipItem,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, ref, type Ref } from 'vue'

import { resolveGatewayBaseUrl } from '@/api/gateway-base-url'
import { describeRequestError } from '@/api/request-timeout'
import {
  ENVIRONMENT_HEADER,
  isShiftHandoverPhotoContentType,
  ORGANIZATION_HEADER,
  sendShiftHandoverAttachmentBytes,
  SHIFT_HANDOVER_PHOTO_MAX_BYTES,
} from '@/api/shift-handover-upload'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from '@/composables/useListFreshness'
import { usePdaIdentity } from '@/composables/useWorkbenchHome'
import { useAuthStore } from '@/stores/auth'

/** 交接班读/写权限码（权威来源：`NervIipPermissionCodes.MesHandoversRead/Manage`）。 */
export const SHIFT_HANDOVER_PERMISSIONS = {
  read: 'business.mes.handovers.read',
  manage: 'business.mes.handovers.manage',
} as const

/**
 * 班次/班组目录也是一次网关读取，需要 `business.masterdata.resources.read`。
 * 没有它时交班录入无法选班次/班组——这是一条独立于 handovers.manage 的前置。
 */
export const SHIFT_HANDOVER_DIRECTORY_PERMISSION = 'business.masterdata.resources.read'

const LIST_TAKE = 50
const DIRECTORY_TAKE = 200

/** 交接单待接班状态的列表过滤值（读面回显 `Open`，列表 query 的枚举取小写 `open`）。 */
export const HANDOVER_OPEN_STATUS_FILTER = 'open'

export type ShiftHandoverRow = BusinessConsoleMesShiftHandoverRow
export type ShiftHandoverDetail = BusinessConsoleMesShiftHandoverDetail
export type ShiftHandoverWipItem = BusinessConsoleMesShiftHandoverWipItem
export type ShiftHandoverUnfinishedWorkOrder = BusinessConsoleMesShiftHandoverUnfinishedWorkOrder
export type ShiftHandoverOpenIssue = BusinessConsoleMesShiftHandoverOpenIssue
export type ShiftHandoverAttachment = BusinessConsoleMesShiftHandoverAttachment

export interface ShiftHandoverDirectoryOption {
  value: string
  label: string
}

/**
 * 主数据码看起来像 GUID 时不上屏也不可选。
 *
 * 与 console 侧 `pages/mes/handovers.vue` 同一判据：交接单的 `shiftId` / `teamId` 是
 * MasterData 公共 id（EARLY / TEAM-WB-MC-A 这种），GUID 形状的行是工程标识符，
 * 让操作工在 PDA 上选一个 GUID 是没有意义的。
 */
const SYSTEM_ID_PATTERN =
  /^[{(]?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[)}]?$/i

export function isSystemIdentifier(value?: string | null): boolean {
  return SYSTEM_ID_PATTERN.test((value ?? '').trim())
}

export function toDirectoryOptions(
  resources: readonly BusinessConsoleResourceItem[],
): ShiftHandoverDirectoryOption[] {
  return resources
    .map((resource) => {
      const value = resource.code?.trim()
      if (!value || resource.active === false || isSystemIdentifier(value)) return undefined
      const displayName = resource.displayName?.trim()
      const label = displayName && !isSystemIdentifier(displayName) ? displayName : value
      return { value, label }
    })
    .filter((option): option is ShiftHandoverDirectoryOption => Boolean(option))
    .sort((a, b) => a.label.localeCompare(b.label, 'zh-Hans-CN'))
}

/** 交班人/接班人显示名：解析不出用户目录时不回显用户 id（那是工程标识符）。 */
export function outgoingUserLabel(row: { outgoingUserName?: string | null }): string {
  return row.outgoingUserName?.trim() || '未记录'
}

export function incomingUserLabel(row: {
  incomingUserName?: string | null
  acceptedAtUtc?: string | null
}): string {
  return row.incomingUserName?.trim() || (row.acceptedAtUtc ? '未记录' : '待接班')
}

export function formatAttachmentSize(sizeBytes?: number | null): string {
  if (typeof sizeBytes !== 'number' || !Number.isFinite(sizeBytes) || sizeBytes < 0)
    return '未知大小'
  if (sizeBytes < 1024) return `${sizeBytes} B`
  if (sizeBytes < 1024 * 1024) return `${(sizeBytes / 1024).toFixed(1)} KB`
  return `${(sizeBytes / (1024 * 1024)).toFixed(1)} MB`
}

/**
 * 相机/相册给的文件名在 Android WebView 上经常是空串或 `image.jpg`。
 *
 * FileStorage 的 `shift-handover-photo` 用途按**扩展名**放行（.png/.jpg/.jpeg），
 * 名字和内容类型对不上就会在 complete 时被 `MatchesDeclaredContentAsync` 顶回来，
 * 所以这里按内容类型补一个合法扩展名，而不是把原名原样塞过去。
 */
export function toHandoverPhotoFileName(
  rawName: string | undefined,
  contentType: string,
  now: Date = new Date(),
): string {
  const extension = contentType.toLowerCase().includes('png') ? '.png' : '.jpg'
  const trimmed = (rawName ?? '').trim().replace(/[\\/]/g, '')
  const base = trimmed.replace(/\.[^.]*$/, '').trim()
  const stamp = now.toISOString().replace(/[:.]/g, '-')
  return base ? `${base}${extension}` : `handover-photo-${stamp}${extension}`
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function envelopeItems<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[] } | null },
>(envelope: TEnvelope | undefined): TItem[] {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

function envelopeTotal<TEnvelope extends { success?: boolean; data?: { total?: number } | null }>(
  envelope: TEnvelope | undefined,
): number {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]
    return keyParts.some((part) => isRecord(part) && '_id' in part && part._id === id)
  }
}

function ignoreBackgroundError(_error: unknown) {}

/**
 * 写回执判读。
 *
 * 网关对建单/接班都只保证 `data.accepted === true`（`BusinessConsoleAcceptedResponse`），
 * `operationReceipt` 允许为空——这跟 console 侧 `readReceiptOutcome` 是同一口径。
 * 不认这条就会把「网关回了个空壳」当成功上屏。
 */
export function assertHandoverAccepted(response: unknown, action: string): void {
  if (!isRecord(response) || !isRecord(response.data) || response.data.accepted !== true) {
    throw new Error(`${action}未返回有效回执，请刷新后核实结果，勿重复提交。`)
  }
  const receipt = response.data.operationReceipt
  if (receipt == null) return
  if (!isRecord(receipt) || (receipt.outcome !== 'accepted' && receipt.outcome !== 'confirmed')) {
    throw new Error(`${action}未返回有效回执，请刷新后核实结果，勿重复提交。`)
  }
}

/** 交接班公共范围（org/env + 两个权限判定）。 */
export function useShiftHandoverScope() {
  const identity = usePdaIdentity()
  const canRead = computed(
    () => identity.hasScope.value && identity.can(SHIFT_HANDOVER_PERMISSIONS.read),
  )
  const canManage = computed(
    () => identity.hasScope.value && identity.can(SHIFT_HANDOVER_PERMISSIONS.manage),
  )
  const canReadDirectory = computed(
    () => identity.hasScope.value && identity.can(SHIFT_HANDOVER_DIRECTORY_PERMISSION),
  )
  const scopeKey = computed(() =>
    identity.hasScope.value
      ? `${identity.organizationId.value}:${identity.environmentId.value}`
      : '',
  )

  return {
    organizationId: identity.organizationId,
    environmentId: identity.environmentId,
    displayName: identity.displayName,
    teams: computed(() => identity.worker.value?.teams ?? []),
    hasScope: identity.hasScope,
    scopeKey,
    canRead,
    canManage,
    canReadDirectory,
  }
}

/** 待接班（及全部）交接单列表。 */
export function useMesShiftHandovers() {
  const scope = useShiftHandoverScope()
  const filters = reactive({ status: HANDOVER_OPEN_STATUS_FILTER as string | undefined })
  const enabled = scope.canRead

  const handoversQuery = useQuery(() => ({
    ...listBusinessConsoleMesShiftHandoversQueryOptions({
      query: {
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        ...(filters.status ? { status: filters.status as 'open' | 'accepted' } : {}),
        skip: 0,
        take: LIST_TAKE,
      },
    }),
    enabled: enabled.value,
  }))

  // 列表身份键带上 status：切过滤器等于换了一份读面，旧响应不能继续垫底。
  const listScopeKey = computed(() =>
    scope.scopeKey.value ? `${scope.scopeKey.value}:${filters.status ?? 'all'}` : '',
  )
  const currentResponse = useScopeBoundListResponse(
    () => handoversQuery.data.value,
    listScopeKey,
    enabled,
  )
  const lastUpdatedAt = useListFreshness(currentResponse, enabled)
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    enabled,
    handoversQuery.isLoading,
  )

  return {
    filters,
    enabled,
    canRead: scope.canRead,
    canManage: scope.canManage,
    hasScope: scope.hasScope,
    handovers: computed<ShiftHandoverRow[]>(() =>
      envelopeItems<ShiftHandoverRow, BusinessConsoleMesShiftHandoverListEnvelope>(
        currentResponse.value,
      ),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: handoversQuery.isLoading,
    error: handoversQuery.error,
    lastUpdatedAt,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (enabled.value ? handoversQuery.refetch() : Promise.resolve()),
  }
}

/** 单张交接单详情 + 接班确认。 */
export function useMesShiftHandoverDetail(handoverId: Readonly<Ref<string>>) {
  const scope = useShiftHandoverScope()
  const queryCache = useQueryCache()
  const enabled = computed(() => scope.canRead.value && handoverId.value.trim().length > 0)

  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleMesShiftHandoverQueryOptions({
      path: { handoverId: handoverId.value.trim() },
      query: {
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
      },
    }),
    enabled: enabled.value,
  }))

  const acceptMutation = useMutation({
    ...acceptBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess() {
      void Promise.all([
        queryCache.invalidateQueries({
          predicate: isBusinessQuery('listBusinessConsoleMesShiftHandovers'),
        }),
        queryCache.invalidateQueries({
          predicate: isBusinessQuery('getBusinessConsoleMesShiftHandover'),
        }),
      ]).catch(ignoreBackgroundError)
    },
  })

  const envelope = computed(
    () => detailQuery.data.value as BusinessConsoleMesShiftHandoverDetailEnvelope | undefined,
  )
  // 详情取数失败时不拿列表行垫底：三张明细以空数组渲染会把「没取到」谎报成「没登记」。
  const detail = computed<ShiftHandoverDetail | undefined>(() =>
    envelope.value?.success ? (envelope.value.data ?? undefined) : undefined,
  )

  return {
    enabled,
    canRead: scope.canRead,
    canManage: scope.canManage,
    hasScope: scope.hasScope,
    detail,
    wipItems: computed<ShiftHandoverWipItem[]>(() => detail.value?.wipItems ?? []),
    unfinishedWorkOrders: computed<ShiftHandoverUnfinishedWorkOrder[]>(
      () => detail.value?.unfinishedWorkOrders ?? [],
    ),
    openIssues: computed<ShiftHandoverOpenIssue[]>(() => detail.value?.openIssues ?? []),
    attachments: computed<ShiftHandoverAttachment[]>(() => detail.value?.attachments ?? []),
    pending: detailQuery.isLoading,
    error: detailQuery.error,
    hasFailedResponse: computed(
      () =>
        enabled.value &&
        !detailQuery.isLoading.value &&
        envelope.value !== undefined &&
        !(envelope.value.success && envelope.value.data),
    ),
    acceptPending: acceptMutation.isLoading,
    refresh: () => (enabled.value ? detailQuery.refetch() : Promise.resolve()),
    /**
     * 接班：请求体是空的，接班人由网关按认证 principal 注入（#3328 已把 idempotencyKey 去掉）。
     * 重放安全由 MES 的 `ShiftHandover.Accept` 首句幂等早退承担。
     */
    acceptHandover: async () => {
      const id = handoverId.value.trim()
      if (!id) throw new Error('缺少交接单标识，无法接班。')
      const response = await acceptMutation.mutateAsync({
        path: { handoverId: id },
        query: {
          organizationId: scope.organizationId.value,
          environmentId: scope.environmentId.value,
        },
      })
      assertHandoverAccepted(response, '接班')
      return response
    },
  }
}

/** 交班录入用的班次 / 班组目录。 */
export function useShiftHandoverDirectory() {
  const scope = useShiftHandoverScope()
  const enabled = scope.canReadDirectory

  function directoryQuery(resourceType: 'shift' | 'team') {
    return useQuery(() => ({
      ...listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: scope.organizationId.value,
          environmentId: scope.environmentId.value,
          resourceType,
          includeDisabled: false,
          skip: 0,
          take: DIRECTORY_TAKE,
        },
      }),
      enabled: enabled.value,
    }))
  }

  const shiftQuery = directoryQuery('shift')
  const teamQuery = directoryQuery('team')

  function resources(data: unknown): BusinessConsoleResourceItem[] {
    const envelope = data as BusinessConsoleResourceListEnvelope | undefined
    return envelope?.success ? (envelope.data?.resources ?? []) : []
  }

  return {
    enabled,
    shiftOptions: computed(() => toDirectoryOptions(resources(shiftQuery.data.value))),
    teamOptions: computed(() => toDirectoryOptions(resources(teamQuery.data.value))),
    pending: computed(() => shiftQuery.isLoading.value || teamQuery.isLoading.value),
    error: computed(() => shiftQuery.error.value ?? teamQuery.error.value),
    refresh: () =>
      enabled.value
        ? Promise.allSettled([shiftQuery.refetch(), teamQuery.refetch()])
        : Promise.resolve([]),
  }
}

export interface CreateShiftHandoverInput {
  shiftId: string
  teamId: string
  teamName?: string
  idempotencyKey: string
  wipItems: ShiftHandoverWipItem[]
  unfinishedWorkOrders: ShiftHandoverUnfinishedWorkOrder[]
  openIssues: ShiftHandoverOpenIssue[]
  attachments: ShiftHandoverAttachment[]
}

/** 交班建单 + 照片上传。 */
export function useShiftHandoverSubmission() {
  const scope = useShiftHandoverScope()
  const auth = useAuthStore()
  const queryCache = useQueryCache()

  const createMutation = useMutation({
    ...createBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleMesShiftHandovers') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    canManage: scope.canManage,
    hasScope: scope.hasScope,
    createPending: createMutation.isLoading,

    /**
     * 一张照片的完整通路：建会话 → HEAD 取 offset → PATCH 送字节 → 复查 offset → complete。
     * 只有中间两跳是手搓 fetch（tus 不在 OpenAPI 契约里），首尾两跳走 generated operation。
     */
    async uploadAttachment(file: File): Promise<ShiftHandoverAttachment> {
      if (!scope.canManage.value) throw new Error('当前账号没有交接班管理权限，无法上传照片。')
      const contentType = file.type
      if (!isShiftHandoverPhotoContentType(contentType)) {
        throw new Error('交接班附件只支持 JPG / PNG 照片。')
      }
      if (file.size <= 0) throw new Error('照片为空，请重新拍照。')
      if (file.size > SHIFT_HANDOVER_PHOTO_MAX_BYTES) {
        throw new Error('照片超出交接班附件大小上限（20 MB），请重拍或压缩后再传。')
      }

      const fileName = toHandoverPhotoFileName(file.name, contentType)
      const { data: sessionEnvelope } =
        await createBusinessConsoleShiftHandoverAttachmentUploadSession({
          body: {
            organizationId: scope.organizationId.value,
            environmentId: scope.environmentId.value,
            fileName,
            contentType,
            expectedSizeBytes: file.size,
          },
          throwOnError: true,
        })
      const session = sessionEnvelope?.success ? sessionEnvelope.data : undefined
      if (!session?.uploadSessionId) throw new Error('文件服务未返回可用的上传会话，请重试。')

      await sendShiftHandoverAttachmentBytes(
        { uploadUrl: session.uploadUrl, uploadHeaders: session.uploadHeaders },
        file,
        {
          organizationId: scope.organizationId.value,
          environmentId: scope.environmentId.value,
          accessToken: auth.accessToken,
        },
        { baseUrl: resolveGatewayBaseUrl() },
      )

      const { data: completeEnvelope } = await completeBusinessConsoleShiftHandoverAttachmentUpload(
        {
          path: { uploadSessionId: session.uploadSessionId },
          body: {
            organizationId: scope.organizationId.value,
            environmentId: scope.environmentId.value,
            sizeBytes: file.size,
          },
          throwOnError: true,
        },
      )
      const attachment = completeEnvelope?.success ? completeEnvelope.data : undefined
      if (!attachment?.fileId) throw new Error('文件服务未确认照片落盘，请重试。')
      return attachment
    },

    async createHandover(input: CreateShiftHandoverInput) {
      if (!scope.canManage.value) throw new Error('当前账号没有交接班管理权限，无法交班。')
      const body: BusinessConsoleMesCreateShiftHandoverRequest = {
        // org/env 最后由 principal 范围注入，调用方给不了。
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        shiftId: input.shiftId,
        teamId: input.teamId,
        teamName: input.teamName,
        idempotencyKey: input.idempotencyKey,
        wipItems: input.wipItems,
        unfinishedWorkOrders: input.unfinishedWorkOrders,
        openIssues: input.openIssues,
        attachments: input.attachments,
      }
      const response = await createMutation.mutateAsync({ body })
      assertHandoverAccepted(response, '交班提交')
      return response
    },
  }
}

/**
 * 交接单附件的查看通路。
 *
 * 下载面**有**可用的 generated operation（`GET .../{fileId}/content` 已回 `Blob | File`），
 * 所以这里不手搓 fetch。但它在契约里没有 query/header 参数，组织/环境只能经 `headers` 传
 * ——网关字节面 `BusinessConsoleFileTransfer.ProxyAsync` 从 `X-Organization-Id` /
 * `X-Environment-Id`（或同名 query）取范围，缺了直接 400。
 *
 * 读权限是 `business.mes.handovers.read`，不是 SOP 那条 `business.engineering.documents.read`；
 * 网关在取字节前复核目标文件用途必须是 `shift-handover-photo`。
 */
export function useShiftHandoverAttachmentViewer() {
  const scope = useShiftHandoverScope()
  const openingFileId = ref('')
  const error = ref('')

  async function openAttachment(attachment: ShiftHandoverAttachment) {
    const fileId = attachment.fileId?.trim()
    if (!fileId || openingFileId.value) return
    openingFileId.value = fileId
    error.value = ''
    try {
      const { data } = await downloadBusinessConsoleShiftHandoverAttachmentContent({
        path: { fileId },
        headers: {
          [ORGANIZATION_HEADER]: scope.organizationId.value,
          [ENVIRONMENT_HEADER]: scope.environmentId.value,
        },
        throwOnError: true,
      })
      if (!(data instanceof Blob)) throw new Error('文件服务未返回照片内容，请重试。')
      const blobUrl = URL.createObjectURL(data)
      const link = document.createElement('a')
      link.href = blobUrl
      link.target = '_blank'
      link.rel = 'noopener'
      document.body.appendChild(link)
      link.click()
      link.remove()
      window.setTimeout(() => URL.revokeObjectURL(blobUrl), 60_000)
    } catch (cause) {
      error.value = describeRequestError(cause, '照片打开失败，请重试。').message
    } finally {
      openingFileId.value = ''
    }
  }

  return { openingFileId, error, openAttachment }
}
