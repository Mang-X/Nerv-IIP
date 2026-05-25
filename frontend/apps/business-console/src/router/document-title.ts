import type { Router } from 'vue-router'
import { translate } from '@/i18n'

export const DEFAULT_DOCUMENT_TITLE_KEY = 'app.title'

export function installDocumentTitleSync(router: Router) {
  router.afterEach((to) => {
    document.title = translate(
      typeof to.meta.title === 'string' ? to.meta.title : DEFAULT_DOCUMENT_TITLE_KEY,
    )
  })
}
