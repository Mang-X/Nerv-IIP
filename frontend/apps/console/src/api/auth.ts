import {
  getConsolePrincipal,
  loginConsoleUser,
  logoutConsoleSession,
  refreshConsoleSession,
  type ConsoleAuthResponse,
  type ConsoleLoginRequest,
  type ConsoleLogoutRequest,
  type ConsolePrincipalResponse,
  type ConsoleRefreshRequest,
} from '@nerv-iip/api-client'

export class ConsoleAuthError extends Error {
  constructor(
    message: string,
    readonly status?: number,
  ) {
    super(message)
  }
}

function assertData<T>(
  result: { data?: T; error?: unknown; response?: Response },
  fallback: string,
): T {
  if (result.data) {
    return result.data
  }

  const status = result.response?.status
  throw new ConsoleAuthError(
    status === 401 ? 'Invalid credentials or expired session.' : fallback,
    status,
  )
}

export async function loginConsole(request: ConsoleLoginRequest): Promise<ConsoleAuthResponse> {
  return assertData(
    await loginConsoleUser({ body: request }),
    'Unable to connect to the authentication service.',
  )
}

export async function refreshConsole(request: ConsoleRefreshRequest): Promise<ConsoleAuthResponse> {
  return assertData(
    await refreshConsoleSession({ body: request }),
    'Unable to refresh the session.',
  )
}

export async function logoutConsole(
  accessToken: string,
  request: ConsoleLogoutRequest,
): Promise<void> {
  await logoutConsoleSession({
    body: request,
    headers: {
      Authorization: `Bearer ${accessToken}`,
    },
  })
}

export async function getConsoleMe(accessToken: string): Promise<ConsolePrincipalResponse> {
  return assertData(
    await getConsolePrincipal({
      headers: {
        Authorization: `Bearer ${accessToken}`,
      },
    }),
    'Unable to load the current principal.',
  )
}
