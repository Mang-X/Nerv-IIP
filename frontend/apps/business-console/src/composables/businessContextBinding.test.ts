import { describe, expect, it } from 'vitest'
import { hasBusinessContext, withBusinessContextEnabled } from './businessContextBinding'

/**
 * `hasBusinessContext` 是全站「业务范围选没选」的**唯一判定**：它同时决定查询发不发
 * （`withBusinessContextEnabled`）和表格走不走「尚未发起查询」态（`NvDataTable` 的
 * `awaitingScope`）。所以它必须对残缺入参给出答案而不是抛异常——一旦它会抛，页面就会
 * 各写各的内联判断绕开它，两个口径分叉之后「还没查」和「真的 0 条」又会重新混在一起。
 */
describe('hasBusinessContext', () => {
  it('业务上下文齐全时判定为就绪', () => {
    expect(hasBusinessContext({ organizationId: 'org-001', environmentId: 'env-dev' })).toBe(true)
  })

  it('缺字段 / 空串 / 纯空白一律判定为未就绪，且不抛异常', () => {
    const incomplete = [
      {},
      { organizationId: 'org-001' },
      { environmentId: 'env-dev' },
      { organizationId: '', environmentId: 'env-dev' },
      { organizationId: 'org-001', environmentId: '' },
      { organizationId: '   ', environmentId: 'env-dev' },
      { organizationId: undefined, environmentId: undefined },
    ]
    for (const filters of incomplete) {
      expect(() =>
        hasBusinessContext(filters as unknown as Parameters<typeof hasBusinessContext>[0]),
      ).not.toThrow()
      expect(
        hasBusinessContext(filters as unknown as Parameters<typeof hasBusinessContext>[0]),
      ).toBe(false)
    }
  })

  it('filters 本身为 null / undefined 时返回 false 而不是抛错', () => {
    expect(hasBusinessContext(null)).toBe(false)
    expect(hasBusinessContext(undefined)).toBe(false)
  })

  it('未就绪时查询保持禁用', () => {
    expect(
      withBusinessContextEnabled(
        { queryKey: ['x'] },
        {
          organizationId: '',
          environmentId: '',
        },
      ).enabled,
    ).toBe(false)
    expect(
      withBusinessContextEnabled(
        { queryKey: ['x'] },
        {
          organizationId: 'org-001',
          environmentId: 'env-dev',
        },
      ).enabled,
    ).toBe(true)
  })
})
