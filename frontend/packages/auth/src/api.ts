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
    readonly code?: string,
    readonly lockoutUntilUtc?: string,
    readonly remainingAttempts?: number,
  ) {
    super(message)
  }
}

export interface ConsoleAuthApiMessages {
  accountLocked?: (lockoutUntilUtc?: string) => string
  invalidCredentialsOrExpiredSession: string
  loginFallback: string
  principalFallback: string
  remainingAttempts?: (count: number) => string
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
  const failureCode = result.response?.headers.get('X-Nerv-Iam-Login-Failure') ?? undefined
  const lockoutUntilUtc = result.response?.headers.get('X-Nerv-Iam-Lockout-Until-Utc') ?? undefined
  const remainingAttemptsHeader = result.response?.headers.get(
    'X-Nerv-Iam-Remaining-Attempts',
  )
  const remainingAttempts = remainingAttemptsHeader
    ? Number.parseInt(remainingAttemptsHeader, 10)
    : undefined
  const message =
    failureCode === 'iam-account-locked' && messages.accountLocked
      ? messages.accountLocked(lockoutUntilUtc)
      : failureCode === 'iam-invalid-credentials' &&
          remainingAttempts &&
          remainingAttempts > 0 &&
          messages.remainingAttempts
        ? messages.remainingAttempts(remainingAttempts)
        : status === 401
          ? messages.invalidCredentialsOrExpiredSession
          : fallback
  throw new ConsoleAuthError(
    message,
    status,
    failureCode,
    lockoutUntilUtc,
    remainingAttempts,
  )
}
