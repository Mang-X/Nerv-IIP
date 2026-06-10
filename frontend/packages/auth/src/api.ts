import type {
  ConsoleAuthEnvelope,
  ConsoleAuthResponse,
  ConsoleLoginRequest,
  ConsoleLogoutRequest,
  ConsolePrincipalEnvelope,
  ConsolePrincipalResponse,
  ConsoleRefreshRequest,
} from '@nerv-iip/api-client'

export class ConsoleAuthError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
  }
}

export interface ConsoleAuthApiMessages {
  invalidCredentialsOrExpiredSession: string
  loginFallback: string
  principalFallback: string
  refreshFallback: string
}

export interface ConsoleAuthOperationClient {
  getConsolePrincipal: (options: {
    headers: { Authorization: string }
  }) => Promise<{ data?: ConsolePrincipalEnvelope; response?: Response }>
  loginConsoleUser: (options: {
    body: ConsoleLoginRequest
  }) => Promise<{ data?: ConsoleAuthEnvelope; response?: Response }>
  logoutConsoleSession: (options: {
    body: ConsoleLogoutRequest
    headers: { Authorization: string }
  }) => Promise<unknown>
  refreshConsoleSession: (options: {
    body: ConsoleRefreshRequest
  }) => Promise<{ data?: ConsoleAuthEnvelope; response?: Response }>
}

export interface ConsoleAuthApi {
  getConsoleMe: (accessToken: string) => Promise<ConsolePrincipalResponse>
  loginConsole: (request: ConsoleLoginRequest) => Promise<ConsoleAuthResponse>
  logoutConsole: (accessToken: string, request: ConsoleLogoutRequest) => Promise<void>
  refreshConsole: (request: ConsoleRefreshRequest) => Promise<ConsoleAuthResponse>
}

export interface CreateConsoleAuthApiOptions {
  client: ConsoleAuthOperationClient
  messages: ConsoleAuthApiMessages
}

export function createConsoleAuthApi(options: CreateConsoleAuthApiOptions): ConsoleAuthApi {
  const { client, messages } = options

  return {
    async getConsoleMe(accessToken) {
      return assertData(
        await client.getConsolePrincipal({
          headers: {
            Authorization: `Bearer ${accessToken}`,
          },
        }),
        messages.principalFallback,
        messages,
      )
    },
    async loginConsole(request) {
      return assertData(
        await client.loginConsoleUser({ body: request }),
        messages.loginFallback,
        messages,
      )
    },
    async logoutConsole(accessToken, request) {
      await client.logoutConsoleSession({
        body: request,
        headers: {
          Authorization: `Bearer ${accessToken}`,
        },
      })
    },
    async refreshConsole(request) {
      return assertData(
        await client.refreshConsoleSession({ body: request }),
        messages.refreshFallback,
        messages,
      )
    },
  }
}

function assertData<T>(
  result: {
    data?: { data?: T | null; success?: boolean; message?: string | null }
    response?: Response
  },
  fallback: string,
  messages: ConsoleAuthApiMessages,
): T {
  if (result.data?.success && result.data.data) {
    return result.data.data
  }

  const status = result.response?.status
  throw new ConsoleAuthError(
    status === 401 ? messages.invalidCredentialsOrExpiredSession : fallback,
    status,
  )
}
