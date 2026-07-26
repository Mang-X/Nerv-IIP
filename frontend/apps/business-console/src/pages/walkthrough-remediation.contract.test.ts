import { describe, expect, it } from 'vitest'

const businessPages = import.meta.glob('./**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>
const screenPages = import.meta.glob('../../../screen/src/pages/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>
const screenComponents = import.meta.glob('../../../screen/src/components/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>
const screenLayouts = import.meta.glob('../../../screen/src/layouts/**/*.vue', {
  eager: true,
  import: 'default',
  query: '?raw',
}) as Record<string, string>

describe('2026-07-26 leadership walkthrough remediation', () => {
  it('does not leave a single KPI card isolated from a metric group', () => {
    for (const [path, source] of Object.entries(businessPages)) {
      const cardCount = source.match(/<NvMetricCard\b/g)?.length ?? 0
      const hasCompanionMetric = /<NvMetric(?:Strip|Ring)\b/.test(source)
      expect.soft(cardCount === 1 && !hasCompanionMetric, path).toBe(false)
    }
  })

  it('does not use gradient backgrounds on either walkthrough surface', () => {
    const sources = {
      ...businessPages,
      ...screenPages,
      ...screenComponents,
      ...screenLayouts,
    }
    for (const [path, source] of Object.entries(sources)) {
      expect
        .soft(source, `${path} contains a Tailwind gradient background`)
        .not.toMatch(/\bbg-gradient(?:-to-[a-z]+)?\b/)
      expect
        .soft(source, `${path} contains a CSS gradient background`)
        .not.toMatch(/background(?:-image)?\s*:\s*(?:linear|radial|repeating-linear)-gradient\(/s)
    }
  })

  it('keeps implementation-stage copy out of business pages', () => {
    for (const [path, source] of Object.entries(businessPages)) {
      expect.soft(source, path).not.toMatch(/过渡入口|正式页面|即将上线/)
    }
  })

  it('does not render an empty spare-parts table when the list request failed', () => {
    const source = businessPages['./maintenance/spare-parts.vue']
    expect(source).toContain('v-if="!listErrorMessage"')
    expect(source).toContain('<Empty v-if="listErrorMessage"')
    expect(source).toContain('数据来自维修工单的备件需求')
  })

  it('keeps implementation details out of screen-facing copy', () => {
    const sources = { ...screenPages, ...screenComponents, ...screenLayouts }
    for (const [path, source] of Object.entries(sources)) {
      const template = source.slice(source.indexOf('<template>'), source.lastIndexOf('</template>'))
      expect
        .soft(template, path)
        .not.toMatch(
          /待\s*#\d+|·\s*#\d+|historian|数据为\s*mock|后端接入|聚合端点|适配器聚合|接入中|读面|演示数据冒充|演示推算|作业域演示数据|演示模式/,
        )
    }
  })

  it('keeps all three quality backlog groups inside the fixed-height screen panel', () => {
    const source = screenPages['../../../screen/src/pages/quality.vue']
    expect(source).toMatch(/\.qb-ib\s*\{[\s\S]*?gap:\s*4px/)
    expect(source).toMatch(/\.ib-meta\s*\{[\s\S]*?margin-top:\s*2px/)
    expect(source).toMatch(/\.ib-bar\s*\{[\s\S]*?margin-top:\s*4px/)
  })
})
