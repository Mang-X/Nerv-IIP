import { createHash } from 'node:crypto'
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { basename, dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * NvUI import-hygiene guard（ADR 0020 Decision 4.4 / #789 收口）的**唯一实现**（#2022）。
 *
 * 规则本体住在这里，四个 app 各留一个只调用 {@link runNvUiImportHygieneContract} 的壳
 * （`apps/<app>/src/nvui-imports.contract.test.ts`）。此前四份是字节相同的手工副本，没有
 * 任何东西保证它们继续相同：只改一两份会让门禁在某些 app 上悄悄变松而 CI 照绿。
 *
 * 组件库只通过稳定包边界消费——裸 `@nerv-iip/ui` / `@nerv-iip/ui-mobile`，用 `Nv*` 规范名。
 *
 *  1. **硬禁**（永远判红）：深导入 `@nerv-iip/ui|ui-mobile/*`（例外见
 *     {@link ALLOWED_UI_SUBPATHS} 与 {@link TEST_ONLY_UI_SUBPATHS}）、直接 `reka-ui`、
 *     直接 `shadcn-vue`。
 *  2. **收口不变量**：codemod 收口（#789）删光了库桶里的 `@deprecated` 旧名别名，旧名因此
 *     根本不可导入——是硬 typecheck 错误而不是软棘轮告警（per-app 的
 *     `nvui-legacy-imports.baseline.json` 已退役）。这里断言库暴露零个 `@deprecated` 别名，
 *     所以重新引入一个别名会在这里红。
 *  3. **副本一致性**（#2022）：断言所有该受守护的 app 都有这个壳、且四份壳字节相同。
 */

/** 运行时子入口，全 app 源码放行；见 `frontend/DESIGN/governance.md` 的「包子入口边界」。 */
const ALLOWED_UI_SUBPATHS = new Set(['file-preview'])
/**
 * test-only 子入口（#2014）：`@nerv-iip/ui/test-support` 装的是各包 vitest `setupFiles`
 * 用的支撑件（unovis tooltip 定时器收口等）与本门禁自身的实现，不是组件边界的一部分。
 * 它只在 `src/test/setup.ts` 里放行；页面 / 组件 / composable 引用它照旧判红。
 */
const TEST_ONLY_UI_SUBPATHS = new Set(['test-support'])
// 放行面刻意钉死到 setup 文件本身，而不是整个 `src/test/` 目录：这个子入口的用途只有
// 「在 vitest 环境装好之前改一次全局」，普通测试文件没有理由碰它。目录级放行会把
// 「随便哪个 test/ 下的辅助文件都能深导入」也一并放过，比这条规则想表达的宽。
const isTestSetupFile = (rel: string) => rel === 'test/setup.ts'

/** 各 app 里这个壳的固定文件名——完整性断言按它找副本。 */
export const CONTRACT_SHELL_BASENAME = 'nvui-imports.contract.test.ts'

/**
 * 判定单条 import specifier 是否违反 import hygiene。
 *
 * @param spec 源文件里写的模块字符串。
 * @param rel  该源文件相对 app `src/` 的路径（`/` 分隔）——放行面依赖它。
 * @returns 违规原因（用作断言消息），合规返回 `null`。
 */
export function nvuiImportViolation(spec: string, rel: string): string | null {
  const uiDeep = /^@nerv-iip\/ui\/(.+)$/.exec(spec)
  if (uiDeep) {
    const sub = uiDeep[1]
    const allowed =
      ALLOWED_UI_SUBPATHS.has(sub) || (isTestSetupFile(rel) && TEST_ONLY_UI_SUBPATHS.has(sub))
    if (!allowed) return `deep import "${spec}" — use the bare @nerv-iip/ui barrel`
  }
  if (/^@nerv-iip\/ui-mobile\/.+$/.test(spec)) {
    return `deep import "${spec}" — use the bare @nerv-iip/ui-mobile barrel`
  }
  if (/^reka-ui(\/|$)/.test(spec)) {
    return `direct reka-ui import "${spec}" — headless primitives live inside @nerv-iip/ui`
  }
  if (/^shadcn-vue(\/|$)/.test(spec)) {
    return `direct shadcn-vue import "${spec}" — use @nerv-iip/ui`
  }
  return null
}

export function walk(dir: string, keep: (name: string) => boolean): string[] {
  const out: string[] = []
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name === 'dist') continue
    const full = join(dir, e.name)
    if (e.isDirectory()) out.push(...walk(full, keep))
    else if (keep(e.name)) out.push(full)
  }
  return out
}

export const isSource = (n: string) =>
  /\.(vue|ts)$/.test(n) && !/\.(test|spec)\./.test(n) && !n.endsWith('.d.ts')

