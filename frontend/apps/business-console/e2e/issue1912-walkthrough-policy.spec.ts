import { expect, test } from '@playwright/test'

import { fillFilterAndWaitForListResponse } from './issue1912-walkthrough-policy'

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
      const load = value => fetch('${listPath}?keyword=' + encodeURIComponent(value))
      load(keyword)
      input.addEventListener('input', event => load(event.target.value))
    </script>
  </body>
</html>`

test.describe('walkthrough filter response boundary', () => {
  test.beforeEach(() => {
    test.skip(
      test.info().project.name !== 'desktop',
      '浏览器无关的 helper 只在 desktop 项目运行',
    )
  })

  test('URL keyword 已由初始列表请求应用时不等待第二次请求', async ({ page }) => {
    const queries: string[] = []

    await page.route(`**${fixturePath}*`, route => route.fulfill({
      contentType: 'text/html',
      body: fixtureHtml,
    }))
    await page.route(`**${listPath}*`, async route => {
      queries.push(new URL(route.request().url()).searchParams.get('keyword') ?? '')
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const initialListResponse = page.waitForResponse(response => (
      response.request().method() === 'GET'
      && new URL(response.url()).pathname === listPath
      && response.status() === 200
    ))
    await page.goto(`${fixturePath}?keyword=SO-WALK-001`)
    await initialListResponse

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

    await page.route(`**${fixturePath}*`, route => route.fulfill({
      contentType: 'text/html',
      body: fixtureHtml,
    }))
    await page.route(`**${listPath}*`, async route => {
      queries.push(new URL(route.request().url()).searchParams.get('keyword') ?? '')
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const initialListResponse = page.waitForResponse(response => (
      response.request().method() === 'GET'
      && new URL(response.url()).pathname === listPath
      && response.status() === 200
    ))
    await page.goto(`${fixturePath}?keyword=SO-OLD-001`)
    await initialListResponse

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      timeoutMs: 2_000,
    })

    expect(result.waitedForResponse).toBe(true)
    expect(queries).toEqual(['SO-OLD-001', 'SO-WALK-001'])
  })

  test('URL keyword 未回填到输入框时仍等待筛选请求', async ({ page }) => {
    const queries: string[] = []

    await page.route(`**${fixturePath}*`, route => route.fulfill({
      contentType: 'text/html',
      body: fixtureHtml,
    }))
    await page.route(`**${listPath}*`, async route => {
      queries.push(new URL(route.request().url()).searchParams.get('keyword') ?? '')
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const initialListResponse = page.waitForResponse(response => (
      response.request().method() === 'GET'
      && new URL(response.url()).pathname === listPath
      && response.status() === 200
    ))
    await page.goto(`${fixturePath}?keyword=SO-WALK-001&hydrate=false`)
    await initialListResponse

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
})
