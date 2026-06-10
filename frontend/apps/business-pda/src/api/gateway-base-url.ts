/**
 * Resolve the absolute gateway base URL for the api-client transport.
 *
 * Web/dev leaves `VITE_NERV_IIP_API_BASE_URL` empty → returns `undefined` so the
 * api-client falls back to its relative default (`/api/...`) served by the vite dev
 * proxy. The Capacitor/APK build runs inside a WebView with NO dev proxy, so it MUST
 * set this to the absolute BusinessGateway/PlatformGateway base URL; otherwise auth
 * requests would target the relative WebView origin instead of the real gateway.
 *
 * Empty string is treated as unset (`undefined`) — behaviour-identical to the
 * api-client default (`options.baseUrl ?? getApiBaseUrl()`).
 */
export function resolveGatewayBaseUrl(
  env: ImportMetaEnv = import.meta.env,
): string | undefined {
  return env.VITE_NERV_IIP_API_BASE_URL || undefined
}
