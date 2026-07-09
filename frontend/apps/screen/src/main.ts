import { configureApiClient } from '@nerv-iip/api-client'
import { configureAuthenticatedApiClient } from '@nerv-iip/auth'
import { createPinia } from 'pinia'
import { createApp, watch } from 'vue'
import App from './App.vue'
import './assets/main.css'
import { IS_REAL_DATA } from './data/config'
import { setScreenSession } from './data/session'
import { router } from './router'
import { useRealAuthStore } from './stores/realAuth'

// 大屏固定深色：强制 .dark，且不挂任何主题切换器。
// screen 层的 --sb-* token 本就独立于主题系统，这里加 .dark 只为让共享组件（Toaster 等）随之深色。
document.documentElement.classList.add('dark')

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)

// 真实数据模式：接 @nerv-iip/auth 会话到 business-console facade（token 注入 + 401 跳登录），
// 并把 principal 的 org/env 注入大屏取数上下文。mock 模式保持原样（不触碰鉴权链路）。
if (IS_REAL_DATA) {
  const auth = useRealAuthStore()
  configureAuthenticatedApiClient({
    auth,
    configureApiClient,
    loginPath: '/login',
    router,
  })
  watch(
    () => auth.principal,
    (principal) =>
      setScreenSession({
        organizationId: principal?.organizationId ?? '',
        environmentId: principal?.environmentId ?? '',
      }),
    { immediate: true },
  )
}

app.use(router)
app.mount('#app')

// mock 模式：数据全部由 data/mock 提供，无需配置 api-client。
// 切真实数据：设置 VITE_SCREEN_DATA_MODE=real（fetcher 自动改走 data/real/*）。
