import { writeFile } from 'node:fs/promises'
import type { APIResponse } from '@playwright/test'
import { createHash } from 'node:crypto'

export function failureDetail(value: unknown): string {
  return `failure-sha256:${createHash('sha256').update(String(value)).digest('hex')}`
}

export type JsonRecord = Record<string, unknown>
export type Conclusion = 'runtime-confirmed' | 'gap' | 'not-verified'
export type AutomationMode = 'automatic' | 'manual' | 'mixed'
export type WalkthroughActor = 'erp-admin' | 'wms-worker'

export const REQUIRED_NODES = [
  'rfq-supplier-quotation',
  'supplier-quotation-purchase-order',
  'purchase-order-approval',
  'purchase-order-receipt',
  'receipt-inbound-inventory',
  'sales-quotation-sales-order',
  'sales-order-demand',
  'demand-mrp-suggestion',
  'mrp-suggestion-mes-work-order',
  'mes-work-order-production',
  'production-finished-goods-receipt',
  'finished-goods-inventory',
  'sales-order-delivery',
  'delivery-wms-outbound',
  'wms-completed-erp-delivery',
  'erp-account-receivable',
] as const

export type NodeName = (typeof REQUIRED_NODES)[number]

export type EvidenceEntry = {
  node: NodeName
  sourceObject: string
  downstreamObject: string
  stableKey: string
  automationMode: AutomationMode
  request: JsonRecord | null
  responseOrLog: unknown
  conclusion: Conclusion
  demoWording: string
  responsibilityIssue: string | null
}

export type UiProof = {
  node: NodeName
  actor: WalkthroughActor
  principalId: string
  page: string
  pageHttpStatus: number
  listPath: string
  listHttpStatus: number
  listQuery: JsonRecord
  stableKey: string
  renderedRowText: string
  emptyText: string
  screenshot: string
}

export class PublicCallError extends Error {
  constructor(
    readonly method: 'GET' | 'POST',
    readonly path: string,
    readonly status: number,
    readonly request: JsonRecord,
    readonly payload: unknown,
  ) {
    super(`${method} ${path} returned HTTP ${status}: ${failureDetail(JSON.stringify(payload))}`)
    this.name = 'PublicCallError'
  }
}

export class PollTimeoutError extends Error {
  constructor(
    readonly path: string,
    readonly lastData: unknown,
    readonly attempts: number,
    readonly timeoutMs: number,
  ) {
    super(
      `Timed out after ${attempts} attempts in ${timeoutMs}ms waiting for ${path}; last=${failureDetail(JSON.stringify(lastData))}`,
    )
    this.name = 'PollTimeoutError'
  }
}

export function asRecord(value: unknown): JsonRecord {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as JsonRecord)
    : {}
}

export function dataOf(value: unknown): unknown {
  return asRecord(value).data ?? value
}

export function rowsOf(value: unknown): JsonRecord[] {
  const data = dataOf(value)
  if (Array.isArray(data)) return data.map(asRecord)
  const items = asRecord(data).items
  return Array.isArray(items) ? items.map(asRecord) : []
}

export function inventoryStateFingerprint(value: unknown): JsonRecord {
  const data = asRecord(dataOf(value))
  const items = Array.isArray(data.items)
    ? data.items
        .map(asRecord)
        .map((item) => ({
          locationCode: textOf(item.locationCode),
          lotNo: item.lotNo ?? null,
          serialNo: item.serialNo ?? null,
          qualityStatus: textOf(item.qualityStatus),
          ownerType: textOf(item.ownerType),
          ownerId: item.ownerId ?? null,
          onHandQuantity: item.onHandQuantity ?? null,
          reservedQuantity: item.reservedQuantity ?? null,
          availableQuantity: item.availableQuantity ?? null,
          inventoryValue: item.inventoryValue ?? null,
        }))
        .sort((left, right) =>
          `${left.locationCode}/${left.lotNo ?? ''}/${left.serialNo ?? ''}`.localeCompare(
            `${right.locationCode}/${right.lotNo ?? ''}/${right.serialNo ?? ''}`,
          ),
        )
    : []
  return {
    onHandQuantity: data.onHandQuantity ?? null,
    reservedQuantity: data.reservedQuantity ?? null,
    availableQuantity: data.availableQuantity ?? null,
    inventoryValue: data.inventoryValue ?? null,
    items,
  }
}

export function inventoryMovementFingerprint(value: unknown): JsonRecord {
  const data = asRecord(dataOf(value))
  const items = rowsOf(value)
    .map((item) => ({
      movementId: textOf(item.movementId),
      movementType: textOf(item.movementType),
      sourceService: textOf(item.sourceService),
      sourceDocumentId: textOf(item.sourceDocumentId),
      sourceDocumentLineId: item.sourceDocumentLineId ?? null,
      idempotencyKey: textOf(item.idempotencyKey),
      skuCode: textOf(item.skuCode),
      uomCode: textOf(item.uomCode),
      siteCode: textOf(item.siteCode),
      locationCode: textOf(item.locationCode),
      lotNo: item.lotNo ?? null,
      serialNo: item.serialNo ?? null,
      quantity: item.quantity ?? null,
    }))
    .sort((left, right) => left.movementId.localeCompare(right.movementId))
  return {
    totalCount: data.totalCount ?? null,
    inboundQuantityTotal: data.inboundQuantityTotal ?? null,
    outboundQuantityTotal: data.outboundQuantityTotal ?? null,
    items,
  }
}

