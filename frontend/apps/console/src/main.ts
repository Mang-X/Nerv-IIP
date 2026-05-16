import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { configureApiClient } from '@nerv-iip/api-client'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import './assets/main.css'
import { router } from './router'

configureApiClient()

const app = createApp(App)
const pinia = createPinia()

app.use(pinia)
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
app.use(router)
app.mount('#app')
