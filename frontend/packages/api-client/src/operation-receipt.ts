import {
  getBusinessConsoleMesProductionReport,
  listBusinessConsoleEquipmentAlarms,
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsOutboundOrders,
} from './generated/business-console/sdk.gen'

interface BusinessConsoleOperationReceiptCommon {
  operationType: string
  authority: string
  resourceType: string
  resourceId: string
  idempotencyKey: string
}

export interface BusinessConsoleConfirmedOperationReceipt extends BusinessConsoleOperationReceiptCommon {
  outcome: 'confirmed'
  stateConfirmed: true
  readbackRequired: false
  changedAtUtc: string
  resourceStatus: string
  readbackMethod?: null
  readbackPath?: null
}

export interface BusinessConsoleAcceptedOperationReceipt extends BusinessConsoleOperationReceiptCommon {
  outcome: 'accepted'
  stateConfirmed: false
  readbackRequired: true
  changedAtUtc?: string | null
  resourceStatus?: string | null
  readbackMethod: 'GET'
  readbackPath: string
}

export type BusinessConsoleOperationReceiptLike =
  | BusinessConsoleConfirmedOperationReceipt
  | BusinessConsoleAcceptedOperationReceipt

type BusinessConsoleOperationIdentity = Pick<
  BusinessConsoleOperationReceiptCommon,
  'operationType' | 'resourceId'
>

export interface BusinessConsoleOperationEnvelope {
  success?: boolean
  data?: {
    operationReceipt?: unknown
  } | null
}

export class BusinessOperationUnconfirmedError extends Error {
  readonly code = 'business-operation-unconfirmed'

  constructor(
    message: string,
    readonly operationType?: string,
    readonly readbackPath?: string,
  ) {
    super(message)
    this.name = 'BusinessOperationUnconfirmedError'
  }
}

export class BusinessOperationFailedError extends Error {
  readonly code = 'business-operation-failed'
  readonly indeterminate = false

  constructor(
    message: string,
    readonly failureCode?: string,
  ) {
    super(message)
    this.name = 'BusinessOperationFailedError'
  }
}

export type BusinessConsoleOperationReadbackVerdict =
  | { state: 'confirmed-success' }
  | { state: 'confirmed-business-failure'; message: string; failureCode?: string }
  | { state: 'indeterminate' }

type JsonRecord = Record<string, unknown>

function record(value: unknown): JsonRecord | undefined {
  return value && typeof value === 'object' ? (value as JsonRecord) : undefined
}

function text(value: unknown) {
  return typeof value === 'string' ? value : undefined
}

function normalized(value: unknown) {
  return text(value)?.trim().toLowerCase()
}

function requiredText(value: unknown) {
  const result = text(value)?.trim()
  return result || undefined
}

function parseOperationReceipt(value: unknown): BusinessConsoleOperationReceiptLike | undefined {
  const candidate = record(value)
  if (!candidate) return undefined

  const common = {
    operationType: requiredText(candidate.operationType),
    authority: requiredText(candidate.authority),
    resourceType: requiredText(candidate.resourceType),
    resourceId: requiredText(candidate.resourceId),
    idempotencyKey: requiredText(candidate.idempotencyKey),
  }
  if (Object.values(common).some((field) => !field)) return undefined

  if (
    candidate.outcome === 'confirmed' &&
    candidate.stateConfirmed === true &&
    candidate.readbackRequired === false &&
    candidate.readbackMethod == null &&
    candidate.readbackPath == null
  ) {
    const changedAtUtc = requiredText(candidate.changedAtUtc)
    const resourceStatus = requiredText(candidate.resourceStatus)
    if (!changedAtUtc || Number.isNaN(Date.parse(changedAtUtc)) || !resourceStatus) return undefined
    return {
      ...(common as BusinessConsoleOperationReceiptCommon),
      outcome: 'confirmed',
      stateConfirmed: true,
      readbackRequired: false,
      changedAtUtc,
      resourceStatus,
    }
  }

  if (
    candidate.outcome === 'accepted' &&
    candidate.stateConfirmed === false &&
    candidate.readbackRequired === true &&
    candidate.readbackMethod === 'GET'
  ) {
    const readbackPath = requiredText(candidate.readbackPath)
    const changedAtUtc =
      candidate.changedAtUtc == null ? null : requiredText(candidate.changedAtUtc)
    const resourceStatus =
      candidate.resourceStatus == null ? null : requiredText(candidate.resourceStatus)
    if (!readbackPath) return undefined
    if (
      (candidate.changedAtUtc != null &&
        (!changedAtUtc || Number.isNaN(Date.parse(changedAtUtc)))) ||
      (candidate.resourceStatus != null && !resourceStatus)
    ) {
      return undefined
    }
    return {
      ...(common as BusinessConsoleOperationReceiptCommon),
      outcome: 'accepted',
      stateConfirmed: false,
      readbackRequired: true,
      changedAtUtc,
      resourceStatus,
      readbackMethod: 'GET',
      readbackPath,
    }
  }

  return undefined
}

