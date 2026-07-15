import { PiniaColada } from '@pinia/colada'
import { configureApiClient } from '@nerv-iip/api-client'
import { configureAuthenticatedApiClient } from '@nerv-iip/auth'
import { initTheme } from '@nerv-iip/ui'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import './assets/main.css'
import { resolveGatewayBaseUrl } from './api/gateway-base-url'
import { createTimeoutFetch, resolveRequestTimeoutMs } from './api/request-timeout'
import { router } from './router'
import { useAuthStore } from './stores/auth'

// Apply persisted colour mode + dynamic accent before first paint.
initTheme()

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)

const auth = useAuthStore()
configureAuthenticatedApiClient({
  auth,
  // Wrap configureApiClient so the package wiring still injects the R4 base URL.
  // Web/dev leaves VITE_NERV_IIP_API_BASE_URL empty → falls back to a relative
  // base (`/api/...`) served by the vite dev proxy. The Capacitor/APK build MUST
  // set it to the absolute BusinessGateway/PlatformGateway base URL, because the
  // WebView has no dev proxy. Passing `|| undefined` is behaviour-identical to the
  // api-client default (`options.baseUrl ?? getApiBaseUrl()`). See .env.example.
  configureApiClient: (options) =>
    configureApiClient({
      ...options,
      baseUrl: resolveGatewayBaseUrl(),
      // Every facade call flows through this fetch, so the 30s timeout + offline
      // pre-check live here once — never per page. See ./api/request-timeout.
      // VITE_NERV_IIP_REQUEST_TIMEOUT_MS is a DEV-only TEST/DEBUG override (live specs
      // inject a short ceiling instead of really waiting 30s), clamped to [100, 30000];
      // production/APK builds (DEV=false) and unset/invalid values resolve to the 30s
      // default unconditionally. See .env.example.
      fetch: createTimeoutFetch({ timeoutMs: resolveRequestTimeoutMs() }),
    }),
  loginPath: '/login',
  router,
})

app.use(PiniaColada, {
  queryOptions: {
    gcTime: 300_000,
  },
})
app.use(router)
app.mount('#app')
