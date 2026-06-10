import { PiniaColada } from '@pinia/colada'
import { configureApiClient } from '@nerv-iip/api-client'
import { initTheme } from '@nerv-iip/ui'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { handleUnauthorized } from './api/unauthorized'
import './assets/main.css'
import { router } from './router'
import { useAuthStore } from './stores/auth'

// Apply persisted colour mode + dynamic accent before first paint.
initTheme()

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)

const auth = useAuthStore()
auth.setSessionExpiredHandler(() => handleUnauthorized(auth, router))
configureApiClient({
  // Web/dev leaves VITE_NERV_IIP_API_BASE_URL empty → falls back to a relative
  // base (`/api/...`) served by the vite dev proxy. The Capacitor/APK build MUST
  // set it to the absolute BusinessGateway/PlatformGateway base URL, because the
  // WebView has no dev proxy. Passing `|| undefined` is behaviour-identical to the
  // api-client default (`options.baseUrl ?? getApiBaseUrl()`). See .env.example.
  baseUrl: import.meta.env.VITE_NERV_IIP_API_BASE_URL || undefined,
  accessTokenProvider: () => auth.accessToken,
  onUnauthorized: () => handleUnauthorized(auth, router),
})

app.use(PiniaColada, {
  queryOptions: {
    gcTime: 300_000,
  },
})
app.use(router)
app.mount('#app')
