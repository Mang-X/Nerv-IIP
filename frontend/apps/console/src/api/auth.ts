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
    changeConsolePassword: (options) => apiClient.changeConsolePassword(options),
    getConsolePrincipal: (options) => apiClient.getConsolePrincipal(options),
    loginConsoleUser: (options) => apiClient.loginConsoleUser(options),
    logoutConsoleSession: (options) => apiClient.logoutConsoleSession(options),
    refreshConsoleSession: (options) => apiClient.refreshConsoleSession(options),
  },
  messages: {
    changePasswordFallback: '修改密码失败，请稍后重试。',
    accountLocked: (lockoutUntilUtc) => formatConsoleLockoutMessage(lockoutUntilUtc),
    invalidCredentialsOrExpiredSession: '账号密码错误或会话已过期。',
    loginFallback: '无法连接认证服务。',
    noMembership: '账号还没有分配到任何组织或角色，请联系管理员。',
    principalFallback: '无法加载当前登录用户。',
    remainingAttempts: (count) => `登录失败，还可尝试 ${count} 次。`,
    refreshFallback: '无法刷新会话。',
  },
})