function envelopeData(value: unknown) {
  const root = record(value)
  if (!root || root.success !== true) return undefined
  return record(root.data)
}

function listItems(value: unknown) {
  const data = envelopeData(value)
  return Array.isArray(data?.items) ? (data.items.map(record).filter(Boolean) as JsonRecord[]) : []
}

function matchesResource(item: JsonRecord, field: string, resourceId: string) {
  return text(item[field]) === resourceId
}

function confirmedFailure(
  item: JsonRecord,
  fallback: string,
): BusinessConsoleOperationReadbackVerdict {
  const message =
    requiredText(item.failureMessage) ??
    requiredText(item.inventoryPostingFailureMessage) ??
    fallback
  const failureCode =
    requiredText(item.failureCode) ?? requiredText(item.inventoryPostingFailureCode)
  return {
    state: 'confirmed-business-failure',
    message,
    ...(failureCode ? { failureCode } : {}),
  }
}

export function verifyBusinessConsoleOperationReadback(
  receipt: BusinessConsoleOperationIdentity,
  payload: unknown,
): BusinessConsoleOperationReadbackVerdict {
  const operationType = receipt.operationType
  const resourceId = receipt.resourceId
  if (!operationType || !resourceId) return { state: 'indeterminate' }

  if (operationType === 'mes.production-report.record') {
    const report = record(envelopeData(payload)?.report)
    return text(report?.productionReportId) === resourceId
      ? { state: 'confirmed-success' }
      : { state: 'indeterminate' }
  }

  if (operationType === 'wms.inbound-order.complete') {
    const item = listItems(payload).find((candidate) =>
      matchesResource(candidate, 'inboundOrderId', resourceId),
    )
    const status = normalized(item?.status)
    if (status === 'completed' || status === 'pendingqualitycheck') {
      return { state: 'confirmed-success' }
    }
    if (item && ['inventorypostingfailed', 'failed', 'cancelled'].includes(status ?? '')) {
      return confirmedFailure(item, '入库完成失败，请刷新后按最新状态处理')
    }
    return { state: 'indeterminate' }
  }

  if (operationType === 'wms.outbound-order.complete') {
    const item = listItems(payload).find((candidate) =>
      matchesResource(candidate, 'outboundOrderId', resourceId),
    )
    const status = normalized(item?.status)
    const postingStatus = normalized(item?.inventoryPostingStatus)
    if (status === 'completed' && postingStatus === 'posted') {
      return { state: 'confirmed-success' }
    }
    if (item && (postingStatus === 'failed' || status === 'inventorypostingfailed')) {
      return confirmedFailure(item, '库存过账失败，请刷新后按最新状态处理')
    }
    return { state: 'indeterminate' }
  }

  if (operationType === 'wms.count-execution.complete') {
    const item = listItems(payload).find((candidate) =>
      matchesResource(candidate, 'countExecutionId', resourceId),
    )
    const postingStatus = normalized(item?.inventoryPostingStatus)
    if (postingStatus === 'posted') return { state: 'confirmed-success' }
    if (item && postingStatus === 'failed') {
      return confirmedFailure(item, '盘点库存过账失败，请刷新后按最新状态处理')
    }
    return { state: 'indeterminate' }
  }

  const alarm = listItems(payload).find((candidate) =>
    matchesResource(candidate, 'alarmEventId', resourceId),
  )
  if (!alarm) return { state: 'indeterminate' }
  if (operationType === 'iiot.alarm.acknowledge') {
    return text(alarm.acknowledgedAtUtc)
      ? { state: 'confirmed-success' }
      : { state: 'indeterminate' }
  }
  if (operationType === 'iiot.alarm.shelve') {
    return normalized(alarm.status) === 'shelved' &&
      text(alarm.shelvedAtUtc) &&
      text(alarm.shelvedUntilUtc)
      ? { state: 'confirmed-success' }
      : { state: 'indeterminate' }
  }
  if (operationType === 'iiot.alarm.unshelve') {
    const status = normalized(alarm.status)
    return status && status !== 'shelved'
      ? { state: 'confirmed-success' }
      : { state: 'indeterminate' }
  }

  return { state: 'indeterminate' }
}

