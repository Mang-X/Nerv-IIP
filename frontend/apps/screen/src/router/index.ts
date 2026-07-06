import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { useAccessScope } from '@/access/useAccessScope'
import { screenForPath } from '@/data/screens'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

// 权限守卫：目标是某大屏、但当前 persona 不可见 → 回大屏选择页。
// pinia 在 main.ts 中先于 router 安装，导航期 store 可用。
router.beforeEach((to) => {
  const key = screenForPath(to.path)
  if (!key) return true
  const scope = useAccessScope()
  return scope.canSeeScreen(key) ? true : '/'
})

if (import.meta.hot) {
  handleHotUpdate(router)
}
