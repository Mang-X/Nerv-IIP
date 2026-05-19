import { getConsoleMe, loginConsole, logoutConsole, refreshConsole } from '@/api/auth'
import type { ConsoleAuthResponse, ConsolePrincipalResponse } from '@nerv-iip/api-client'
import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

const STORAGE_KEY = 'nerv-iip.console.auth'

interface StoredSession {
  principal?: ConsolePrincipalResponse
  refreshToken: string
  sessionId: string
}

type CompleteConsoleAuthResponse = ConsoleAuthResponse & {
  accessToken: string
  expiresAtUtc: string
  principal: ConsolePrincipalResponse
  refreshToken: string
  sessionId: string
}

export const useAuthStore = defineStore('auth', () => {
  const accessToken = shallowRef<string>()
  const refreshToken = shallowRef<string>()
  const sessionId = shallowRef<string>()
  const expiresAtUtc = shallowRef<string>()
  const principal = shallowRef<ConsolePrincipalResponse>()
  const restoreStatus = shallowRef<'idle' | 'restoring' | 'restored' | 'failed'>('idle')
  const authError = shallowRef<string>()

  const isAuthenticated = computed(() => Boolean(accessToken.value && principal.value))
  const isRestoring = computed(() => restoreStatus.value === 'restoring')
  const displayName = computed(() => principal.value?.loginName ?? 'Unknown user')

  async function login(loginName: string, password: string) {
    authError.value = undefined
    try {
      applySession(await loginConsole({ loginName, password }))
    } catch (error) {
      clearSession('login-failed')
      authError.value = error instanceof Error ? error.message : 'Unable to sign in.'
      throw error
    }
  }

  async function restoreSession() {
    if (restoreStatus.value === 'restoring') {
      return
    }

    const stored = readStoredSession()
    if (!stored) {
      restoreStatus.value = 'failed'
      return
    }

    restoreStatus.value = 'restoring'
    try {
      applySession(await refreshConsole({ refreshToken: stored.refreshToken }))
      restoreStatus.value = 'restored'
    } catch {
      clearSession('restore-failed')
      restoreStatus.value = 'failed'
    }
  }

  async function refreshSession() {
    if (!refreshToken.value) {
      clearSession('missing-refresh-token')
      return
    }

    applySession(await refreshConsole({ refreshToken: refreshToken.value }))
  }

  async function loadPrincipal() {
    if (!accessToken.value) {
      clearSession('missing-access-token')
      return
    }

    principal.value = await getConsoleMe(accessToken.value)
    persistSession()
  }

  async function logout() {
    const token = accessToken.value
    const currentSessionId = sessionId.value
    clearSession('logout')
    if (token) {
      void logoutConsole(token, { sessionId: currentSessionId }).catch(() => undefined)
    }
  }

  function clearSession(_reason: string) {
    accessToken.value = undefined
    refreshToken.value = undefined
    sessionId.value = undefined
    expiresAtUtc.value = undefined
    principal.value = undefined
    localStorage.removeItem(STORAGE_KEY)
  }

  function applySession(session: ConsoleAuthResponse) {
    const completeSession = assertCompleteSession(session)

    accessToken.value = completeSession.accessToken
    refreshToken.value = completeSession.refreshToken
    sessionId.value = completeSession.sessionId
    expiresAtUtc.value = completeSession.expiresAtUtc
    principal.value = completeSession.principal
    persistSession()
  }

  function persistSession() {
    if (!refreshToken.value || !sessionId.value) {
      return
    }

    const stored: StoredSession = {
      principal: principal.value,
      refreshToken: refreshToken.value,
      sessionId: sessionId.value,
    }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(stored))
  }

  function readStoredSession(): StoredSession | undefined {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return undefined
    }

    try {
      const parsed = JSON.parse(raw) as Partial<StoredSession>
      if (!isNonEmptyString(parsed.refreshToken) || !isNonEmptyString(parsed.sessionId)) {
        localStorage.removeItem(STORAGE_KEY)
        return undefined
      }

      return {
        principal: parsed.principal,
        refreshToken: parsed.refreshToken,
        sessionId: parsed.sessionId,
      }
    } catch {
      localStorage.removeItem(STORAGE_KEY)
      return undefined
    }
  }

  return {
    accessToken,
    authError,
    clearSession,
    displayName,
    expiresAtUtc,
    isAuthenticated,
    isRestoring,
    loadPrincipal,
    login,
    logout,
    principal,
    refreshSession,
    refreshToken,
    restoreSession,
    restoreStatus,
    sessionId,
  }
})

function assertCompleteSession(session: ConsoleAuthResponse): CompleteConsoleAuthResponse {
  if (
    isNonEmptyString(session.accessToken) &&
    isNonEmptyString(session.refreshToken) &&
    isNonEmptyString(session.sessionId) &&
    isNonEmptyString(session.expiresAtUtc) &&
    isRecord(session.principal)
  ) {
    return session as CompleteConsoleAuthResponse
  }

  throw new Error('Authentication service returned an invalid session.')
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
