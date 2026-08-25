export type SessionCredentialHeaders = Record<string, string | undefined>

export type SessionCredentialPage = object

export type SessionCredentialScope = {
  origin: string
  page: SessionCredentialPage
  businessPathPrefix: string
  refreshPath: string
}

export type SessionCredentialRequest = {
  page: SessionCredentialPage
  request: {
    url: () => string
    headers: () => Record<string, string>
  }
}

export type SessionCredentialRefreshResponse = {
  page: SessionCredentialPage
  response: {
    url: () => string
    status: () => number
    json: () => Promise<unknown>
  }
}

function asRecord(value: unknown): Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {}
}

function authorizationFromHeaders(headers: Record<string, string | undefined>): string {
  return headers.authorization ?? headers.Authorization ?? ''
}

function authorizationFromRefreshPayload(payload: unknown): string {
  const envelope = asRecord(payload)
  const data = asRecord(envelope.data ?? payload)
  const accessToken = data.accessToken
  if (typeof accessToken !== 'string' || !accessToken.trim()) {
    throw new Error('refresh response did not contain an access token')
  }

  return `Bearer ${accessToken.trim()}`
}

export function createSessionCredentialTracker(scope: SessionCredentialScope) {
  const expectedOrigin = new URL(scope.origin).origin
  let current = ''
  let generation = 0
  let refreshResponseObserved = false
  let pendingRefreshResponses = 0
  let refreshQueue: Promise<void> = Promise.resolve()

  const isScopedPage = (page: SessionCredentialPage) => page === scope.page
  const isScopedUrl = (rawUrl: string, expectedPath: string) => {
    try {
      const url = new URL(rawUrl)
      return url.origin === expectedOrigin && url.pathname === expectedPath
    } catch {
      return false
    }
  }

  return {
    observeRequest(source: SessionCredentialRequest) {
      if (!isScopedPage(source.page) || refreshResponseObserved || pendingRefreshResponses > 0) {
        return
      }

      let url: URL
      try {
        url = new URL(source.request.url())
      } catch {
        return
      }
      if (url.origin !== expectedOrigin || !url.pathname.startsWith(scope.businessPathPrefix)) {
        return
      }

      const authorization = authorizationFromHeaders(source.request.headers()).trim()
      if (authorization) current = authorization
    },
    observeRefreshResponse(source: SessionCredentialRefreshResponse): Promise<void> {
      if (!isScopedPage(source.page) || !isScopedUrl(source.response.url(), scope.refreshPath)) {
        return Promise.resolve()
      }

      pendingRefreshResponses += 1
      const responseGeneration = generation
      const capture = refreshQueue.then(async () => {
        if (source.response.status() !== 200) {
          throw new Error('refresh response was not successful')
        }

        const authorization = authorizationFromRefreshPayload(await source.response.json())
        if (generation === responseGeneration) {
          current = authorization
          refreshResponseObserved = true
        }
      })
      refreshQueue = capture
        .catch(() => undefined)
        .finally(() => {
          pendingRefreshResponses -= 1
        })
      return capture
    },
    async headers(): Promise<SessionCredentialHeaders | undefined> {
      await refreshQueue
      return current ? { authorization: current } : undefined
    },
    clear() {
      generation += 1
      current = ''
      refreshResponseObserved = false
    },
  }
}

export async function callWithSessionCredential<T>(
  tracker: { headers: () => Promise<SessionCredentialHeaders | undefined> },
  operation: (headers: SessionCredentialHeaders | undefined) => Promise<T>,
): Promise<T> {
  return operation(await tracker.headers())
}

export async function withSessionCredentialCleanup<T>(
  operation: () => Promise<T>,
  cleanup: () => void,
): Promise<T> {
  try {
    return await operation()
  } finally {
    cleanup()
  }
}
