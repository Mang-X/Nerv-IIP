import { describe, expect, test } from 'vitest'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

const appRoot = process.cwd()
const docsRoot = join(appRoot, 'docs')
const workspaceRoot = join(appRoot, '..', '..')
const internalGapRoot = join(docsRoot, 'internal', 'gaps')
const businessConsolePagesRoot = join(workspaceRoot, 'apps', 'business-console', 'src', 'pages')

const requiredGuideSections = [
  '适用角色',
  '前置资料',
  '页面入口',
  '操作步骤',
  '业务对象/单据流',
  '状态变化',
  '结果校验',
  '常见失败/空态',
  '当前限制',
]

const requiredGapSections = ['能力缺失', '操作不连贯', '手填 ID', '术语不清', '反馈不足']

const developmentOnlyWordingPatterns = [/\bdemo\w*\b/i, /\bseed\w*\b/i, /\bmock\w*\b/i]

const docsAppRoutePrefixes = [
  '/getting-started',
  '/internal',
  '/processes',
  '/roles',
  '/how-to',
  '/explanation',
  '/reference',
]

const roleMapSlugs = [
  'planner',
  'team-leader',
  'warehouse',
  'quality-inspector',
  'equipment-engineer',
  'procurement-finance',
]

const quadrantIndexPages = [
  'roles/index.md',
  'how-to/index.md',
  'explanation/index.md',
  'reference/index.md',
]
const nonBusinessConsoleRoutePrefixes = ['/api']

function readDocsFile(relativePath: string) {
  return readFileSync(join(docsRoot, relativePath), 'utf8')
}

