export {
  getConsolePrincipalQueryOptions,
  loginConsoleUserMutationOptions,
  logoutConsoleSessionMutationOptions,
  refreshConsoleSessionMutationOptions,
} from './generated/@pinia/colada.gen'

export {
  getConsolePrincipal,
  loginConsoleUser,
  logoutConsoleSession,
  refreshConsoleSession,
} from './generated/sdk.gen'

import type {
  NervIipPlatformGatewayWebApplicationAuthConsoleAuthResponse,
  NervIipPlatformGatewayWebApplicationAuthConsoleLoginRequest,
  NervIipPlatformGatewayWebApplicationAuthConsoleLogoutRequest,
  NervIipPlatformGatewayWebApplicationAuthConsolePrincipalResponse,
  NervIipPlatformGatewayWebApplicationAuthConsoleRefreshRequest,
} from './generated/types.gen'

export type ConsoleAuthResponse = NervIipPlatformGatewayWebApplicationAuthConsoleAuthResponse
export type ConsoleLoginRequest = NervIipPlatformGatewayWebApplicationAuthConsoleLoginRequest
export type ConsoleLogoutRequest = NervIipPlatformGatewayWebApplicationAuthConsoleLogoutRequest
export type ConsolePrincipalResponse =
  NervIipPlatformGatewayWebApplicationAuthConsolePrincipalResponse
export type ConsoleRefreshRequest = NervIipPlatformGatewayWebApplicationAuthConsoleRefreshRequest
