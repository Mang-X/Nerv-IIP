import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// Pages migrated to the FE-2 block gold standard (see DESIGN/patterns/pages/list-workbench.md).
// Add each page here as it is migrated in stage B — the rules below then prevent drift.
const GOLD_STANDARD_PAGES = ['mes/operation-tasks.vue', 'master-data/skus.vue']

const REQUIRED_BLOCKS = ['PageHeader', 'DataTable', 'DataTablePagination', 'SectionCard']
const LEGACY_BLOCKS = [
  'BusinessPageHeader',
  'BusinessContextBar',
  'BusinessMetricCell',
  'BusinessTablePagination',
  'BusinessRowActions',
  'BusinessStatusBadge',
  'BusinessEmptyState',
  'BusinessFormStatus',
]
// Developer-language / fake-data terms that must never appear in a business page.
// (organization/environment IDs are legitimate inside API request bodies, so they are
// not token-banned here; their *visible* exposure is prevented by not using BusinessContextBar.)
const BANNED_COPY = ['operationId', 'sourceSystem', 'demo', 'seed', 'mock', '样例']

const pagesDir = dirname(fileURLToPath(import.meta.url))
function read(page: string): string {
  return readFileSync(resolve(pagesDir, page), 'utf8')
}

describe('gold-standard page enforcement', () => {
  for (const page of GOLD_STANDARD_PAGES) {
    describe(page, () => {
      const src = read(page)

      it('uses the required FE-2 block components', () => {
        for (const block of REQUIRED_BLOCKS) {
          expect.soft(src, `${block} should be used`).toMatch(new RegExp(`\\b${block}\\b`))
        }
      })

      it('does not use legacy per-app block components', () => {
        for (const legacy of LEGACY_BLOCKS) {
          expect.soft(src, `${legacy} must be replaced by a @nerv-iip/ui block`).not.toMatch(new RegExp(`\\b${legacy}\\b`))
        }
      })

      it('renders the table via DataTable, not a raw <Table>', () => {
        expect.soft(src, 'raw <Table> assembly is banned — use DataTable').not.toMatch(/<Table[\s>]/)
        expect.soft(src).not.toMatch(/<TableHeader[\s>]/)
      })

      it('has no deep imports (only @nerv-iip/ui and @nerv-iip/app-shell)', () => {
        expect.soft(src, 'deep @nerv-iip/ui import is banned').not.toMatch(/from ['"]@nerv-iip\/ui\//)
        expect.soft(src, 'import reka-ui directly is banned').not.toMatch(/from ['"]reka-ui/)
        expect.soft(src, 'import shadcn-vue directly is banned').not.toMatch(/from ['"]shadcn-vue/)
      })

      it('has no developer-language / platform-metadata copy', () => {
        for (const term of BANNED_COPY) {
          expect.soft(src, `"${term}" must not appear in a business page`).not.toContain(term)
        }
      })
    })
  }
})
