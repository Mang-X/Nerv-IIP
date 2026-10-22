import { createMemoryHistory, createRouter } from 'vue-router'
import { routes } from 'vue-router/auto-routes'
import { describe, expect, it } from 'vitest'

/**
 * 交接班三条路由的**实测**（#2784）。
 *
 * 不推断：`definePage({ meta })` 只有在 meta 是静态字面量时才会被 unplugin-vue-router
 * 编进路由记录，别名/成员引用会让生成失败，而失败方向是「页面还在、meta 没了」
 * ——那时 `requiresAuth` 守卫会静默放过未登录访问。这里用真 router 解析，
 * 断言合并后的 meta 与路由参数。
 */
function resolveWith(path: string) {
  const router = createRouter({ history: createMemoryHistory(), routes })
  return router.resolve(path)
}

describe('PDA shift-handover routes', () => {
  it('resolves 交班录入 with its requiresAuth meta', () => {
    const resolved = resolveWith('/mes/handover')
    expect(resolved.matched.length).toBeGreaterThan(0)
    expect(resolved.meta.requiresAuth).toBe(true)
    expect(resolved.meta.title).toBe('交班')
  })

  it('resolves 接班列表 both with and without the trailing slash', () => {
    // 列表页是 `pages/mes/handovers/index.vue`，生成的子记录带尾斜杠；
    // 首页入口与「去交班/返回」按钮 push 的是不带尾斜杠的 `/mes/handovers`，两种都必须落到同一页。
    for (const path of ['/mes/handovers', '/mes/handovers/']) {
      const resolved = resolveWith(path)
      expect(resolved.matched.length, path).toBeGreaterThan(0)
      expect(resolved.meta.requiresAuth, path).toBe(true)
      expect(resolved.meta.title, path).toBe('接班')
    }
  })

  it('resolves 接班确认 and binds the handoverId param', () => {
    const resolved = resolveWith('/mes/handovers/HO-2026-0001')
    expect(resolved.params).toEqual({ handoverId: 'HO-2026-0001' })
    expect(resolved.meta.requiresAuth).toBe(true)
    expect(resolved.meta.title).toBe('接班确认')
  })

  it('keeps 交班录入 and 接班列表 as two distinct pages', () => {
    // 差一个 s；塌成同一条的话「交班」入口会点进接班列表。
    expect(resolveWith('/mes/handover').meta.title).not.toBe(
      resolveWith('/mes/handovers').meta.title,
    )
  })
})
