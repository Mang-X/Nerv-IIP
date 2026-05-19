import { getConsoleMe, loginConsole, logoutConsole, refreshConsole } from '@/api/auth'
import type { ConsoleAuthResponse, ConsolePrincipalResponse } from '@nerv-iip/api-client'
import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

const STORAGE_KEY = 'nerv-iip.console.auth'
const REFRESH_BEFORE_EXPIRY_MS = 60_000
const MAX_REFRESH_DELAY_MS = 2_147_483_647

interface StoredSession {
  principal?: ConsolePrincipalResponse
  refreshToken: string
  sessionId: string
}

type ValidConsoleAuthResponse = ConsoleAuthResponse & {
  accessToken: string
  expiresAtUtc: string
  principal?: ConsolePrincipalResponse
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
  let refreshTimer: ReturnType<typeof setTimeout> | undefined
  let refreshPromise: Promise<void> | undefined
  let sessionExpiredHandler: ((reason: string) => void) | undefined
  let sessionEpoch = 0

  const isAuthenticated = computed(() => Boolean(accessToken.value && principal.value))
  const isRestoring = computed(() => restoreStatus.value === 'restoring')
  const displayName = computed(() => principal.value?.loginName ?? 'Unknown user')

  async function login(loginName: string, password: string) {
    authError.value = undefined
    try {
      await applySession(await loginConsole({ loginName, password }))
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
    const restoreEpoch = sessionEpoch
    try {
      const restoredSession = await refreshConsole({ refreshToken: stored.refreshToken })
      if (sessionEpoch !== restoreEpoch) {
        restoreStatus.value = 'failed'
        return
      }

      await applySession(restoredSession)
      restoreStatus.value = 'restored'
    } catch {
      if (sessionEpoch === restoreEpoch) {
        clearSession('restore-failed')
      }
      restoreStatus.value = 'failed'
    }
  }

  async function refreshSession() {
    if (refreshPromise) {
      return refreshPromise
    }

    const currentRefreshToken = refreshToken.value
    if (!currentRefreshToken) {
      clearSession('missing-refresh-token')
      return
    }

    refreshPromise = refreshSessionOnce(currentRefreshToken)
    return refreshPromise
  }

  async function loadPrincipal() {
    if (!accessToken.value) {
      clearSession('missing-access-token')
      return
    }

    const currentAccessToken = accessToken.value
    const loadEpoch = sessionEpoch
    const loadedPrincipal = await getConsoleMe(currentAccessToken)
    if (sessionEpoch !== loadEpoch || accessToken.value !== currentAccessToken) {
      return
    }

    principal.value = loadedPrincipal
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
    sessionEpoch += 1
    clearRefreshTimer()
    accessToken.value = undefined
    refreshToken.value = undefined
    sessionId.value = undefined
    expiresAtUtc.value = undefined
    principal.value = undefined
    localStorage.removeItem(STORAGE_KEY)
  }

  function setSessionExpiredHandler(handler?: (reason: string) => void) {
    sessionExpiredHandler = handler
  }

  async function refreshSessionOnce(currentRefreshToken: string) {
    const refreshEpoch = sessionEpoch
    let responseWasCurrent = false
    try {
      const refreshedSession = await refreshConsole({ refreshToken: currentRefreshToken })
      if (!isRefreshStillCurrent(refreshEpoch, currentRefreshToken)) {
        return
      }

      responseWasCurrent = true
      await applySession(refreshedSession)
    } catch (error) {
      if (responseWasCurrent || isRefreshStillCurrent(refreshEpoch, currentRefreshToken)) {
        clearSession('refresh-failed')
        sessionExpiredHandler?.('refresh-failed')
      }
      throw error
    } finally {
      refreshPromise = undefined
    }
  }

  async function applySession(session: ConsoleAuthResponse) {
    const completeSession = assertValidSession(session)

    sessionEpoch += 1
    accessToken.value = completeSession.accessToken
    refreshToken.value = completeSession.refreshToken
    sessionId.value = completeSession.sessionId
    expiresAtUtc.value = completeSession.expiresAtUtc
    principal.value = completeSession.principal
    persistSession()
    scheduleRefresh()

    if (!principal.value) {
      await loadPrincipal()
    }
  }

  function isRefreshStillCurrent(refreshEpoch: number, originalRefreshToken: string) {
    return sessionEpoch === refreshEpoch && refreshToken.value === originalRefreshToken
  }

  function scheduleRefresh() {
    clearRefreshTimer()

    const expiresAtMs = Date.parse(expiresAtUtc.value ?? '')
    if (!Number.isFinite(expiresAtMs)) {
      return
    }

    const refreshInMs = Math.min(
      Math.max(expiresAtMs - Date.now() - REFRESH_BEFORE_EXPIRY_MS, 0),
      MAX_REFRESH_DELAY_MS,
    )
    refreshTimer = setTimeout(() => {
      refreshTimer = undefined
      void refreshSession().catch(() => undefined)
    }, refreshInMs)
    unrefTimer(refreshTimer)
  }

  function clearRefreshTimer() {
    if (!refreshTimer) {
      return
    }

    clearTimeout(refreshTimer)
    refreshTimer = undefined
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
    setSessionExpiredHandler,
    sessionId,
  }
})

function assertValidSession(session: ConsoleAuthResponse): ValidConsoleAuthResponse {
  if (
    isNonEmptyString(session.accessToken) &&
    isNonEmptyString(session.refreshToken) &&
    isNonEmptyString(session.sessionId) &&
    isNonEmptyString(session.expiresAtUtc) &&
    (session.principal === undefined || isRecord(session.principal))
  ) {
    return session as ValidConsoleAuthResponse
  }

  throw new Error('Authentication service returned an invalid session.')
}

function unrefTimer(timer: ReturnType<typeof setTimeout>) {
  if (typeof timer === 'object' && timer !== null && 'unref' in timer) {
    const unref = timer.unref
    if (typeof unref === 'function') {
      unref.call(timer)
    }
  }
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}
