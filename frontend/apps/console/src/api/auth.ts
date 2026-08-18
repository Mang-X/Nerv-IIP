import * as apiClient from '@nerv-iip/api-client'
import { createConsoleAuthApi } from '@nerv-iip/auth'

export { ConsoleAuthError } from '@nerv-iip/auth'

export function formatConsoleLockoutMessage(
  lockoutUntilUtc?: string,
  locale?: string,
  timeZone?: string,
) {
  if (!lockoutUntilUtc) return '账户已锁定，请稍后重试。'

  const lockoutUntil = new Date(lockoutUntilUtc)
  if (Number.isNaN(lockoutUntil.getTime())) return '账户已锁定，请稍后重试。'

  const retryTime = new Intl.DateTimeFormat(locale, {
    hour: '2-digit',
    hourCycle: 'h23',
    minute: '2-digit',
    timeZone,
  }).format(lockoutUntil)
  return `账户已锁定，请于 ${retryTime} 后重试。`
}

export const consoleAuthApi = createConsoleAuthApi({
  client: {
    getConsolePrincipal: (options) => apiClient.getConsolePrincipal(options),
    loginConsoleUser: (options) => apiClient.loginConsoleUser(options),
    logoutConsoleSession: (options) => apiClient.logoutConsoleSession(options),
    refreshConsoleSession: (options) => apiClient.refreshConsoleSession(options),
  },
  messages: {
    accountLocked: (lockoutUntilUtc) => formatConsoleLockoutMessage(lockoutUntilUtc),
    invalidCredentialsOrExpiredSession: 'Invalid credentials or expired session.',
    loginFallback: 'Unable to connect to the authentication service.',
    principalFallback: 'Unable to load the current principal.',
    remainingAttempts: (count) => `登录失败，还可尝试 ${count} 次。`,
    refreshFallback: 'Unable to refresh the session.',
  },
})
