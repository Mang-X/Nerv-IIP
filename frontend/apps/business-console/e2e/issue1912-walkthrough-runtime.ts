type JsonRecord = Record<string, unknown>

export type WalkthroughActor = 'erp-admin' | 'wms-worker'

export type WalkthroughActorContext = Readonly<{
  actor: WalkthroughActor
  principalId: string
  authorization: string
}>

export type AuthorizedWorkScope = Readonly<{
  displayName: string
  poolCode: string | null
  scopeId: string
  scopeKind: string
  siteCode: string | null
}>

export type PublicError = Readonly<{
  code: string
  message: string
}>

export type AuthorizedScopeRef = Pick<AuthorizedWorkScope, 'scopeKind' | 'scopeId'>

function asRecord(value: unknown): JsonRecord {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as JsonRecord)
    : {}
}

function textOf(value: unknown): string {
  return typeof value === 'string'
    ? value
    : value === null || value === undefined
      ? ''
      : String(value)
}

function nullableText(value: unknown): string | null {
  const text = textOf(value).trim()
  return text === '' ? null : text
}

function dataOf(value: unknown): unknown {
  return asRecord(value).data ?? value
}

function nestedRecords(value: unknown): JsonRecord[] {
  const records: JsonRecord[] = []
  const queue: unknown[] = [value]
  const seen = new Set<object>()
  while (queue.length > 0) {
    const current = queue.shift()
    if (current === null || typeof current !== 'object') continue
    if (seen.has(current)) continue
    seen.add(current)
    if (Array.isArray(current)) {
      queue.push(...current)
      continue
    }
    const record = asRecord(current)
    records.push(record)
    queue.push(...Object.values(record))
  }
  return records
}

function firstField(records: readonly JsonRecord[], keys: readonly string[]): string {
  for (const record of records) {
    for (const key of keys) {
      const value = textOf(record[key]).trim()
      if (value !== '') return value
    }
  }
  return ''
}

/**
 * Reads only the public WarehouseWorkScopeCatalogItem fields. An absent or incomplete
 * catalog is deliberately represented as undefined so callers cannot invent a scope.
 */
export function selectAuthorizedWorkScope(value: unknown): AuthorizedWorkScope | undefined {
  const items = asRecord(dataOf(value)).items
  if (!Array.isArray(items)) return undefined

  for (const item of items) {
    const record = asRecord(item)
    const scopeKind = textOf(record.scopeKind).trim()
    const scopeId = textOf(record.scopeId).trim()
    const displayName = textOf(record.displayName).trim()
    if (scopeKind === '' || scopeId === '' || displayName === '') continue
    return {
      displayName,
      poolCode: nullableText(record.poolCode),
      scopeId,
      scopeKind,
      siteCode: nullableText(record.siteCode),
    }
  }
  return undefined
}

/**
 * Extracts the stable public error code and message from a ResponseData or downstream envelope.
 * The WMS 403 envelope exposes its reason as `message`, so it is used as the code only when an
 * explicit code/reason field is absent.
 */
export function extractPublicError(value: unknown): PublicError {
  const records = nestedRecords(value)
  const message = firstField(records, ['message', 'detail', 'errorMessage', 'title'])
  const code = firstField(records, ['code', 'errorCode', 'reason', 'reasonCode']) || message
  return { code, message: message || code }
}

/**
 * Keeps actor/principal/credential selection in one runtime seam so a call cannot silently use
 * the other browser context's credential.
 */
export async function runWithActorContext<T>(
  context: WalkthroughActorContext,
  operation: (context: WalkthroughActorContext) => Promise<T>,
): Promise<T> {
  return operation(context)
}

/**
 * A missing authorization scope is a hard stop: the downstream mutation callback is never run.
 */
export async function runWithAuthorizedScope<T>(
  scope: AuthorizedScopeRef | undefined,
  operation: (scope: AuthorizedScopeRef) => Promise<T>,
): Promise<{ called: true; value: T } | { called: false; reason: 'missing-authorized-scope' }> {
  if (!scope) return { called: false, reason: 'missing-authorized-scope' }
  return { called: true, value: await operation(scope) }
}