export function textOf(value: unknown): string {
  return value === null || value === undefined ? '' : String(value)
}

export function safeText(value: unknown): string {
  return textOf(value)
    .replace(
      /(["']?(?:authorization|password|(?:access|refresh|id)?[_-]?token|secret|connectionstring|jwt)["']?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|[^\s,;}]+)/gi,
      '$1<redacted-secret>',
    )
    .replace(/bearer\s+[^\s"']+/gi, '<redacted-credential>')
    .replace(/authorization/gi, '<redacted-header>')
    .replace(/password/gi, '<redacted-field>')
    .replace(/(?:access|refresh|id)?[_-]?token/gi, '<redacted-field>')
    .replace(/(?:secret|connectionstring|jwt)/gi, '<redacted-field>')
    .slice(0, 1600)
}

export function credentialDigest(headers: { authorization?: string } | undefined): string {
  const authorization = headers?.authorization?.trim()
  return authorization ? createHash('sha256').update(authorization).digest('hex').slice(0, 16) : ''
}

export function publicJson(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(publicJson)
  if (value === null || typeof value !== 'object') {
    return typeof value === 'string' ? safeText(value) : value
  }
  return Object.fromEntries(
    Object.entries(value as JsonRecord)
      .filter(
        ([key]) =>
          !/(authorization|password|(?:access|refresh|id)?[_-]?token|secret|connectionstring|jwt)/i.test(
            key,
          ),
      )
      .map(([key, item]) => [key, publicJson(item)]),
  )
}

export async function jsonOf(response: APIResponse): Promise<unknown> {
  const contentType = response.headers()['content-type'] ?? ''
  if (!contentType.includes('json')) return { text: failureDetail(await response.text()) }
  return response.json()
}

export function dateOnly(date: Date): string {
  return date.toISOString().slice(0, 10)
}

export function errorText(error: unknown): string {
  return failureDetail(error instanceof Error ? error.message : error)
}

export function createWalkthroughEvidence() {
  const evidence = new Map<NodeName, EvidenceEntry>()
  const setup: JsonRecord[] = []
  const uiEvidence: UiProof[] = []
  const failedRequests: JsonRecord[] = []
  const expectedRequestCancellations: JsonRecord[] = []
  const expectedBusinessRejections: JsonRecord[] = []
  const pageErrors: string[] = []

  for (const node of REQUIRED_NODES) {
    evidence.set(node, {
      node,
      sourceObject: 'not-observed',
      downstreamObject: 'not-observed',
      stableKey: node,
      automationMode: 'automatic',
      request: null,
      responseOrLog: { reason: 'upstream evidence was not established in this run' },
      conclusion: 'not-verified',
      demoWording: `${node}: this run did not establish a public runtime association.`,
      responsibilityIssue: null,
    })
  }

  const record = (entry: EvidenceEntry) => evidence.set(entry.node, entry)

  const markFailure = (node: NodeName, error: unknown, mode: AutomationMode = 'automatic') => {
    const current = evidence.get(node)!
    const publicError =
      error instanceof PublicCallError
        ? {
            error: errorText(error),
            request: error.request,
            response: failureDetail(JSON.stringify(error.payload)),
          }
        : error instanceof PollTimeoutError
          ? {
              error: errorText(error),
              path: error.path,
              attempts: error.attempts,
              timeoutMs: error.timeoutMs,
              lastData: failureDetail(JSON.stringify(error.lastData)),
            }
          : { error: errorText(error) }
    record({
      ...current,
      automationMode: mode,
      request: error instanceof PublicCallError ? error.request : current.request,
      responseOrLog: publicError,
      conclusion: 'gap',
      demoWording: `${node}: the public runtime attempt did not converge; this is a gap, not a completed hop.`,
      responsibilityIssue: null,
    })
  }

  const write = async (path: string, metadata: JsonRecord) => {
    const entries = REQUIRED_NODES.map((node) => evidence.get(node)!)
    await writeFile(
      path,
      JSON.stringify(
        {
          ...metadata,
          setup,
          identityIsolation: setup.find((item) => item.kind === 'identityIsolation') ?? null,
          expectedBusinessRejections,
          uiEvidence,
          failedRequests,
          expectedRequestCancellations,
          pageErrors,
          entries,
          summary: Object.fromEntries(
            (['runtime-confirmed', 'gap', 'not-verified'] as const).map((conclusion) => [
              conclusion,
              entries.filter((entry) => entry.conclusion === conclusion).length,
            ]),
          ),
          conclusion:
            entries.every((entry) => entry.conclusion === 'runtime-confirmed') &&
            failedRequests.length === 0 &&
            pageErrors.length === 0
              ? 'runtime-confirmed'
              : 'not-verified',
        },
        null,
        2,
      ),
      'utf8',
    )
  }
  return {
    evidence,
    record,
    markFailure,
    setup,
    uiEvidence,
    failedRequests,
    expectedRequestCancellations,
    expectedBusinessRejections,
    pageErrors,
    write,
  }
}
