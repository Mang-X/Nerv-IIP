import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const source = readFileSync(
  resolve(dirname(fileURLToPath(import.meta.url)), 'analysis.vue'),
  'utf8',
)

describe('quality analysis request failure presentation', () => {
  it('does not present NCR-derived KPI or dimensions as zero/empty when the list request failed', () => {
    expect(source).toContain('v-if="listErrorMessage"')
    expect(source).toContain('NCR 数据加载失败')
    // KPI 卡整组挂在 v-else 上：加载失败时不渲染任何派生指标（不把失败画成 0）。
    expect(source).toContain('<div v-else class="grid gap-4 lg:grid-cols-3">')
    expect(source).toContain('v-if="!listErrorMessage"')
  })
})
