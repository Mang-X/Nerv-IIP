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
    expect(source).toContain('<NvSectionCards v-else')
    expect(source).toContain('v-if="!listErrorMessage"')
  })
})
