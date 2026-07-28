export interface PendingBusinessIntentScope {
  principalId: string
  organizationId: string
  environmentId: string
  operationType: string
  payloadFingerprint: string
}

export interface PendingBusinessIntent extends PendingBusinessIntentScope {
  idempotencyKey: string
  createdAtUtc: string
  payloadSnapshot?: unknown
}

const STORAGE_KEY = 'nerv-iip.pending-business-intents.v1'
const memoryEntries = new Map<string, PendingBusinessIntent>()
const clearedScopeKeys = new Set<string>()
let storageLoaded = false

function scopeKey(scope: PendingBusinessIntentScope) {
  return [
    scope.principalId,
    scope.organizationId,
    scope.environmentId,
    scope.operationType,
    scope.payloadFingerprint,
  ].join('\u001f')
}

function storage(): Storage | undefined {
  try {
    return globalThis.sessionStorage
  } catch {
    return undefined
  }
}

function loadEntries() {
  if (storageLoaded) return new Map(memoryEntries)
  storageLoaded = true
  const target = storage()
  if (!target) return new Map(memoryEntries)
  try {
    const parsed = JSON.parse(target.getItem(STORAGE_KEY) ?? '[]') as unknown
    if (!Array.isArray(parsed)) return new Map(memoryEntries)
    for (const candidate of parsed) {
      if (
        candidate &&
        typeof candidate === 'object' &&
        typeof candidate.principalId === 'string' &&
        typeof candidate.organizationId === 'string' &&
        typeof candidate.environmentId === 'string' &&
        typeof candidate.operationType === 'string' &&
        typeof candidate.payloadFingerprint === 'string' &&
        typeof candidate.idempotencyKey === 'string' &&
        typeof candidate.createdAtUtc === 'string'
      ) {
        const intent = candidate as PendingBusinessIntent
        const key = scopeKey(intent)
        if (!clearedScopeKeys.has(key)) memoryEntries.set(key, intent)
      }
    }
  } catch {
    // Ignore unavailable/corrupt session data; the runtime memory copy remains available.
  }
  return new Map(memoryEntries)
}

function saveEntries(entries: Map<string, PendingBusinessIntent>) {
  memoryEntries.clear()
  for (const [key, value] of entries) memoryEntries.set(key, value)
  const target = storage()
  try {
    target?.setItem(STORAGE_KEY, JSON.stringify([...entries.values()]))
  } catch {
    // A stale durable snapshot is more dangerous than no snapshot: after a reload it could
    // resurrect a cleared key. Removal normally still works for quota failures.
    try {
      target?.removeItem(STORAGE_KEY)
    } catch {
      // Memory remains authoritative for this runtime when storage is entirely unavailable.
    }
  }
}

export function acquirePendingBusinessIntent(
  scope: PendingBusinessIntentScope,
  createIdempotencyKey: () => string,
  payloadSnapshot?: unknown,
) {
  const entries = loadEntries()
  const key = scopeKey(scope)
  const existing = entries.get(key)
  if (existing) return existing
  const intent: PendingBusinessIntent = {
    ...scope,
    idempotencyKey: createIdempotencyKey(),
    createdAtUtc: new Date().toISOString(),
    ...(payloadSnapshot === undefined ? {} : { payloadSnapshot }),
  }
  entries.set(key, intent)
  saveEntries(entries)
  return intent
}

/** Clear only after authoritative confirmation or an explicit user abandon. */
export function clearPendingBusinessIntent(scope: PendingBusinessIntentScope) {
  const entries = loadEntries()
  const key = scopeKey(scope)
  entries.delete(key)
  clearedScopeKeys.add(key)
  saveEntries(entries)
}

export function peekPendingBusinessIntent(scope: PendingBusinessIntentScope) {
  return loadEntries().get(scopeKey(scope))
}
