import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it } from 'vitest'
import { installDocumentTitleSync } from './document-title'

describe('document title sync', () => {
  beforeEach(() => {
    document.title = ''
  })

  function createTitleRouter() {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: '/', component: { template: '<div />' }, meta: { title: 'routes.instances' } },
        { path: '/untitled', component: { template: '<div />' } },
      ],
    })
    installDocumentTitleSync(router)
    return router
  }

  it('sets document title from route meta title after navigation', async () => {
    const router = createTitleRouter()

    await router.push('/')

    expect(document.title).toBe('实例')
  })

  it('falls back to the console title when route meta title is missing', async () => {
    const router = createTitleRouter()

    await router.push('/untitled')

    expect(document.title).toBe('Nerv-IIP 控制台')
  })
})
