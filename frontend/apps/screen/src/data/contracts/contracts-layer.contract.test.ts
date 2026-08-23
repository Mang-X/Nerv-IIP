// data/contracts 是大屏三段式 seam 的类型层：mock / real 两侧实现它，页面与 store 消费它。
// 契约层反向依赖 mock 会让 L4 删除 mock 时 real 模式编译不过（#1911 / NERV-1126）。
//
// 合同来源：`frontend/apps/screen/AGENTS.md`「数据 seam（三段式）」——
// 页面只消费类型化接口，切真实数据只改 `data/fetchers/*` 与 `data/real/*`。
// 分类：Governance（依赖方向的机器可检查规则）。
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const contractsDir = dirname(fileURLToPath(import.meta.url))
/** `@/` 别名根（vite.config.ts：`@` → `apps/screen/src`）。 */
const srcDir = resolve(contractsDir, '..', '..')

// 递归枚举：契约层将来分子目录时新文件不能逃扫。
const sourceFiles = readdirSync(contractsDir, { recursive: true })
  .map((entry) => String(entry).split(sep).join('/'))
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

/** 说明符指向的绝对路径；裸包名（node:fs / @nerv-iip/*）返回 undefined。 */
function resolveSpecifier(file: string, specifier: string): string | undefined {
  if (specifier.startsWith('@/')) return join(srcDir, specifier.slice(2))
  if (specifier.startsWith('.')) return resolve(dirname(join(contractsDir, file)), specifier)
  return undefined
}

function isInsideContracts(path: string): boolean {
  return path === contractsDir || path.startsWith(contractsDir + sep)
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
    // 目录名可以是路径末段（barrel：`../mock`）也可以是中间段（`@/data/mock/scope`）。
    const offenders = specs.filter((s) => /(^|\/)(mock|real|fetchers)(\/|$)/.test(s))
    expect(offenders).toEqual([])
  })

  it.each(sourceFiles)('%s 的路径 import 不出契约层', (file) => {
    const specs = moduleSpecifiers(readFileSync(join(contractsDir, file), 'utf8'))
    // 白名单口径：`@/` 与相对路径一律解析后要求落在契约层内；裸包名不是层内 seam，不管。
    const escaped = specs.filter((s) => {
      const target = resolveSpecifier(file, s)
      return target !== undefined && !isInsideContracts(target)
    })
    expect(escaped).toEqual([])
  })
})
