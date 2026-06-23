import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

// Lightweight document-title sync (no i18n in the standalone showcase).
router.afterEach((to) => {
  const title = to.meta.title as string | undefined
  document.title = title ? `${title} · Nerv-IIP 设计系统` : 'Nerv-IIP 设计系统'
})

if (import.meta.hot) {
  handleHotUpdate(router)
}
