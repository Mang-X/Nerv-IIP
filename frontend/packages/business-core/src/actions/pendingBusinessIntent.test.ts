import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  peekPendingBusinessIntent,
} from './pendingBusinessIntent'

const scope = {
  principalId: 'principal-1',
  organizationId: 'org-1',
  environmentId: 'env-1',
  operationType: 'wms.count-execution.complete',
  payloadFingerprint: 'count-1:[]',
}

describe('pending business intent session store', () => {
  beforeEach(() => {
    sessionStorage.clear()
    clearPendingBusinessIntent(scope)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('restores the same key after a page/composable is recreated', () => {
    const first = acquirePendingBusinessIntent(scope, () => 'key-1')
    const restored = acquirePendingBusinessIntent(scope, () => 'key-2')

    expect(first.idempotencyKey).toBe('key-1')
    expect(restored.idempotencyKey).toBe('key-1')
    expect(peekPendingBusinessIntent(scope)?.idempotencyKey).toBe('key-1')
  })

  it('rotates only after authoritative confirmation clears the intent', () => {
    acquirePendingBusinessIntent(scope, () => 'key-1')
    clearPendingBusinessIntent(scope)

    expect(acquirePendingBusinessIntent(scope, () => 'key-2').idempotencyKey).toBe('key-2')
  })

  it('isolates identical payloads by principal and business context', () => {
    acquirePendingBusinessIntent(scope, () => 'key-1')
    const other = acquirePendingBusinessIntent(
      { ...scope, principalId: 'principal-2' },
      () => 'key-2',
    )

    expect(other.idempotencyKey).toBe('key-2')
  })

  it('keeps the same key in memory when session storage rejects a write', () => {
    vi.stubGlobal('sessionStorage', {
      getItem: () => null,
      setItem: () => {
        throw new DOMException('storage unavailable', 'QuotaExceededError')
      },
    } as unknown as Storage)

    const first = acquirePendingBusinessIntent(scope, () => 'key-1')
    const restored = acquirePendingBusinessIntent(scope, () => 'key-2')

    expect(first.idempotencyKey).toBe('key-1')
    expect(restored.idempotencyKey).toBe('key-1')
  })

  it('does not resurrect a cleared intent from stale session data when the clear write fails', () => {
    acquirePendingBusinessIntent(scope, () => 'stale-key')
    const storageKey = sessionStorage.key(0)
    expect(storageKey).toBeTruthy()
    const staleSerializedEntries = sessionStorage.getItem(storageKey!)

    vi.stubGlobal('sessionStorage', {
      getItem: () => staleSerializedEntries,
      setItem: () => {
        throw new DOMException('storage unavailable', 'QuotaExceededError')
      },
    } as unknown as Storage)

    clearPendingBusinessIntent(scope)

    expect(peekPendingBusinessIntent(scope)).toBeUndefined()
    expect(acquirePendingBusinessIntent(scope, () => 'fresh-key').idempotencyKey).toBe('fresh-key')
    expect(peekPendingBusinessIntent(scope)?.idempotencyKey).toBe('fresh-key')
    expect(acquirePendingBusinessIntent(scope, () => 'newer-key').idempotencyKey).toBe('fresh-key')
  })

  it('removes a stale persisted snapshot when a clear cannot rewrite storage', async () => {
    acquirePendingBusinessIntent(scope, () => 'stale-key')
    const storageKey = sessionStorage.key(0)
    let staleSerializedEntries = sessionStorage.getItem(storageKey!)
    const removeItem = vi.fn(() => {
      staleSerializedEntries = null
    })
    vi.stubGlobal('sessionStorage', {
      getItem: () => staleSerializedEntries,
      setItem: () => {
        throw new DOMException('storage unavailable', 'QuotaExceededError')
      },
      removeItem,
    } as unknown as Storage)

    clearPendingBusinessIntent(scope)
    vi.resetModules()
    const refreshedStore = await import('./pendingBusinessIntent')

    expect(removeItem).toHaveBeenCalled()
    expect(refreshedStore.peekPendingBusinessIntent(scope)).toBeUndefined()
  })
})
