import { createPinia } from 'pinia'
import { createApp } from 'vue'
import App from './App.vue'
import './assets/main.css'
import { router } from './router'

// 大屏固定深色：强制 .dark，且不挂任何主题切换器。
// screen 层的 --sb-* token 本就独立于主题系统，这里加 .dark 只为让共享组件（Toaster 等）随之深色。
document.documentElement.classList.add('dark')

const app = createApp(App)
app.use(createPinia())
app.use(router)
app.mount('#app')

// 接真后端时（当前数据为 mock）：
//   import { configureApiClient } from '@nerv-iip/api-client'
//   import { configureAuthenticatedApiClient } from '@nerv-iip/auth'
//   在 createPinia() 之后、mount 之前，用 useAuthStore() 接 configureAuthenticatedApiClient({...})，
//   并把各 mock fetcher 换成 @nerv-iip/api-client 的 business-console 端点。
