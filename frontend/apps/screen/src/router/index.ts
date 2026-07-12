import { sanitizeRedirectPath } from '@nerv-iip/auth'
import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { useAccessScope } from '@/access/useAccessScope'
import { IS_REAL_DATA } from '@/data/config'
import { screenForPath } from '@/data/screens'
import { useRealAuthStore } from '@/stores/realAuth'

const LOGIN_PATH = '/login'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

router.beforeEach(async (to) => {
  // 真实数据模式：先恢复会话，再做鉴权门禁（未登录 → 登录页；已登录访问登录页 → 回跳）。
  // mock 模式沿用演示登录，不拦截。
  if (IS_REAL_DATA) {
    const auth = useRealAuthStore()
    if (auth.restoreStatus === 'idle') {
      await auth.restoreSession()
    }
    if (to.path !== LOGIN_PATH && !auth.isAuthenticated) {
      return { path: LOGIN_PATH, query: { redirect: to.fullPath } }
    }
    if (to.path === LOGIN_PATH && auth.isAuthenticated) {
      return sanitizeRedirectPath(to.query.redirect, {
        defaultRedirectPath: '/',
        loginPath: LOGIN_PATH,
      })
    }
  }

  // 权限守卫：目标是某大屏、但当前 persona 不可见 → 回大屏选择页。
  // pinia 在 main.ts 中先于 router 安装，导航期 store 可用。
  const key = screenForPath(to.path)
  if (!key) return true
  const scope = useAccessScope()
  return scope.canSeeScreen(key) ? true : '/'
})

if (import.meta.hot) {
  handleHotUpdate(router)
}