interface ConfirmBusinessConsoleOperationBaseOptions {
  expectedOperationType: string
  expectedIdempotencyKey: string
  attempts?: number
  retryDelayMs?: number
  readback?: (path: string, receipt: BusinessConsoleOperationReceiptLike) => Promise<unknown>
}

export type ConfirmBusinessConsoleOperationOptions<
  TEnvelope extends BusinessConsoleOperationEnvelope,
> = ConfirmBusinessConsoleOperationBaseOptions &
  (
    | {
        expectedResourceId: string
        expectedResourceIdSelector?: never
      }
    | {
        expectedResourceId?: never
        expectedResourceIdSelector: (envelope: TEnvelope) => string | null | undefined
      }
  )

function requiredQuery(url: URL, name: string) {
  const value = url.searchParams.get(name)?.trim()
  if (!value) throw new Error(`回读地址缺少 ${name}`)
  return value
}

function requireExactResource(url: URL, name: string, resourceId: string) {
  const value = requiredQuery(url, name)
  if (value !== resourceId) throw new Error(`回读地址的 ${name} 与操作资源不一致`)
  return value
}

/**
 * Dispatches only the frozen operation/readback pairs through generated SDK
 * methods. The server-provided path is evidence to validate, never a URL to
 * fetch blindly.
 */
export async function readBusinessConsoleOperationState(
  path: string,
  receipt: BusinessConsoleOperationIdentity,
) {
  const operationType = receipt.operationType
  const resourceId = receipt.resourceId
  if (!operationType || !resourceId) throw new Error('回执缺少操作类型或资源标识')

  const url = new URL(path, 'http://business-console.local')
  const organizationId = requiredQuery(url, 'organizationId')
  const environmentId = requiredQuery(url, 'environmentId')

  if (
    operationType === 'wms.inbound-order.complete' &&
    url.pathname === '/api/business-console/v1/wms/inbound-orders'
  ) {
    const inboundOrderId = requireExactResource(url, 'inboundOrderId', resourceId)
    return (
      await listBusinessConsoleWmsInboundOrders({
        query: { organizationId, environmentId, inboundOrderId },
        throwOnError: true,
      })
    ).data
  }

  if (
    operationType === 'wms.outbound-order.complete' &&
    url.pathname === '/api/business-console/v1/wms/outbound-orders'
  ) {
    const outboundOrderId = requireExactResource(url, 'outboundOrderId', resourceId)
    return (
      await listBusinessConsoleWmsOutboundOrders({
        query: { organizationId, environmentId, outboundOrderId },
        throwOnError: true,
      })
    ).data
  }

  if (
    operationType === 'wms.count-execution.complete' &&
    url.pathname === '/api/business-console/v1/wms/count-executions'
  ) {
    const countExecutionId = requireExactResource(url, 'countExecutionId', resourceId)
    return (
      await listBusinessConsoleWmsCountExecutions({
        query: { organizationId, environmentId, countExecutionId },
        throwOnError: true,
      })
    ).data
  }

  if (operationType === 'mes.production-report.record') {
    const match = url.pathname.match(
      /^\/api\/business-console\/v1\/mes\/production-reports\/([^/]+)$/,
    )
    if (match) {
      const reportNo = decodeURIComponent(match[1]!)
      return (
        await getBusinessConsoleMesProductionReport({
          path: { reportNo },
          query: { organizationId, environmentId },
          throwOnError: true,
        })
      ).data
    }
  }

  if (
    ['iiot.alarm.acknowledge', 'iiot.alarm.shelve', 'iiot.alarm.unshelve'].includes(
      operationType,
    ) &&
    url.pathname === '/api/business-console/v1/equipment/alarms'
  ) {
    const alarmEventId = requireExactResource(url, 'alarmEventId', resourceId)
    return (
      await listBusinessConsoleEquipmentAlarms({
        query: { organizationId, environmentId, alarmEventId },
        throwOnError: true,
      })
    ).data
  }

  throw new Error(`操作 ${operationType} 没有受治理的回读映射`)
}