const MODULE_RE = /(?:from|import\(|\bimport)\s*['"]([^'"]+)['"]/g
export function modulesOf(src: string): string[] {
  const out: string[] = []
  let m: RegExpExecArray | null
  MODULE_RE.lastIndex = 0
  while ((m = MODULE_RE.exec(src))) out.push(m[1])
  return out
}

/**
 * 从库桶里的 `@deprecated` 别名反推旧名集合。#789 收口后应为空集——那就是下面断言的不变量。
 */
export function deriveDeprecated(files: string[]): Set<string> {
  const s = new Set<string>()
  const re = /@deprecated[^\n]*\n\s*(?:default|[A-Za-z0-9_]+)\s+as\s+([A-Za-z0-9_]+)/g
  for (const f of files) {
    const src = readFileSync(f, 'utf8')
    let m: RegExpExecArray | null
    re.lastIndex = 0
    while ((m = re.exec(src))) s.add(m[1])
  }
  return s
}

/**
 * 枚举「该挂这条门禁」的 app：`frontend/apps/*` 里既消费组件库、又有 vitest `test` 脚本的。
 *
 * 判据从各 app 的 `package.json` 推导而不是写死名单，这样新增 app 忘了挂壳会在这里红。
 * `apps/design-system` 消费组件库但没有测试运行器（VitePress 文档站），因此不在集合里；
 * `apps/docs` 有测试但不消费组件库，同理不在。`packages/*` 里的库间消费不由这条 app 门禁
 * 覆盖，维持既有范围（#2022 只改组织方式，不扩面）。
 */
export function appsRequiringContract(frontendRoot: string): string[] {
  const appsDir = resolve(frontendRoot, 'apps')
  return readdirSync(appsDir, { withFileTypes: true })
    .filter((e) => e.isDirectory())
    .map((e) => e.name)
    .filter((name) => {
      const pkgPath = join(appsDir, name, 'package.json')
      if (!existsSync(pkgPath)) return false
      const pkg = JSON.parse(readFileSync(pkgPath, 'utf8')) as {
        dependencies?: Record<string, string>
        devDependencies?: Record<string, string>
        scripts?: Record<string, string>
      }
      const deps = { ...pkg.dependencies, ...pkg.devDependencies }
      const consumesLibrary = '@nerv-iip/ui' in deps || '@nerv-iip/ui-mobile' in deps
      return consumesLibrary && typeof pkg.scripts?.test === 'string'
    })
    .sort()
}

/** 原始字节的 sha256——不做换行归一化，避免仅 CRLF 差异被判为「相同」。 */
function sha256(file: string): string {
  return createHash('sha256').update(readFileSync(file)).digest('hex')
}

/**
 * 挂上 NvUI import hygiene 门禁。四个 app 的壳里各调一次，传自己的 `import.meta.url`。
 *
 * @param contractFileUrl 调用方（app 的 `src/nvui-imports.contract.test.ts`）的 `import.meta.url`。
 */
export function runNvUiImportHygieneContract(contractFileUrl: string): void {
  const contractFile = fileURLToPath(contractFileUrl)
  const srcDir = dirname(contractFile)
  const frontendRoot = resolve(srcDir, '../../..')
  const appName = basename(resolve(srcDir, '..'))

  const UI_OLD = deriveDeprecated(
    walk(resolve(frontendRoot, 'packages/ui/src/components'), (n) => n === 'index.ts'),
  )
  const MOBILE_OLD = deriveDeprecated([resolve(frontendRoot, 'packages/ui-mobile/src/index.ts')])

  const files = walk(srcDir, isSource)

  describe(`NvUI import hygiene (stable package boundary) — ${appName}`, () => {
    it('found app source files to guard', () => {
      expect(files.length).toBeGreaterThan(0)
    })

    for (const file of files) {
      const rel = relative(srcDir, file).replace(/\\/g, '/')
      it(`${rel} imports NvUI only through the stable boundary`, () => {
        for (const spec of modulesOf(readFileSync(file, 'utf8'))) {
          const violation = nvuiImportViolation(spec, rel)
          expect.soft(violation, violation ?? `${spec} is fine`).toBeNull()
        }
      })
    }

    // #789 收口：库不再暴露任何 `@deprecated` 旧名别名，旧名（`NvButton`、`ScreenPanel`、
    // ui-mobile 的 `Badge` …）因此根本不可导入——旧名棘轮与 per-app baseline 已退役，
    // typecheck 是硬门。这里断言该不变量，重新加回别名会在这里红。
    it('the library exposes no @deprecated old-name aliases (closeout done)', () => {
      expect(
        [...UI_OLD, ...MOBILE_OLD].sort(),
        'closeout removed every @deprecated alias — an old-name import is now a typecheck error',
      ).toEqual([])
    })

    // #2022：四份壳曾是手工维护的字节副本。规则本体收进 `@nerv-iip/ui/test-support` 之后，
    // 这条断言守住剩下的两个漂移面——「有 app 没挂壳」和「某个 app 的壳被单独改过」。
    // 它在每个 app 的 job 里各跑一次，所以 CI 上失败归属仍然落到具体 app。
    describe('contract shell consistency across apps', () => {
      const expectedApps = appsRequiringContract(frontendRoot)
      const shellOf = (app: string) =>
        resolve(frontendRoot, 'apps', app, 'src', CONTRACT_SHELL_BASENAME)

      it('every library-consuming app with a test runner ships the contract shell', () => {
        expect(
          expectedApps,
          'no app matched the guard criteria — the derivation is broken',
        ).not.toEqual([])
        expect(expectedApps).toContain(appName)
        const missing = expectedApps.filter((app) => !existsSync(shellOf(app)))
        expect(
          missing,
          `these apps consume the component library and run vitest but have no src/${CONTRACT_SHELL_BASENAME}`,
        ).toEqual([])
      })

      it('all app contract shells are byte-identical', () => {
        const digests = Object.fromEntries(
          expectedApps
            .filter((app) => existsSync(shellOf(app)))
            .map((app) => [app, sha256(shellOf(app))]),
        )
        const mine = digests[appName]
        const drifted = Object.entries(digests)
          .filter(([, digest]) => digest !== mine)
          .map(([app]) => app)
        expect(
          drifted,
          `contract shells diverged from ${appName}'s — the shell is a fixed call into ` +
            `@nerv-iip/ui/test-support and must stay byte-identical across apps; rules belong in ` +
            `that module, not in a per-app copy. Digests: ` +
            JSON.stringify(digests, null, 2),
        ).toEqual([])
      })
    })
  })
}
