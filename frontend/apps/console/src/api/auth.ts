import * as apiClient from '@nerv-iip/api-client'
import { createConsoleAuthApi } from '@nerv-iip/auth'

export { ConsoleAuthError } from '@nerv-iip/auth'

export const consoleAuthApi = createConsoleAuthApi({
  client: {
    getConsolePrincipal: (options) => apiClient.getConsolePrincipal(options),
    loginConsoleUser: (options) => apiClient.loginConsoleUser(options),
    logoutConsoleSession: (options) => apiClient.logoutConsoleSession(options),
    refreshConsoleSession: (options) => apiClient.refreshConsoleSession(options),
  },
  messages: {
    invalidCredentialsOrExpiredSession: 'Invalid credentials or expired session.',
    loginFallback: 'Unable to connect to the authentication service.',
    principalFallback: 'Unable to load the current principal.',
    refreshFallback: 'Unable to refresh the session.',
  },
})

export const { getConsoleMe, loginConsole, logoutConsole, refreshConsole } = consoleAuthApi
