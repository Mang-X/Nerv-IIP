import './assets/main.css'

import { configureApiClient } from '@nerv-iip/api-client'
import { configureAuthenticatedApiClient } from '@nerv-iip/auth'
import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import { IS_REAL_DATA } from './data/config'
import { router } from './router'
import { useRealAuthStore } from './stores/realAuth'

// 大屏固定深色：强制 .dark，且不挂任何主题切换器。
// screen 层的 --nv-scr-* token 本就独立于主题系统，这里加 .dark 只为让共享组件（Toaster 等）随之深色。
document.documentElement.classList.add('dark')

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)

if (IS_REAL_DATA) {
  const auth = useRealAuthStore()
  configureAuthenticatedApiClient({
    auth,
    configureApiClient,
    loginPath: '/login',
    router,
  })
}

app.use(router)
app.mount('#app')
