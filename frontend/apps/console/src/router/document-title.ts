import type { Router } from 'vue-router'

export const DEFAULT_DOCUMENT_TITLE = 'Nerv-IIP Console'

export function installDocumentTitleSync(router: Router) {
  router.afterEach((to) => {
    document.title = typeof to.meta.title === 'string' ? to.meta.title : DEFAULT_DOCUMENT_TITLE
  })
}
