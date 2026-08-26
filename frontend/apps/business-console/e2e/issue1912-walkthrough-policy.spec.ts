import { expect, test } from '@playwright/test'

import {
  clickRefreshAndWaitForListResponse,
  clickTabAndConfirmUnmount,
  fillFilterAndWaitForListResponse,
  navigateAndWaitForInitialList,
  RequestFailureEvidenceTracker,
} from './issue1912-walkthrough-policy'

const fixturePath = '/issue1912-filter-policy-fixture'
const listPath = '/api/issue1912-filter-policy-list'
const clientFixturePath = '/issue1912-client-filter-policy-fixture'
const clientListPath = '/api/issue1912-client-filter-policy-list'

const fixtureHtml = `<!doctype html>
<html>
  <head><meta charset="utf-8" /></head>
  <body>
    <label>关键字搜索 <input aria-label="关键字搜索" /></label>
    <script>
      const input = document.querySelector('input')
      const url = new URL(location.href)
      const keyword = url.searchParams.get('keyword') || url.searchParams.get('initial') || ''
      if (url.searchParams.get('hydrate') !== 'false') input.value = keyword
      const load = value => fetch('${listPath}?keyword=' + encodeURIComponent(value)).then(response => {
        if (!response.ok) setTimeout(() => load(value), 10)
      })
      load(keyword)
      input.addEventListener('input', event => load(event.target.value))
    </script>
  </body>
</html>`

