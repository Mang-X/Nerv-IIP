import type { Router } from 'vue-router'

const DEFAULT_DOCUMENT_TITLE_KEY = 'app.title'

export function installDocumentTitleSync(router: Router, translate: (key: string) => string) {
  router.afterEach((to) => {
    document.title = translate(
      typeof to.meta.title === 'string' ? to.meta.title : DEFAULT_DOCUMENT_TITLE_KEY,
    )
  })
}
