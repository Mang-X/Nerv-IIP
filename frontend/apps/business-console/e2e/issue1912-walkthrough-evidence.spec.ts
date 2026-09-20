import { expect, test } from '@playwright/test'
import { createWalkthroughEvidence } from './issue1912-walkthrough-evidence'
import { createWalkthroughSession } from './issue1912-walkthrough-session'
import { createWalkthroughPageProofs } from './issue1912-walkthrough-pages'

test('页面证据绑定选择后的列表响应，页面错误只保留闭合摘要', async ({
  page,
  browser,
  baseURL,
}, testInfo) => {
  const ledger = createWalkthroughEvidence()
  const session = await createWalkthroughSession({ page, browser, baseURL: baseURL!, ...ledger })
  const listPath = '/api/issue1912-evidence-list'
  const routePath = '/issue1912-evidence-page'
  await page.route(`**${routePath}`, (route) =>
    route.fulfill({
      contentType: 'text/html',
      body: `<meta charset="utf-8">
      <button aria-label="物料">选择物料</button>
      <button role="option" onclick="load('FG-QJ-P1-L')">FG-QJ-P1-L</button>
      <table><tbody></tbody></table>
      <script>
        async function load(sku) {
          const response = await fetch('${listPath}?skuCode=' + sku)
          const item = await response.json()
          document.querySelector('tbody').innerHTML = '<tr><td>' + item.skuCode + '</td></tr>'
        }
        void load('PK-BOX-01')
      </script>`,
    }),
  )
  await page.route(`**${listPath}*`, (route) =>
    route.fulfill({
      json: { skuCode: new URL(route.request().url()).searchParams.get('skuCode') },
    }),
  )
  const proofs = createWalkthroughPageProofs({
    adminRuntime: session.adminRuntime,
    workerRuntime: session.workerRuntime,
    screenshotDirectory: testInfo.outputPath(),
    uiEvidence: ledger.uiEvidence,
    markFailure: ledger.markFailure,
    baseURL: baseURL!,
  })
  try {
    const proof = await proofs.provePageSafely('finished-goods-inventory', {
      route: routePath,
      listPath,
      stableText: 'FG-QJ-P1-L',
      selectOptions: [{ label: '物料', option: 'FG-QJ-P1-L' }],
      emptyText: '没有库存',
      screenshotName: 'selected.png',
    })
    expect(proof.listQuery).toEqual({ skuCode: 'FG-QJ-P1-L' })
    expect(proof.renderedRowText).toBe('FG-QJ-P1-L')
    expect(ledger.uiEvidence).toEqual([proof])
    const pageError = page.waitForEvent('pageerror')
    await page.evaluate(() => {
      setTimeout(() => {
        throw new Error('customer email alice@example.com')
      }, 0)
    })
    await pageError
    expect(ledger.pageErrors).toHaveLength(1)
    expect(ledger.pageErrors[0]).toMatch(/^erp-admin: failure-sha256:[a-f0-9]{64}$/)
    expect(ledger.failedRequests).toEqual([])
  } finally {
    session.sessionCredentialTracker.clear()
    session.workerSessionCredentialTracker.clear()
    await session.workerContext.close()
  }
})
