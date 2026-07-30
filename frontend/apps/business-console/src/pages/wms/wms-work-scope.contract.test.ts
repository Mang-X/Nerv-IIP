import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

const cases = [
  { page: 'inbound.vue', catalog: 'receipts' },
  { page: 'putaway.vue', catalog: 'receipts' },
  { page: 'picking.vue', catalog: 'shipments' },
  { page: 'outbound.vue', catalog: 'shipments' },
  { page: 'counts.vue', catalog: 'counts' },
] as const

function pageSource(page: string) {
  return readFileSync(resolve(process.cwd(), 'src/pages/wms', page), 'utf8')
}

describe('WMS PC 作业范围契约', () => {
  it.each(cases)('$page 使用后端可信 $catalog 目录，不提供任意范围输入', ({ page, catalog }) => {
    const source = pageSource(page)

    expect(source).toContain(`bindWmsWorkScopeFilters(filters, '${catalog}')`)
    expect(source).toContain('workScopeRequired: true')
    expect(source).toMatch(
      /<NvSearchSelect(?=[^>]*aria-label="作业范围")(?=[^>]*v-model="scopeKey")(?=[^>]*:options="scopeOptions")[^>]*>/,
    )
    expect(source).not.toContain('v-model="filters.scopeKind"')
    expect(source).not.toContain('v-model="filters.scopeId"')
  })

  it.each(cases)('$page 切换作业范围时重置分页并展示当前真实范围', ({ page }) => {
    const source = pageSource(page)

    expect(source).toContain('() => filters.scopeKind')
    expect(source).toContain('() => filters.scopeId')
    expect(source).toContain(':scope="selectedScopeLabel ||')
    expect(source).not.toContain('暂不支持按操作员归属筛选')
    expect(source).not.toContain('当前登录组织 / 当前业务环境')
  })
})
