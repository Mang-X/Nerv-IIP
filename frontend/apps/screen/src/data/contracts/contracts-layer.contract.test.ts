// data/contracts 是大屏三段式 seam 的类型层：mock / real 两侧实现它，页面与 store 消费它。
// 契约层反向依赖 mock 会让 L4 删除 mock 时 real 模式编译不过（#1911 / NERV-1126）。
import { readdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { describe, expect, it } from 'vitest'

const contractsDir = dirname(fileURLToPath(import.meta.url))

const sourceFiles = readdirSync(contractsDir)
  .filter((f) => f.endsWith('.ts') && !f.endsWith('.test.ts'))
  .sort()

/** `import ... from 'x'` / `export ... from 'x'` / `import('x')` 的模块说明符。 */
function moduleSpecifiers(source: string): string[] {
  const specifiers: string[] = []
  for (const m of source.matchAll(/(?:\bfrom|\bimport)\s*\(?\s*['"]([^'"]+)['"]/g)) {
    specifiers.push(m[1])
  }
  return specifiers
}

describe('data/contracts 层依赖方向', () => {
  it('枚举到契约文件（避免空集合让下面的断言恒真）', () => {
    expect(sourceFiles).toContain('launcher.ts')
    expect(sourceFiles).toContain('masterdata.ts')
    expect(sourceFiles).toContain('scope.ts')
    expect(sourceFiles.length).toBeGreaterThanOrEqual(9)
  })

  it.each(sourceFiles)('%s 不 import mock / real / fetchers 实现层', (file) => {
    const specs = moduleSpecifiers(readFileSync(join(contractsDir, file), 'utf8'))
    const offenders = specs.filter((s) => /(^|\/)(mock|real|fetchers)\//.test(s))
    expect(offenders).toEqual([])
  })

  it.each(sourceFiles)('%s 只从契约层内部取类型', (file) => {
    const specs = moduleSpecifiers(readFileSync(join(contractsDir, file), 'utf8'))
    const external = specs.filter((s) => s.startsWith('@/') && !s.startsWith('@/data/contracts/'))
    expect(external).toEqual([])
  })
})
