import { readFileSync } from 'node:fs'
import { dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

// Pages migrated to the FE-2 block gold standard (see DESIGN/patterns/pages/list-workbench.md).
// Add each page here as it is migrated in stage B — the rules below then prevent drift.
const GOLD_STANDARD_PAGES = [
  'mes/operation-tasks.vue',
  'mes/work-orders/index.vue',
  'mes/schedules.vue',
  'mes/wip.vue',
  'mes/capacity.vue',
  'mes/dispatch.vue',
  'mes/receipts.vue',
  'mes/materials.vue',
  'mes/downtime.vue',
  'mes/handovers.vue',
  'mes/production-reports.vue',
  'mes/quality.vue',
  'quality/inspections.vue',
  'quality/ncrs.vue',
  'quality/reason-codes.vue',
  'engineering/production-versions.vue',
  'engineering/standard-operations.vue',
  'engineering/ebom.vue',
  'engineering/mbom.vue',
  'engineering/routings.vue',
  'engineering/items.vue',
  'engineering/documents.vue',
  'engineering/eco.vue',
  'master-data/skus.vue',
  'master-data/partners.vue',
  'master-data/product-categories.vue',
  'master-data/skill-catalog.vue',
  'master-data/code-rules.vue',
  // facilities.vue 是「工厂结构」树-详情示范页（左树 + 右详情），不再是黄金标准列表，
  // 不含 DataTable/DataTablePagination 必备块，故从此清单移除。树页自身约束见 facilities.test.ts。
  'master-data/devices.vue',
  'master-data/units.vue',
  // organization.vue 改为「组织与班组」树-详情页（左部门树 + 右详情/班组），不再是黄金标准列表，
  // 不含主 DataTable/DataTablePagination 必备块，故从此清单移除。树页约束见 organization.test.ts。
  'master-data/scheduling.vue',
  // skills.vue 升为「人员技能」矩阵页（工人 × 技能），不再是黄金标准列表，
  // 不含 DataTable/DataTablePagination 必备块，故从此清单移除。矩阵约束见 skills.test.ts。
  'master-data/reference-data.vue',
  'wms/inbound.vue',
  'wms/outbound.vue',
  'wms/wcs.vue',
  'wms/counts.vue',
  'barcode/rules.vue',
  'barcode/templates.vue',
  'maintenance/work-orders.vue',
  'maintenance/plans.vue',
  'equipment/telemetry/tags.vue',
  'equipment/telemetry/alarm-rules.vue',
  'equipment/telemetry/history.vue',
  'equipment/telemetry/oee.vue',
  'erp/index.vue',
  'erp/procurement/rfqs.vue',
  'erp/procurement/supplier-quotations.vue',
  'erp/procurement/purchase-orders.vue',
  'erp/procurement/receipts.vue',
  'erp/sales.vue',
  'erp/sales/quotations.vue',
  'erp/sales/orders.vue',
  'erp/sales/deliveries.vue',
  'erp/finance.vue',
  'erp/finance/ar-ap.vue',
  'erp/finance/vouchers.vue',
  'erp/finance/cost-candidates.vue',
]

// SectionCards is NOT required: KPI cards are decided per page (business-meaningful,
// decision-driving metrics only — never mechanical pagination/tree metadata). Many
// maintenance pages legitimately have none. See master-data-templates.md §0/§2 and
// business-console AGENTS.md §1.5-B.
// 分页已集成进 DataTablePro（manual 服务端模式 → 卡内页脚），不再要求独立的 DataTablePagination 块。
const REQUIRED_BLOCKS = ['PageHeader', 'DataTablePro']
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
        const lower = src.toLowerCase()
        for (const term of BANNED_COPY) {
          expect.soft(lower, `"${term}" must not appear in a business page`).not.toContain(term.toLowerCase())
        }
      })
    })
  }
})
