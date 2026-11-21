import { installDocumentTitleSync } from '@nerv-iip/app-shell'
import { translate } from '@/i18n'
import { createRouter, createWebHistory } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { installAuthGuard } from './guards/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes,
})

installAuthGuard(router)
installDocumentTitleSync(router, translate)

if (import.meta.hot) {
  handleHotUpdate(router)
}
