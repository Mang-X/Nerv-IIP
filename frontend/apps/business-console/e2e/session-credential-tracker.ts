export type SessionCredentialHeaders = Record<string, string | undefined>

export type SessionCredentialRequest = {
  headers: () => Record<string, string>
}

export type SessionCredentialSource = SessionCredentialHeaders | SessionCredentialRequest

function authorizationFrom(source: SessionCredentialSource): string {
  const headers =
    'headers' in source && typeof source.headers === 'function'
      ? source.headers()
      : (source as SessionCredentialHeaders)
  return headers.authorization ?? headers.Authorization ?? ''
}

export function createSessionCredentialTracker() {
  let current = ''

  return {
    observe(source: SessionCredentialSource) {
      const authorization = authorizationFrom(source).trim()
      if (authorization) current = authorization
    },
    headers(): SessionCredentialHeaders | undefined {
      return current ? { authorization: current } : undefined
    },
    clear() {
      current = ''
    },
  }
}
