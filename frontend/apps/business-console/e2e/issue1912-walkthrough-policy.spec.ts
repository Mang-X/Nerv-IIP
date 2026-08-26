import { expect, test } from '@playwright/test'

import {
  clickTabAndConfirmUnmount,
  fillFilterAndWaitForListResponse,
  navigateAndWaitForInitialList,
  RequestFailureEvidenceTracker,
} from './issue1912-walkthrough-policy'

const fixturePath = '/issue1912-filter-policy-fixture'
const listPath = '/api/issue1912-filter-policy-list'

const fixtureHtml = `<!doctype html>
<html>
  <head><meta charset="utf-8" /></head>
  <body>
    <label>关键字搜索 <input aria-label="关键字搜索" /></label>
    <script>
      const input = document.querySelector('input')
      const url = new URL(location.href)
      const keyword = url.searchParams.get('keyword') || ''
      if (url.searchParams.get('hydrate') !== 'false') input.value = keyword
      const load = value => fetch('${listPath}?keyword=' + encodeURIComponent(value)).then(response => {
        if (!response.ok) setTimeout(() => load(value), 10)
      })
      load(keyword)
      input.addEventListener('input', event => load(event.target.value))
    </script>
  </body>
</html>`

test.describe('walkthrough filter response boundary', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', '浏览器无关的 helper 只在 desktop 项目运行')
  })

  test('URL keyword 已由初始列表请求应用时不等待第二次请求', async ({ page }) => {
    const queries: string[] = []

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: fixtureHtml,
      }),
    )
    await page.route(`**${listPath}*`, async (route) => {
      queries.push(new URL(route.request().url()).searchParams.get('keyword') ?? '')
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const { navigation, firstList } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-WALK-001`,
      listPath,
      timeoutMs: 2_000,
    })
    expect(navigation?.status()).toBe(200)
    expect(firstList.status()).toBe(200)

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      timeoutMs: 500,
    })

    expect(result.waitedForResponse).toBe(false)
    expect(queries).toEqual(['SO-WALK-001'])
  })

  test('真正变更筛选值时仍等待 200 列表响应', async ({ page }) => {
    const queries: string[] = []
    const statuses: number[] = []
    let changedAttempts = 0

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: fixtureHtml,
      }),
    )
    await page.route(`**${listPath}*`, async (route) => {
      const keyword = new URL(route.request().url()).searchParams.get('keyword') ?? ''
      queries.push(keyword)
      changedAttempts += keyword === 'SO-WALK-001' ? 1 : 0
      const status = keyword === 'SO-WALK-001' && changedAttempts === 1 ? 503 : 200
      statuses.push(status)
      if (status === 200 && changedAttempts > 1)
        await new Promise((resolve) => setTimeout(resolve, 100))
      await route.fulfill({
        status,
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const { navigation, firstList } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-OLD-001`,
      listPath,
      timeoutMs: 2_000,
    })
    expect(navigation?.status()).toBe(200)
    expect(firstList.status()).toBe(200)

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      timeoutMs: 2_000,
    })

    expect(result.waitedForResponse).toBe(true)
    expect(statuses).toEqual([200, 503, 200])
    expect(queries).toEqual(['SO-OLD-001', 'SO-WALK-001', 'SO-WALK-001'])
  })

  test('URL keyword 未回填到输入框时仍等待筛选请求', async ({ page }) => {
    const queries: string[] = []

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: fixtureHtml,
      }),
    )
    await page.route(`**${listPath}*`, async (route) => {
      queries.push(new URL(route.request().url()).searchParams.get('keyword') ?? '')
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const { navigation, firstList } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-WALK-001&hydrate=false`,
      listPath,
      timeoutMs: 2_000,
    })
    expect(navigation?.status()).toBe(200)
    expect(firstList.status()).toBe(200)

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      timeoutMs: 2_000,
    })

    expect(result.waitedForResponse).toBe(true)
    expect(queries).toEqual(['SO-WALK-001', 'SO-WALK-001'])
  })

  test('仅在 tab 内容实际卸载后才建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab">下一页</button>
      <div role="tabpanel" id="old-panel">旧面板</div>
      <script>
        document.querySelector('#next-tab').addEventListener('click', () => {
          document.querySelector('#old-panel').remove()
        })
      </script>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await clickTabAndConfirmUnmount(page, '下一页', tracker, 1_000)
    expect(await page.locator('#old-panel').count()).toBe(0)
  })

  test('点击失败时不建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab" disabled>下一页</button>
      <div role="tabpanel" id="old-panel">旧面板</div>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await expect(clickTabAndConfirmUnmount(page, '下一页', tracker, 1_000)).rejects.toThrow()
    expect(await page.locator('#old-panel').count()).toBe(1)
  })

  test('点击成功但内容未卸载时不建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab">下一页</button>
      <div role="tabpanel" id="old-panel">旧面板</div>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await expect(clickTabAndConfirmUnmount(page, '下一页', tracker, 100)).rejects.toThrow()
    expect(await page.locator('#old-panel').count()).toBe(1)
  })
})
