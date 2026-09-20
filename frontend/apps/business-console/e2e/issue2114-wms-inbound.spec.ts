import { test } from '@playwright/test'
import { runWarehouseSupply } from './warehouseSupplyScenario'
const evidencePath = process.env.NERV_IIP_NERV2114_EVIDENCE_PATH
test.skip(!evidencePath, 'NERV-2114 requires an explicitly selected managed FullStack session')
test.use({ trace: 'off', screenshot: 'off' })
test.setTimeout(15 * 60 * 1000)
test('NERV-2114 真实采购收货经仓管上架形成唯一批次库存', async ({ page, browser }) => {
  await runWarehouseSupply({
    page,
    browser,
    issue: 'NERV-2114',
    scenario: process.env.NERV_IIP_NERV2114_SCENARIO!,
    evidencePath: evidencePath!,
    headSha: process.env.NERV_IIP_NERV2114_HEAD_SHA!,
    sessionId: process.env.NERV_IIP_NERV2114_SESSION_ID!,
  })
})