const clientFixtureHtml = `<!doctype html>
<html>
  <head><meta charset="utf-8" /></head>
  <body>
    <label>需求池关键字 <input aria-label="需求池关键字" value="SO-OLD-001" /></label>
    <table><tbody>
      <tr data-code="SO-WALK-001"><td>SO-WALK-001</td></tr>
      <tr data-code="SO-OLD-001"><td>SO-OLD-001</td></tr>
    </tbody></table>
    <script>
      const input = document.querySelector('input')
      const rows = [...document.querySelectorAll('tr')]
      const apply = () => rows.forEach(row => {
        row.hidden = !row.textContent.includes(input.value)
      })
      input.addEventListener('input', apply)
      apply()
      fetch('${clientListPath}').then(response => {
        if (!response.ok) throw new Error('initial list failed')
      })
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
      responseMode: 'server',
      timeoutMs: 500,
    })

    expect(result).toEqual({ waitedForResponse: false, reason: 'already-applied' })
    expect(queries).toEqual(['SO-WALK-001'])
  })

  test('初始列表响应已完成但 URL 未携带 keyword 时不重复等待', async ({ page }) => {
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

    const { firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?initial=SO-WALK-001`,
      listPath,
      timeoutMs: 2_000,
    })

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      responseMode: 'server',
      initialListResponse: firstList,
      initialListNavigationEpoch: navigationEpoch,
      timeoutMs: 500,
    })

    expect(result).toEqual({
      waitedForResponse: false,
      reason: 'response-already-complete',
    })
    expect(queries).toEqual(['SO-WALK-001'])
  })

  test('不同导航的同路径同 keyword 响应不能作为当前已完成响应', async ({ page }) => {
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

    const firstNavigation = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?initial=SO-WALK-001&navigation=first`,
      listPath,
      timeoutMs: 2_000,
    })
    await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?initial=SO-WALK-001&navigation=second`,
      listPath,
      timeoutMs: 2_000,
    })

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      responseMode: 'server',
      initialListResponse: firstNavigation.firstList,
      initialListNavigationEpoch: firstNavigation.navigationEpoch,
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(queries).toEqual(['SO-WALK-001', 'SO-WALK-001', 'SO-WALK-001'])
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
      responseMode: 'server',
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(statuses).toEqual([200, 503, 200])
    expect(queries).toEqual(['SO-OLD-001', 'SO-WALK-001', 'SO-WALK-001'])
  })

  test('服务端筛选只接受目标 keyword 的已完成响应', async ({ page }) => {
    const queries: string[] = []
    let expectedResponseCompleted = false

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: fixtureHtml,
      }),
    )
    await page.route(`**${listPath}*`, async (route) => {
      const keyword = new URL(route.request().url()).searchParams.get('keyword') ?? ''
      queries.push(keyword)
      if (keyword === 'SO-WALK-001') {
        await new Promise((resolve) => setTimeout(resolve, 100))
        expectedResponseCompleted = true
      }
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: keyword }] }),
      })
    })

    const { navigation, firstList } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-OLD-001&decoy=true`,
      listPath,
      timeoutMs: 2_000,
    })
    expect(navigation?.status()).toBe(200)
    expect(firstList.status()).toBe(200)

    await page.evaluate(() => {
      const input = document.querySelector('input')
      input?.addEventListener(
        'input',
        (event) => {
          if ((event.target as HTMLInputElement).value !== 'SO-WALK-001') return
          void fetch('/api/issue1912-filter-policy-list?keyword=OTHER-001')
        },
        { once: true },
      )
    })

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      responseMode: 'server',
      initialListResponse: firstList,
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(expectedResponseCompleted).toBe(true)
    expect(queries).toHaveLength(3)
    expect(queries.slice(1).sort()).toEqual(['OTHER-001', 'SO-WALK-001'].sort())
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
      responseMode: 'server',
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(queries).toEqual(['SO-WALK-001', 'SO-WALK-001'])
  })

  test('前端筛选变化只等待 DOM 稳定，不等待不存在的第二个列表请求', async ({ page }) => {
    let listRequestCount = 0

    await page.route(`**${clientFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: clientFixtureHtml,
      }),
    )
    await page.route(`**${clientListPath}*`, async (route) => {
      listRequestCount += 1
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const { firstList } = await navigateAndWaitForInitialList(page, {
      route: clientFixturePath,
      listPath: clientListPath,
      timeoutMs: 2_000,
    })

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath: clientListPath,
      filterLabel: '需求池关键字',
      stableText: 'SO-WALK-001',
      responseMode: 'client',
      initialListResponse: firstList,
      timeoutMs: 250,
    })

    expect(result).toEqual({ waitedForResponse: false, reason: 'client-side-filter' })
    expect(listRequestCount).toBe(1)
    await expect(page.locator('[data-code="SO-WALK-001"]')).toBeVisible()
    await expect(page.locator('[data-code="SO-OLD-001"]')).toBeHidden()
  })

  test('同一路由刷新只接受本次刷新发出的已完成列表响应', async ({ page }) => {
    const refreshFixturePath = '/issue1912-refresh-policy-fixture'
    const refreshPath = '/api/issue1912-refresh-policy-list'
    const revisions: string[] = []

    await page.route(`**${refreshFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `
          <meta charset="utf-8">
          <button type="button">刷新</button>
          <p id="revision"></p>
          <script>
            void fetch('${refreshPath}?revision=stale')
            document.querySelector('button').addEventListener('click', async () => {
              const response = await fetch('${refreshPath}?revision=fresh')
              document.querySelector('#revision').textContent = (await response.json()).revision
            })
          </script>
        `,
      }),
    )
    await page.route(`**${refreshPath}*`, async (route) => {
      const revision = new URL(route.request().url()).searchParams.get('revision') ?? ''
      revisions.push(revision)
      if (revision === 'stale') await new Promise((resolve) => setTimeout(resolve, 100))
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ revision }),
      })
    })
    const staleRequest = page.waitForRequest(
      (request) => new URL(request.url()).searchParams.get('revision') === 'stale',
    )
    await page.goto(refreshFixturePath)
    await staleRequest

    const refreshed = await clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)

    expect(new URL(refreshed.url()).searchParams.get('revision')).toBe('fresh')
    expect(refreshed.status()).toBe(200)
    expect(revisions).toEqual(['stale', 'fresh'])
    await expect(page.locator('#revision')).toHaveText('fresh')
  })

  test('tab 容器保留但旧 slot 内容切换后才建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab">下一页</button>
      <div role="tabpanel" id="old-panel" data-state="active">
        <div id="old-content">旧面板</div>
      </div>
      <div role="tabpanel" id="next-panel" data-state="inactive" hidden>
        <div id="next-content">新面板</div>
      </div>
      <script>
        document.querySelector('#next-tab').addEventListener('click', () => {
          const oldPanel = document.querySelector('#old-panel')
          const nextPanel = document.querySelector('#next-panel')
          oldPanel.dataset.state = 'inactive'
          oldPanel.hidden = true
          oldPanel.replaceChildren()
          nextPanel.dataset.state = 'active'
          nextPanel.hidden = false
        })
      </script>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await clickTabAndConfirmUnmount(page, '下一页', tracker, 1_000)
    await expect(page.locator('#old-panel')).toHaveCount(1)
    await expect(page.locator('#old-panel')).toBeHidden()
    await expect(page.locator('#old-content')).toHaveCount(0)
    await expect(page.locator('#next-panel')).toBeVisible()
  })

  test('点击失败时不建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab" disabled>下一页</button>
      <div role="tabpanel" id="old-panel"><div>旧面板</div></div>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await expect(clickTabAndConfirmUnmount(page, '下一页', tracker, 1_000)).rejects.toThrow()
    expect(await page.locator('#old-panel').count()).toBe(1)
  })

  test('点击成功但内容未卸载时不建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab">下一页</button>
      <div role="tabpanel" id="old-panel" data-state="active"><div>旧面板</div></div>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await expect(clickTabAndConfirmUnmount(page, '下一页', tracker, 100)).rejects.toThrow()
    expect(await page.locator('#old-panel').count()).toBe(1)
  })

  test('仅隐藏旧 tab 容器但保留旧内容时不建立 component-unmount 证据', async ({ page }) => {
    await page.setContent(`
      <button role="tab" id="next-tab">下一页</button>
      <div role="tabpanel" id="old-panel" data-state="active">
        <div id="old-content">旧面板</div>
      </div>
      <div role="tabpanel" id="next-panel" data-state="inactive" hidden>新面板</div>
      <script>
        document.querySelector('#next-tab').addEventListener('click', () => {
          const oldPanel = document.querySelector('#old-panel')
          const nextPanel = document.querySelector('#next-panel')
          oldPanel.dataset.state = 'inactive'
          oldPanel.hidden = true
          nextPanel.dataset.state = 'active'
          nextPanel.hidden = false
        })
      </script>
    `)
    const tracker = new RequestFailureEvidenceTracker()

    await expect(clickTabAndConfirmUnmount(page, '下一页', tracker, 100)).rejects.toThrow()
    await expect(page.locator('#old-panel')).toBeHidden()
    await expect(page.locator('#old-content')).toHaveCount(1)
  })
})
