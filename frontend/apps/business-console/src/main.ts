import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { configureApiClient } from '@nerv-iip/api-client'
import { configureAuthenticatedApiClient } from '@nerv-iip/auth'
import { initTheme } from '@nerv-iip/ui'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import './assets/main.css'
// DHTMLX 甘特布局/网格样式(经 vite alias 指向 vendor 试用包或空 stub);排产工作台用。
import '@dhx/trial-gantt/codebase/dhtmlxgantt.css'
import { getCurrentLocale, i18n } from './i18n'
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
  configureApiClient,
  localeProvider: () => getCurrentLocale(),
  loginPath: '/login',
  router,
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
