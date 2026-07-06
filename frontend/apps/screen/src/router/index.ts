import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

// 注：本期 demo 不强制登录，便于直接预览大屏（数据为 mock）。
// 接真后端时在此用 @nerv-iip/auth 的 createAuthGuard 接入 requiresAuth。

if (import.meta.hot) {
  handleHotUpdate(router)
}
