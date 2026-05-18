import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { installAuthGuard } from './guards/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

installAuthGuard(router)

if (import.meta.hot) {
  handleHotUpdate(router)
}
