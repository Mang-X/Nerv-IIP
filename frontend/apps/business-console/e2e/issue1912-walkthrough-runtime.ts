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

export type AuthorizedWorkPoolScope = Readonly<{
  displayName: string
  poolCode: string
  scopeId: string
  scopeKind: string
  siteCode: string
}>

export type AuthorizedWorkSiteScope = Readonly<{
  displayName: string
  poolCode: null
  scopeId: string
  scopeKind: string
  siteCode: string
}>

export type PublicError = Readonly<{
  code: string
  message: string
}>

export type AuthorizedScopeRef = Pick<AuthorizedWorkScope, 'scopeKind' | 'scopeId'>

export type AuthorizedWorkPoolAssignment = Readonly<{
  resourceId: string
  scope: AuthorizedWorkPoolScope
  body: Readonly<{
    poolCode: string
    operatorPrincipalId: string
    idempotencyKey: string
    expectedVersion: number
  }>
}>

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
    const scope = parseAuthorizedWorkScope(item)
    if (scope) return scope
  }
  return undefined
}

/**
 * Selects a concrete work-pool item for mutations. The catalog may also expose self and site
 * scopes; those deliberately do not provide a pool code and must never be turned into an
 * assignment request. An optional resource site narrows the selection without inventing a scope.
 */
export function selectAuthorizedWorkPoolScope(
  value: unknown,
  siteCode?: string,
): AuthorizedWorkPoolScope | undefined {
  const items = asRecord(dataOf(value)).items
  if (!Array.isArray(items)) return undefined
  const normalizedSiteCode = siteCode?.trim()

  for (const item of items) {
    const scope = parseAuthorizedWorkScope(item)
    if (
      !scope ||
      scope.scopeKind.toLowerCase() !== 'work-pool' ||
      scope.poolCode === null ||
      scope.siteCode === null ||
      (normalizedSiteCode !== undefined &&
        normalizedSiteCode !== '' &&
        scope.siteCode !== normalizedSiteCode)
    ) {
      continue
    }
    return {
      displayName: scope.displayName,
      poolCode: scope.poolCode,
      scopeId: scope.scopeId,
      scopeKind: scope.scopeKind,
      siteCode: scope.siteCode,
    }
  }
  return undefined
}

/**
 * Selects a site item for reading resources that have not received an assignment yet. The
 * catalog's site scope is the only source for the site id; missing or malformed items fail closed.
 */
export function selectAuthorizedWorkSiteScope(
  value: unknown,
  siteCode?: string,
): AuthorizedWorkSiteScope | undefined {
  const items = asRecord(dataOf(value)).items
  if (!Array.isArray(items)) return undefined
  const normalizedSiteCode = siteCode?.trim()

  for (const item of items) {
    const scope = parseAuthorizedWorkScope(item)
    if (
      !scope ||
      scope.scopeKind.toLowerCase() !== 'site' ||
      scope.poolCode !== null ||
      scope.siteCode === null ||
      (normalizedSiteCode !== undefined &&
        normalizedSiteCode !== '' &&
        scope.siteCode !== normalizedSiteCode)
    ) {
      continue
    }
    return {
      displayName: scope.displayName,
      poolCode: null,
      scopeId: scope.scopeId,
      scopeKind: scope.scopeKind,
      siteCode: scope.siteCode,
    }
  }
  return undefined
}

function parseAuthorizedWorkScope(value: unknown): AuthorizedWorkScope | undefined {
  const record = asRecord(value)
  const scopeKind = textOf(record.scopeKind).trim()
  const scopeId = textOf(record.scopeId).trim()
  const displayName = textOf(record.displayName).trim()
  if (scopeKind === '' || scopeId === '' || displayName === '') return undefined
  return {
    displayName,
    poolCode: nullableText(record.poolCode),
    scopeId,
    scopeKind,
    siteCode: nullableText(record.siteCode),
  }
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
 * Builds the public assignment body only from a catalog-selected work-pool and the WMS actor.
 * Returning `called: false` is the fail-closed branch: callers must not invoke the mutation.
 */
export function buildAuthorizedWorkPoolAssignment(
  context: Pick<WalkthroughActorContext, 'actor' | 'principalId'>,
  scope: AuthorizedWorkPoolScope | undefined,
  resourceId: string,
  idempotencyKey: string,
  expectedVersion: number,
):
  | { called: true; request: AuthorizedWorkPoolAssignment }
  | { called: false; reason: 'missing-authorized-scope' | 'wms-worker-context-required' } {
  if (context.actor !== 'wms-worker') {
    return { called: false, reason: 'wms-worker-context-required' }
  }
  if (!scope) return { called: false, reason: 'missing-authorized-scope' }
  return {
    called: true,
    request: {
      resourceId,
      scope,
      body: {
        poolCode: scope.poolCode,
        operatorPrincipalId: context.principalId,
        idempotencyKey,
        expectedVersion,
      },
    },
  }
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
