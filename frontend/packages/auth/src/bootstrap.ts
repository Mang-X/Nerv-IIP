import type { Router } from 'vue-router'
import { handleUnauthorizedRedirect, type AuthSessionController } from './router'

export interface AuthenticatedApiSession extends AuthSessionController {
  accessToken: string | undefined
  setSessionExpiredHandler: (handler?: (reason: string) => void) => void
}

export interface AuthenticatedApiClientOptions {
  accessTokenProvider: () => string | undefined
  localeProvider?: () => string | undefined
  onUnauthorized: () => void
}

export interface ConfigureAuthenticatedApiClientOptions {
  auth: AuthenticatedApiSession
  configureApiClient: (options: AuthenticatedApiClientOptions) => void
  localeProvider?: () => string | undefined
  loginPath: string
  router: Router
}

export function configureAuthenticatedApiClient(options: ConfigureAuthenticatedApiClientOptions) {
  const handleUnauthorized = () =>
    handleUnauthorizedRedirect(options.auth, options.router, {
      loginPath: options.loginPath,
    })

  options.auth.setSessionExpiredHandler(handleUnauthorized)
  options.configureApiClient({
    accessTokenProvider: () => options.auth.accessToken,
    localeProvider: options.localeProvider,
    onUnauthorized: handleUnauthorized,
  })
}
