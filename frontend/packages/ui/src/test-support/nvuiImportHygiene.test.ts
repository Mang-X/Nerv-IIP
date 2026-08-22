import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  appsRequiringContract,
  CONTRACT_SHELL_BASENAME,
  isSource,
  modulesOf,
  nvuiImportViolation,
} from './nvuiImportHygiene'

const frontendRoot = resolve(fileURLToPath(import.meta.url), '../../../../..')

/**
 * 规则本体的阳性/阴性对照（#2022）。
 *
 * 四个 app 的 contract 壳只是把这套规则套到自己的 `src/` 上；规则本身的鉴别力在这里证明，
 * 不必在每个 app 里各造一遍脏源文件。
 */
describe('nvuiImportViolation', () => {
  const SETUP = 'test/setup.ts'
  const PAGE = 'pages/orders/index.vue'

  it('bans deep imports into @nerv-iip/ui', () => {
    expect(nvuiImportViolation('@nerv-iip/ui/src/components/ui/button', PAGE)).toMatch(
      /deep import .* bare @nerv-iip\/ui barrel/,
    )
  })

  it('bans deep imports into @nerv-iip/ui-mobile', () => {
    expect(
      nvuiImportViolation('@nerv-iip/ui-mobile/src/components/NvMobileDialog.vue', PAGE),
    ).toMatch(/deep import .* bare @nerv-iip\/ui-mobile barrel/)
  })

  it('bans direct reka-ui, bare and deep', () => {
    expect(nvuiImportViolation('reka-ui', PAGE)).toMatch(/direct reka-ui import/)
    expect(nvuiImportViolation('reka-ui/namespaced', PAGE)).toMatch(/direct reka-ui import/)
  })

  it('bans direct shadcn-vue, bare and deep', () => {
    expect(nvuiImportViolation('shadcn-vue', PAGE)).toMatch(/direct shadcn-vue import/)
    expect(nvuiImportViolation('shadcn-vue/registry', PAGE)).toMatch(/direct shadcn-vue import/)
  })

  it('allows the bare barrels and unrelated packages', () => {
    for (const spec of ['@nerv-iip/ui', '@nerv-iip/ui-mobile', 'vue', '@/components/Foo.vue']) {
      expect(nvuiImportViolation(spec, PAGE), spec).toBeNull()
    }
  })

  it('allows the file-preview runtime sub-entry everywhere', () => {
    expect(nvuiImportViolation('@nerv-iip/ui/file-preview', PAGE)).toBeNull()
    expect(nvuiImportViolation('@nerv-iip/ui/file-preview', SETUP)).toBeNull()
  })

  it('allows test-support only from src/test/setup.ts', () => {
    expect(nvuiImportViolation('@nerv-iip/ui/test-support', SETUP)).toBeNull()
    // 同目录下的普通测试辅助文件、以及页面/组件，一律判红——放行面钉死到 setup 文件本身。
    for (const rel of [PAGE, 'test/helpers.ts', 'test/setup.extra.ts', 'components/Nv.vue']) {
      expect(nvuiImportViolation('@nerv-iip/ui/test-support', rel), rel).toMatch(/deep import/)
    }
  })

  it('does not let an unknown sub-entry through on either barrel', () => {
    expect(nvuiImportViolation('@nerv-iip/ui/test-supportx', SETUP)).toMatch(/deep import/)
    expect(nvuiImportViolation('@nerv-iip/ui-mobile/file-preview', PAGE)).toMatch(/deep import/)
  })
})

describe('modulesOf', () => {
  it('picks up static, type-only, side-effect and dynamic imports', () => {
    const src = [
      "import Foo from 'reka-ui'",
      "import type { Bar } from '@nerv-iip/ui'",
      "import 'shadcn-vue'",
      "export { x } from '@nerv-iip/ui/file-preview'",
      "const m = await import('@nerv-iip/ui-mobile/deep')",
    ].join('\n')
    expect(modulesOf(src)).toEqual([
      'reka-ui',
      '@nerv-iip/ui',
      'shadcn-vue',
      '@nerv-iip/ui/file-preview',
      '@nerv-iip/ui-mobile/deep',
    ])
  })
})

describe('isSource', () => {
  it('guards .vue/.ts app sources and skips tests and declarations', () => {
    expect(isSource('Page.vue')).toBe(true)
    expect(isSource('useThing.ts')).toBe(true)
    expect(isSource('useThing.test.ts')).toBe(false)
    expect(isSource('useThing.spec.ts')).toBe(false)
    expect(isSource('env.d.ts')).toBe(false)
    expect(isSource('README.md')).toBe(false)
  })
})

describe('appsRequiringContract', () => {
  it('derives exactly the library-consuming apps that run vitest', () => {
    expect(appsRequiringContract(frontendRoot)).toEqual([
      'business-console',
      'business-pda',
      'console',
      'screen',
    ])
  })

  it('each derived app ships the shell and the shells agree byte-for-byte', () => {
    const contents = appsRequiringContract(frontendRoot).map((app) => {
      const shell = resolve(frontendRoot, 'apps', app, 'src', CONTRACT_SHELL_BASENAME)
      expect(existsSync(shell), `${app} has no src/${CONTRACT_SHELL_BASENAME}`).toBe(true)
      return readFileSync(shell).toString('base64')
    })
    expect(new Set(contents).size, 'app contract shells diverged').toBe(1)
  })
})