function wait(milliseconds: number) {
  return new Promise<void>((resolve) => setTimeout(resolve, milliseconds))
}

/**
 * A successful HTTP mutation response is not a business-state confirmation.
 * `confirmed` receipts resolve immediately; `accepted` receipts resolve only
 * after the server-provided same-origin GET readback proves the authoritative
 * resource state.
 */
export async function confirmBusinessConsoleOperation<
  TEnvelope extends BusinessConsoleOperationEnvelope,
>(envelope: TEnvelope, options: ConfirmBusinessConsoleOperationOptions<TEnvelope>) {
  if (envelope?.success !== true) {
    throw new BusinessOperationUnconfirmedError('写操作未返回成功业务信封，请保留当前操作并重试。')
  }

  const rawReceipt = envelope.data?.operationReceipt
  if (!rawReceipt) {
    throw new BusinessOperationUnconfirmedError(
      '写操作缺少权威回执，当前结果仍不确定，请勿重复发起新操作。',
    )
  }

  const receipt = parseOperationReceipt(rawReceipt)
  if (!receipt) {
    throw new BusinessOperationUnconfirmedError(
      '写操作回执缺少公共字段或不符合 confirmed/accepted 语义，当前结果仍不确定，请勿重复发起新操作。',
      requiredText(record(rawReceipt)?.operationType),
      requiredText(record(rawReceipt)?.readbackPath),
    )
  }

  const expectedOperationType = options.expectedOperationType.trim()
  const expectedIdempotencyKey = options.expectedIdempotencyKey.trim()
  const expectedResourceId =
    'expectedResourceId' in options
      ? options.expectedResourceId?.trim()
      : options.expectedResourceIdSelector(envelope)?.trim()
  if (
    !expectedOperationType ||
    !expectedIdempotencyKey ||
    !expectedResourceId ||
    receipt.operationType !== expectedOperationType ||
    receipt.idempotencyKey !== expectedIdempotencyKey ||
    receipt.resourceId !== expectedResourceId
  ) {
    throw new BusinessOperationUnconfirmedError(
      '写操作回执与本次业务意图不一致，当前结果仍不确定，请勿重复提交并刷新核实。',
      receipt.operationType,
      receipt.readbackPath ?? undefined,
    )
  }

  if (receipt.outcome === 'confirmed') {
    return envelope
  }

  const path = receipt.readbackPath
  if (!path.startsWith('/api/business-console/v1/')) {
    throw new BusinessOperationUnconfirmedError(
      '写操作回执不完整，无法安全确认业务状态；请保留当前意图并人工回读。',
      receipt.operationType,
      path,
    )
  }

  const attempts = Math.max(1, options.attempts ?? 3)
  const retryDelayMs = Math.max(0, options.retryDelayMs ?? 200)
  const readback = options.readback ?? readBusinessConsoleOperationState
  let lastError: unknown

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    let verdict: BusinessConsoleOperationReadbackVerdict | undefined
    try {
      const payload = await readback(path, receipt)
      verdict = verifyBusinessConsoleOperationReadback(receipt, payload)
    } catch (error) {
      lastError = error
    }
    if (verdict?.state === 'confirmed-success') return envelope
    if (verdict?.state === 'confirmed-business-failure') {
      throw new BusinessOperationFailedError(verdict.message, verdict.failureCode)
    }
    if (attempt + 1 < attempts && retryDelayMs > 0) await wait(retryDelayMs)
  }

  const detail = lastError instanceof Error ? `（${lastError.message}）` : ''
  throw new BusinessOperationUnconfirmedError(
    `请求已受理，但权威状态尚未确认${detail}。请保留当前意图键，按回读地址刷新后再重试。`,
    receipt.operationType,
    path,
  )
}
