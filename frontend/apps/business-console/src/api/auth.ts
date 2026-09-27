import * as apiClient from '@nerv-iip/api-client'
import { createConsoleAuthApi } from '@nerv-iip/auth'

export { ConsoleAuthError } from '@nerv-iip/auth'

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
    invalidCredentialsOrExpiredSession: '账号密码错误或会话已过期。',
    loginFallback: '无法连接认证服务。',
    principalFallback: '无法加载当前登录用户。',
    refreshFallback: '无法刷新会话。',
  },
})
