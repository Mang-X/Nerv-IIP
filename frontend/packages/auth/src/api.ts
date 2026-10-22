import type {
  ConsoleAuthEnvelope,
  ConsoleAuthResponse,
  ConsoleChangePasswordRequest,
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
  changePasswordFallback: string
  invalidCredentialsOrExpiredSession: string
  loginFallback: string
  /** 密码正确，但账号没有分配到任何组织或角色（IAM 只在密码校验通过后才给出这个区分）。 */
  noMembership: string
  principalFallback: string
  remainingAttempts?: (count: number) => string
  refreshFallback: string
}

export interface ConsoleAuthOperationClient {
  changeConsolePassword: (options: {
    body: ConsoleChangePasswordRequest
    headers: { Authorization: string }
  }) => Promise<{ error?: unknown; response?: Response }>
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
  changeConsolePassword: (
    accessToken: string,
    request: ConsoleChangePasswordRequest,
  ) => Promise<void>
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
    async changeConsolePassword(accessToken, request) {
      const { error, response } = await client.changeConsolePassword({
        body: request,
        headers: {
          Authorization: `Bearer ${accessToken}`,
        },
      })
      if (response?.ok) {
        return
      }

      const status = response?.status
      const message =
        status === 400
          ? (describePasswordChangeRejection(readErrorMessage(error)) ??
            messages.changePasswordFallback)
          : status === 401
            ? messages.invalidCredentialsOrExpiredSession
            : messages.changePasswordFallback
      throw new ConsoleAuthError(message, status)
    },
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
  const remainingAttemptsHeader = result.response?.headers.get('X-Nerv-Iam-Remaining-Attempts')
  const remainingAttempts = remainingAttemptsHeader
    ? Number.parseInt(remainingAttemptsHeader, 10)
    : undefined
  const message =
    failureCode === 'iam-no-membership'
      ? messages.noMembership
      : failureCode === 'iam-account-locked' && messages.accountLocked
        ? messages.accountLocked(lockoutUntilUtc)
        : failureCode === 'iam-invalid-credentials' &&
            remainingAttempts &&
            remainingAttempts > 0 &&
            messages.remainingAttempts
          ? messages.remainingAttempts(remainingAttempts)
          : status === 401
            ? messages.invalidCredentialsOrExpiredSession
            : fallback
  throw new ConsoleAuthError(message, status, failureCode, lockoutUntilUtc, remainingAttempts)
}

// IAM 以英文 KnownException 文案返回改密拒绝原因（经网关按 400 原样透传，见 #3862）。
// 已知文案译成中文；未知文案原样展示——它是 IAM 批准公开的安全业务文案。
const PASSWORD_CHANGE_REJECTIONS: ReadonlyArray<
  readonly [RegExp, (match: RegExpMatchArray) => string]
> = [
  [/^Current password is invalid\.$/, () => '当前密码不正确。'],
  [/^Password was recently used\.$/, () => '新密码不能与最近使用过的密码相同。'],
  [/^Password is required\.$/, () => '请输入新密码。'],
  [
    /^Password must be at least (\d+) characters\.$/,
    (match) => `新密码至少需要 ${match[1]} 个字符。`,
  ],
  [/^Password must include an uppercase letter\.$/, () => '新密码需包含大写字母。'],
  [/^Password must include a lowercase letter\.$/, () => '新密码需包含小写字母。'],
  [/^Password must include a digit\.$/, () => '新密码需包含数字。'],
  [/^Password must include a non-alphanumeric character\.$/, () => '新密码需包含符号。'],
]

function describePasswordChangeRejection(message: string | undefined): string | undefined {
  if (!message) {
    return undefined
  }

  return describeIamPasswordRejection(message) ?? message
}

/**
 * IAM 口令策略的英文拒绝文案译成中文；不认识的返回 undefined，由调用方决定兜底。
 * 控制台管理员新建用户、重置密码时撞口令策略，也走这张表。
 */
export function describeIamPasswordRejection(message: string): string | undefined {
  for (const [pattern, describe] of PASSWORD_CHANGE_REJECTIONS) {
    const match = message.match(pattern)
    if (match) {
      return describe(match)
    }
  }

  return undefined
}

function readErrorMessage(error: unknown): string | undefined {
  if (typeof error === 'object' && error !== null && 'message' in error) {
    const { message } = error
    return typeof message === 'string' ? message : undefined
  }

  return undefined
}
