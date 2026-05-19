import {
  getConsolePrincipal,
  loginConsoleUser,
  logoutConsoleSession,
  refreshConsoleSession,
  type ConsoleAuthEnvelope,
  type ConsoleAuthResponse,
  type ConsolePrincipalEnvelope,
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
  result: { data?: { data?: T | null; success?: boolean; message?: string | null }; error?: unknown; response?: Response },
  fallback: string,
): T {
  if (result.data?.success && result.data.data) {
    return result.data.data
  }

  const status = result.response?.status
  throw new ConsoleAuthError(
    status === 401 ? 'Invalid credentials or expired session.' : fallback,
    status,
  )
}

export async function loginConsole(request: ConsoleLoginRequest): Promise<ConsoleAuthResponse> {
  return assertData(
    await loginConsoleUser({ body: request }) as { data?: ConsoleAuthEnvelope; response?: Response },
    'Unable to connect to the authentication service.',
  )
}

export async function refreshConsole(request: ConsoleRefreshRequest): Promise<ConsoleAuthResponse> {
  return assertData(
    await refreshConsoleSession({ body: request }) as { data?: ConsoleAuthEnvelope; response?: Response },
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
    }) as { data?: ConsolePrincipalEnvelope; response?: Response },
    'Unable to load the current principal.',
  )
}
