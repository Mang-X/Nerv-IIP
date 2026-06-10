import { getConsoleMe, loginConsole, logoutConsole, refreshConsole } from '@/api/auth'
import type { ConsolePrincipalResponse } from '@nerv-iip/api-client'
import { defineStore } from 'pinia'
import { computed, shallowRef } from 'vue'

const STORAGE_KEY = 'nerv-iip.business-pda.auth'

interface StoredSession {
  refreshToken: string
  sessionId: string
  principal?: ConsolePrincipalResponse
}

export const useAuthStore = defineStore('pda-auth', () => {
  const accessToken = shallowRef<string>()
  const refreshToken = shallowRef<string>()
  const sessionId = shallowRef<string>()
  const principal = shallowRef<ConsolePrincipalResponse>()
  const restoreStatus = shallowRef<'idle' | 'restoring' | 'restored' | 'failed'>('idle')
  let sessionExpiredHandler: ((reason: string) => void) | undefined

  const isAuthenticated = computed(() => Boolean(accessToken.value && principal.value))
  const displayName = computed(() => principal.value?.loginName ?? '未知用户')

  function persist() {
    if (refreshToken.value && sessionId.value) {
      localStorage.setItem(
        STORAGE_KEY,
        JSON.stringify({
          refreshToken: refreshToken.value,
          sessionId: sessionId.value,
          principal: principal.value,
        } satisfies StoredSession),
      )
    }
  }

  function clearSession(_reason: string) {
    accessToken.value = undefined
    refreshToken.value = undefined
    sessionId.value = undefined
    principal.value = undefined
    localStorage.removeItem(STORAGE_KEY)
  }

  async function login(loginName: string, password: string) {
    const session = await loginConsole({ loginName, password })
    accessToken.value = session.accessToken ?? undefined
    refreshToken.value = session.refreshToken ?? undefined
    sessionId.value = session.sessionId ?? undefined
    principal.value = session.principal ?? undefined
    persist()
  }

  async function restoreSession() {
    restoreStatus.value = 'restoring'
    const raw = localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      restoreStatus.value = 'failed'
      return
    }
    try {
      const stored = JSON.parse(raw) as StoredSession
      const session = await refreshConsole({ refreshToken: stored.refreshToken })
      accessToken.value = session.accessToken ?? undefined
      refreshToken.value = session.refreshToken ?? undefined
      sessionId.value = session.sessionId ?? stored.sessionId
      principal.value =
        session.principal ?? (accessToken.value ? await getConsoleMe(accessToken.value) : undefined)
      persist()
      restoreStatus.value = 'restored'
    } catch {
      clearSession('restore-failed')
      restoreStatus.value = 'failed'
    }
  }

  async function logout() {
    if (accessToken.value && sessionId.value) {
      try {
        await logoutConsole(accessToken.value, { sessionId: sessionId.value })
      } catch {
        // 忽略登出网络错误，本地仍清会话
      }
    }
    clearSession('logout')
  }

  function setSessionExpiredHandler(handler: (reason: string) => void) {
    sessionExpiredHandler = handler
  }

  return {
    accessToken,
    principal,
    restoreStatus,
    isAuthenticated,
    displayName,
    login,
    logout,
    restoreSession,
    clearSession,
    setSessionExpiredHandler,
  }
})
