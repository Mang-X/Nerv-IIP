import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { configureApiClient } from '@nerv-iip/api-client'
import { initTheme } from '@nerv-iip/ui'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { handleUnauthorized } from './api/unauthorized'
import './assets/main.css'
import { getCurrentLocale, i18n } from './i18n'
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
  accessTokenProvider: () => auth.accessToken,
  localeProvider: () => getCurrentLocale(),
  onUnauthorized: () => handleUnauthorized(auth, router),
})

app.use(PiniaColada, {
  queryOptions: {
    gcTime: 300_000,
  },
  plugins: [
    PiniaColadaAutoRefetch({
      autoRefetch: false,
    }),
  ],
})
app.use(i18n)
app.use(router)
app.mount('#app')
