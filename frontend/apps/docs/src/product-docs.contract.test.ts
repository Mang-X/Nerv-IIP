import { describe, expect, test } from 'vitest'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

const appRoot = process.cwd()
const docsRoot = join(appRoot, 'docs')
const workspaceRoot = join(appRoot, '..', '..')
const internalGapRoot = join(docsRoot, 'internal', 'gaps')

const requiredGuideSections = [
  '适用角色',
  '前置资料',
  '页面入口',
  '操作步骤',
  '业务对象/单据流',
  '状态变化',
  '成功结果',
  '常见失败/空态',
  '当前限制',
]

function readDocsFile(relativePath: string) {
  return readFileSync(join(docsRoot, relativePath), 'utf8')
}

function listMarkdownFiles(relativePath: string) {
  const absolutePath = join(docsRoot, relativePath)

  return readdirSync(absolutePath, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
    .map((entry) => join(entry.parentPath, entry.name))
}

function routeExists(route: string) {
  const typedRouter = readFileSync(join(workspaceRoot, 'apps', 'business-console', 'typed-router.d.ts'), 'utf8')

  return typedRouter.includes(`'${route}'`)
}

describe('product docs app contract', () => {
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

  test('documents the five required core process diagrams', () => {
    const processContent = readDocsFile('processes/index.md')
    const requiredDiagrams = [
      '工程资料：EBOM -> MBOM -> 工艺路线 -> 生产版本',
      '计划生产：需求 -> MRP -> APS -> 生产计划 -> 工单 -> 报工 -> 入库',
      '仓储库存：收货 -> 上架 -> 库存 -> 拣货 -> 出库',
      '质量审批：检验 -> NCR -> 审批 -> 处置 -> 放行/返工/报废',
      '设备维护：报警 -> 维修工单 -> 备件 -> 恢复 -> 可靠性指标',
    ]

    for (const diagram of requiredDiagrams) {
      expect(processContent).toContain(diagram)
    }

    expect(processContent.match(/```mermaid/g)?.length ?? 0).toBeGreaterThanOrEqual(5)
  })

  test('keeps internal gap evidence out of public guide copy', () => {
    expect(existsSync(internalGapRoot)).toBe(true)

    const gapFiles = listMarkdownFiles('internal/gaps')

    expect(gapFiles.length).toBeGreaterThanOrEqual(3)

    for (const file of gapFiles) {
      const content = readFileSync(file, 'utf8')

      expect(content).toContain('## 证据页面')
      expect(content).toContain('## 建议 issue 标题')
    }

    const publicFiles = listMarkdownFiles('.').filter((file) => !file.includes(`${join('internal', 'gaps')}`))

    for (const file of publicFiles) {
      const content = readFileSync(file, 'utf8')

      expect(content, `${file} should not expose internal gap wording`).not.toContain('建议 issue 标题')
    }
  })

  test('references only real business-console routes in public guide copy', () => {
    const publicFiles = listMarkdownFiles('.').filter((file) => !file.includes(`${join('internal', 'gaps')}`))

    for (const file of publicFiles) {
      const content = readFileSync(file, 'utf8')
      const routes = Array.from(content.matchAll(/`(\/[a-z0-9][a-z0-9/:?-]*)`/g), (match) => match[1])
        .filter((route) => route.startsWith('/mes') || route.startsWith('/wms') || route.startsWith('/engineering') || route.startsWith('/inventory') || route.startsWith('/planning') || route.startsWith('/quality') || route.startsWith('/master-data'))
        .filter((route) => !route.includes(':'))

      for (const route of routes) {
        expect(routeExists(route), `${file} should reference an existing business-console route: ${route}`).toBe(true)
      }
    }
  })
})
