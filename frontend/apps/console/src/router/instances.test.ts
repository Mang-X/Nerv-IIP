import { describe, expect, it } from 'vitest'
import { createMemoryHistory, createRouter } from 'vue-router'
import { routes } from 'vue-router/auto-routes'

describe('实例列表路由', () => {
  it('直开 /instances 使用首页实例列表及其鉴权配置', () => {
    const router = createRouter({ history: createMemoryHistory(), routes })
    const home = router.resolve('/')
    const instances = router.resolve('/instances')

    expect(instances.matched.at(-1)?.aliasOf).toBe(home.matched.at(-1))
    expect(instances.meta.requiresAuth).toBe(true)
  })
})