function listMarkdownFiles(relativePath: string) {
  const absolutePath = join(docsRoot, relativePath)

  return readdirSync(absolutePath, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
    .map((entry) => join(entry.parentPath, entry.name))
}

function pagePathToRoute(pagePath: string) {
  const routeSegments = pagePath
    .replace(/\\/g, '/')
    .replace(/\.vue$/, '')
    .split('/')
    .filter((segment) => segment !== 'index')
    .map((segment) => {
      if (segment.startsWith('[...') && segment.endsWith(']')) {
        return `:${segment.slice(4, -1)}(.*)`
      }

      if (segment.startsWith('[') && segment.endsWith(']')) {
        return `:${segment.slice(1, -1)}`
      }

      return segment
    })

  return routeSegments.length === 0 ? '/' : `/${routeSegments.join('/')}`
}

function listBusinessConsoleRoutes() {
  const pageFiles = readdirSync(businessConsolePagesRoot, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .filter((entry) => entry.name.endsWith('.vue'))
    .filter((entry) => !entry.parentPath.includes(`${join('components')}`))
    .filter((entry) => !entry.parentPath.includes(`${join('dialogs')}`))
    .filter((entry) => !entry.parentPath.includes(`${join('drawers')}`))
    .filter((entry) => !entry.parentPath.includes(`${join('fragments')}`))
    .map((entry) => {
      const absolutePath = join(entry.parentPath, entry.name)
      const relativePath = absolutePath.slice(businessConsolePagesRoot.length + 1)

      return pagePathToRoute(relativePath)
    })

  return new Set(pageFiles)
}

function shouldValidateBusinessConsoleRoute(route: string) {
  if (docsAppRoutePrefixes.some((prefix) => route === prefix || route.startsWith(`${prefix}/`))) {
    return false
  }

  if (
    nonBusinessConsoleRoutePrefixes.some(
      (prefix) => route === prefix || route.startsWith(`${prefix}/`),
    )
  ) {
    return false
  }

  return true
}

function extractBusinessConsoleRouteTokens(content: string) {
  return Array.from(content.matchAll(/`(\/[^`\s]*)`/g), (match) => match[1]).filter(
    shouldValidateBusinessConsoleRoute,
  )
}

const businessConsoleRoutes = listBusinessConsoleRoutes()

describe('product docs app contract', () => {
  test('blocks common development-only wording variants in public docs', () => {
    const forbiddenVariants = [
      'demo',
      'demos',
      'demoed',
      'seed',
      'seeds',
      'seeded',
      'seeding',
      'mock',
      'mocks',
      'mocked',
      'mocking',
      'mockup',
    ]

    for (const variant of forbiddenVariants) {
      expect(
        developmentOnlyWordingPatterns.some((pattern) => pattern.test(variant)),
        `${variant} should be blocked from public docs`,
      ).toBe(true)
    }
  })

  test('publishes at least three complete end-to-end getting-started paths', () => {
    const guideFiles = listMarkdownFiles('getting-started')

    expect(guideFiles.length).toBeGreaterThanOrEqual(3)

    for (const file of guideFiles) {
      const content = readFileSync(file, 'utf8')

      for (const section of requiredGuideSections) {
        expect(content, `${file} should include ${section}`).toContain(`## ${section}`)
      }

      expect(content, `${file} should link an internal gap record`).toMatch(
        /\[内部缺口记录\]\(\/internal\/gaps\/[^)]+\)/,
      )
    }
  })

  test('documents the six required core process diagrams', () => {
    const processContent = readDocsFile('processes/index.md')
    const requiredDiagrams = [
      '工程资料：EBOM -> MBOM -> 工艺路线 -> 生产版本',
      '计划生产：需求 -> MRP -> APS -> 生产计划 -> 工单 -> 报工 -> 入库',
      '仓储库存：收货 -> 上架 -> 库存 -> 拣货 -> 出库',
      '质量审批：检验 -> NCR -> 审批 -> 处置 -> 放行/返工/报废',
      '设备维护：报警 -> 维修工单 -> 备件出库 -> 恢复 -> 可靠性指标',
      '条码追溯：条码规则 -> 标签打印 -> 扫码 -> 追溯',
    ]

    for (const diagram of requiredDiagrams) {
      expect(processContent).toContain(diagram)
    }

    expect(processContent.match(/```mermaid/g)?.length ?? 0).toBeGreaterThanOrEqual(6)
    expect(processContent).toContain('BusinessGateway facade')
    expect(processContent).toContain('当前缺口')
  })

  test('publishes the role-oriented first-week path map with availability markers', () => {
    for (const slug of roleMapSlugs) {
      const content = readDocsFile(join('roles', `${slug}.md`))

      expect(content, `roles/${slug}.md should list first-week paths`).toContain('## 第一周路径')
      expect(content, `roles/${slug}.md should mark path availability`).toMatch(
        /✅ 可用|🟡 部分可用|⛔ 缺口/,
      )
    }
  })

  // Guards LINK FORMAT only: every 🟡/⛔ gap row must carry a tracking issue link.
  // It intentionally does NOT assert the issue still exists or is open. The
  // "references a real open issue" guarantee in ADR 0020 is maintained by that
  // ADR's quarterly gap recycle (a process), not by this test. Asserting open-state
  // here would need a network call (non-hermetic, rate-limited CI); a static
  // allowlist would re-encode the same numbers without proving liveness either.
  test('gap rows in role path maps carry a tracking issue link (format only, not open-state)', () => {
    for (const slug of roleMapSlugs) {
      const content = readDocsFile(join('roles', `${slug}.md`))

      const gapRows = content
        .split('\n')
        .filter((line) => line.startsWith('|') && /🟡|⛔/.test(line))

      for (const row of gapRows) {
        expect(
          row,
          `roles/${slug}.md gap rows should link a tracking GitHub issue by URL format`,
        ).toMatch(/https:\/\/github\.com\/Mang-X\/Nerv-IIP\/issues\/\d+/)
      }
    }
  })

  test('keeps the four-quadrant navigation index pages in place', () => {
    for (const page of quadrantIndexPages) {
      expect(existsSync(join(docsRoot, page)), `${page} should exist`).toBe(true)
    }
  })

  test('keeps internal gap evidence out of public guide copy', () => {
    expect(existsSync(internalGapRoot)).toBe(true)

    const gapFiles = listMarkdownFiles('internal/gaps')

    expect(gapFiles.length).toBeGreaterThanOrEqual(3)

    for (const file of gapFiles) {
      const content = readFileSync(file, 'utf8')

      expect(content).toContain('## 证据页面')
      expect(content).toContain('## 建议 issue 标题')

      for (const section of requiredGapSections) {
        expect(content, `${file} should include ${section}`).toContain(`### ${section}`)
      }
    }

    const publicFiles = listMarkdownFiles('.').filter(
      (file) => !file.includes(`${join('internal', 'gaps')}`),
    )

    for (const file of publicFiles) {
      const content = readFileSync(file, 'utf8')

      expect(content, `${file} should not expose internal gap wording`).not.toContain(
        '建议 issue 标题',
      )

      for (const pattern of developmentOnlyWordingPatterns) {
        expect(content, `${file} should avoid development-only wording ${pattern}`).not.toMatch(
          pattern,
        )
      }
    }
  })

  test('validates future business-console route prefixes without manual whitelist updates', () => {
    const publicRouteTokens =
      '`/planning` `/future-domain/workbench` `/getting-started/example` `/api/mobile/v1/**`'
    const routes = extractBusinessConsoleRouteTokens(publicRouteTokens)

    expect(routes).toContain('/future-domain/workbench')
    expect(routes).not.toContain('/getting-started/example')
    expect(routes).not.toContain('/api/mobile/v1/**')
  })

  test('validates the added core-flow page domains against real business-console routes', () => {
    const addedFlowRoutes = [
      '/scheduling',
      '/approval',
      '/equipment/telemetry/tags',
      '/equipment/telemetry/alarm-rules',
      '/maintenance/spare-parts',
      '/maintenance/reliability',
      '/maintenance/availability',
      '/barcode/rules',
      '/barcode/templates',
      '/barcode/print-batches',
      '/barcode/scans',
    ]

    for (const route of addedFlowRoutes) {
      expect(
        extractBusinessConsoleRouteTokens(`\`${route}\``),
        `${route} should be treated as a business-console route token`,
      ).toContain(route)
      expect(
        businessConsoleRoutes.has(route),
        `${route} should exist in business-console pages`,
      ).toBe(true)
    }
  })

  test('references only real business-console routes in public guide copy', () => {
    const publicFiles = listMarkdownFiles('.').filter(
      (file) => !file.includes(`${join('internal', 'gaps')}`),
    )

    for (const file of publicFiles) {
      const content = readFileSync(file, 'utf8')
      const routes = extractBusinessConsoleRouteTokens(content)

      for (const route of routes) {
        expect(
          businessConsoleRoutes.has(route),
          `${file} should reference an existing business-console route: ${route}`,
        ).toBe(true)
      }
    }
  })
})
