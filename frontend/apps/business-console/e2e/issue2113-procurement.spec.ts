import { test } from '@playwright/test'
import { runProcurement } from './procurementScenario'

const evidencePath = process.env.NERV_IIP_NERV2113_EVIDENCE_PATH
test.skip(!evidencePath, 'NERV-2113 requires an explicitly selected managed FullStack session')
test.use({ trace: 'off', screenshot: 'off' })
test.setTimeout(12 * 60 * 1000)

test('NERV-2113 公开采购、审批与收货保持需求和来源且重放不重复', async ({ page }) => {
  await runProcurement({
    page,
    issue: 'NERV-2113',
    scenario: process.env.NERV_IIP_NERV2113_SCENARIO!,
    evidencePath: evidencePath!,
    headSha: process.env.NERV_IIP_NERV2113_HEAD_SHA!,
    sessionId: process.env.NERV_IIP_NERV2113_SESSION_ID!,
  })
})
