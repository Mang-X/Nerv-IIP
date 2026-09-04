import { expect, test } from '@playwright/test'

import {
  fillFilterAndWaitForListResponse,
  listQueryFingerprint,
  navigateAndWaitForInitialList,
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
      const inputKeyword = url.searchParams.get('keyword') || url.searchParams.get('initial') || ''
      const responseKeyword = url.searchParams.get('responseKeyword') || inputKeyword
      if (url.searchParams.get('hydrate') !== 'false') input.value = inputKeyword
      const load = value => fetch('${listPath}?keyword=' + encodeURIComponent(value)).then(response => {
        if (!response.ok) setTimeout(() => load(value), 10)
      })
      load(responseKeyword)
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

    const { navigation, firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
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
      initialListResponse: firstList,
      initialListNavigationEpoch: navigationEpoch,
      expectedListQueryFingerprint: listQueryFingerprint(firstList.url()),
      timeoutMs: 500,
    })

    expect(result).toEqual({ waitedForResponse: false, reason: 'already-applied' })
    expect(queries).toEqual(['SO-WALK-001'])
  })

  test('URL 同值但初始 200 的环境与分页 fingerprint 错误时仍等待目标响应', async ({ page }) => {
    const fingerprintFixturePath = '/issue1912-filter-fingerprint-fixture'
    const fingerprintListPath = '/api/issue1912-filter-fingerprint-list'
    const queries: string[] = []
    const expectedQuery = {
      organizationId: 'org-live',
      environmentId: 'env-live',
      status: 'open',
      skip: '0',
      take: '10',
    }

    await page.route(`**${fingerprintFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <meta charset="utf-8">
          <label for="fingerprint-filter">关键字搜索</label>
          <input id="fingerprint-filter" value="SO-WALK-001">
          <script>
            const input = document.querySelector('input')
            input.value = 'SO-WALK-001'
            const initial = '${fingerprintListPath}?keyword=SO-WALK-001&organizationId=org-stale&environmentId=env-stale&status=open&skip=0&take=999'
            const target = value => '${fingerprintListPath}?keyword=' + encodeURIComponent(value) + '&organizationId=org-live&environmentId=env-live&status=open&skip=0&take=10'
            void fetch(initial)
            input.addEventListener('input', event => void fetch(target(event.target.value)))
          </script>`,
      }),
    )
    await page.route(`**${fingerprintListPath}*`, async (route) => {
      queries.push(route.request().url())
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const { firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
      route: `${fingerprintFixturePath}?keyword=SO-WALK-001`,
      listPath: fingerprintListPath,
      timeoutMs: 2_000,
    })
    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath: fingerprintListPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      responseMode: 'server',
      initialListResponse: firstList,
      initialListNavigationEpoch: navigationEpoch,
      expectedListQueryFingerprint: listQueryFingerprint(
        `http://walkthrough.fixture${fingerprintListPath}?keyword=SO-WALK-001&${new URLSearchParams(expectedQuery)}`,
      ),
      timeoutMs: 500,
    })
    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(queries).toHaveLength(2)
    expect(new URL(queries[1]).searchParams.get('environmentId')).toBe('env-live')
    expect(new URL(queries[1]).searchParams.get('take')).toBe('10')
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
      expectedListQueryFingerprint: listQueryFingerprint(firstList.url()),
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
      route: `${fixturePath}?keyword=SO-WALK-001&navigation=first`,
      listPath,
      timeoutMs: 2_000,
    })
    await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-WALK-001&navigation=second`,
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

  test('URL keyword 同值但初始 200 响应查询错误时仍等待目标筛选响应', async ({ page }) => {
    const queries: string[] = []

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: fixtureHtml,
      }),
    )
    await page.route(`**${listPath}*`, async (route) => {
      const keyword = new URL(route.request().url()).searchParams.get('keyword') ?? ''
      queries.push(keyword)
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: keyword }] }),
      })
    })

    const { firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-WALK-001&responseKeyword=OTHER-001`,
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
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(queries).toEqual(['OTHER-001', 'SO-WALK-001'])
  })

  test('服务端筛选缺少当前导航的初始列表证据时失败关闭', async ({ page }) => {
    await page.setContent('<label>关键字搜索 <input aria-label="关键字搜索" /></label>')

    await expect(
      fillFilterAndWaitForListResponse(page, {
        route: '/issue1912-filter-policy-fixture?keyword=SO-WALK-001',
        listPath,
        filterLabel: '关键字搜索',
        stableText: 'SO-WALK-001',
        responseMode: 'server',
        timeoutMs: 500,
      }),
    ).rejects.toThrow('owned HTTP 200 initial list response')
  })

  test('导航 epoch 只接受当前 document commit 后发出的列表响应', async ({ page }) => {
    const navigationFixturePath = '/issue1912-navigation-ownership-fixture'
    const navigationListPath = '/api/issue1912-navigation-ownership-list'
    const queries: string[] = []
    const navigationTokens: Array<string | undefined> = []
    let documentLoads = 0
    let releaseSecondDocument: () => void = () => undefined
    const secondDocumentReleased = new Promise<void>((resolve) => {
      releaseSecondDocument = resolve
    })

    await page.route(`**${navigationFixturePath}*`, async (route) => {
      documentLoads += 1
      if (documentLoads === 2) await secondDocumentReleased
      const oldPollScript =
        documentLoads === 1
          ? `setTimeout(() => {
              void fetch('${navigationListPath}?keyword=SO-WALK-001&source=old-poll-during-navigation')
            }, 500)`
          : ''
      await route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <button>导航 fixture</button>
          <script>
            ${oldPollScript}
            void fetch('${navigationListPath}?keyword=SO-WALK-001&source=document-${documentLoads}')
          </script>`,
      })
    })
    await page.route(`**${navigationListPath}*`, async (route) => {
      const url = new URL(route.request().url())
      const source = url.searchParams.get('source') ?? ''
      queries.push(source)
      navigationTokens.push(route.request().headers()['x-nerv-walkthrough-navigation'])
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001', source }] }),
      })
    })

    await navigateAndWaitForInitialList(page, {
      route: `${navigationFixturePath}?epoch=1`,
      listPath: navigationListPath,
      timeoutMs: 2_000,
    })
    const secondNavigationRequest = page.waitForRequest(
      (request) =>
        request.isNavigationRequest() &&
        new URL(request.url()).pathname === navigationFixturePath &&
        new URL(request.url()).searchParams.get('epoch') === '2',
    )
    const secondNavigation = navigateAndWaitForInitialList(page, {
      route: `${navigationFixturePath}?epoch=2`,
      listPath: navigationListPath,
      timeoutMs: 2_000,
    })
    await secondNavigationRequest
    await page.waitForRequest(
      (request) =>
        new URL(request.url()).searchParams.get('source') === 'old-poll-during-navigation',
    )
    releaseSecondDocument()

    const current = await secondNavigation
    expect(new URL(current.firstList.url()).searchParams.get('source')).toBe('document-2')
    expect(queries).toEqual(['document-1', 'old-poll-during-navigation', 'document-2'])
    expect(navigationTokens[0]).toBeTruthy()
    expect(navigationTokens[1]).toBe(navigationTokens[0])
    expect(navigationTokens[2]).toBeTruthy()
    expect(navigationTokens[2]).not.toBe(navigationTokens[0])
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
      const status = 200
      statuses.push(status)
      if (keyword === 'SO-WALK-001') await new Promise((resolve) => setTimeout(resolve, 100))
      await route.fulfill({
        status,
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: 'SO-WALK-001' }] }),
      })
    })

    const { navigation, firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
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
      initialListResponse: firstList,
      initialListNavigationEpoch: navigationEpoch,
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(changedAttempts).toBe(1)
    expect(statuses).toEqual([200, 200])
    expect(queries).toEqual(['SO-OLD-001', 'SO-WALK-001'])
  })

  test('筛选首个请求 4xx 后后台轮询不能冒充本次 fill retry', async ({ page }) => {
    const queries: string[] = []
    const markers: (string | undefined)[] = []
    const staticQuery = '&organizationId=org-001&environmentId=env-dev&status=open&skip=0&take=10'
    const retryFixtureHtml = `<!doctype html>
      <meta charset="utf-8">
      <label>关键字搜索 <input aria-label="关键字搜索" /></label>
      <script>
        const input = document.querySelector('input')
        input.value = 'SO-OLD-001'
        void fetch('${listPath}?keyword=SO-OLD-001${staticQuery}')
        input.addEventListener('input', event => {
          const value = event.target.value
          void fetch('${listPath}?keyword=' + encodeURIComponent(value) + '${staticQuery}')
          setTimeout(() => {
            void fetch('${listPath}?keyword=' + encodeURIComponent(value) + '${staticQuery}')
          }, 20)
        })
      </script>`

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({ contentType: 'text/html', body: retryFixtureHtml }),
    )
    let targetAttempts = 0
    await page.route(`**${listPath}*`, async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const keyword = url.searchParams.get('keyword') ?? ''
      queries.push(`${keyword}:${url.searchParams.get('take') ?? ''}`)
      markers.push(request.headers()['x-nerv-walkthrough-action'])
      if (keyword === 'SO-WALK-001') {
        targetAttempts += 1
        await route.fulfill({
          status: targetAttempts === 1 ? 503 : 200,
          contentType: 'application/json',
          body: JSON.stringify({ items: [{ code: keyword }] }),
        })
        return
      }
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: keyword }] }),
      })
    })

    const { firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-OLD-001`,
      listPath,
      timeoutMs: 2_000,
    })

    await expect(
      fillFilterAndWaitForListResponse(page, {
        route: page.url(),
        listPath,
        filterLabel: '关键字搜索',
        stableText: 'SO-WALK-001',
        responseMode: 'server',
        initialListResponse: firstList,
        initialListNavigationEpoch: navigationEpoch,
        timeoutMs: 500,
      }),
    ).rejects.toThrow('HTTP 503')
    await page.waitForTimeout(50)

    expect(targetAttempts).toBe(2)
    expect(markers[1]).toBeTruthy()
    expect(markers[2]).toBeUndefined()
    expect(queries).toEqual(['SO-OLD-001:10', 'SO-WALK-001:10', 'SO-WALK-001:10'])
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

    const { navigation, firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
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
      initialListNavigationEpoch: navigationEpoch,
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(expectedResponseCompleted).toBe(true)
    expect(queries).toHaveLength(3)
    expect(queries.slice(1).sort()).toEqual(['OTHER-001', 'SO-WALK-001'].sort())
  })

  test('服务端筛选不接受 fill 操作前已发出的同 keyword 响应', async ({ page }) => {
    const queries: string[] = []
    let targetRequests = 0
    let actionResponseCompleted = false

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
        targetRequests += 1
        if (targetRequests === 1) {
          await new Promise((resolve) => setTimeout(resolve, 100))
        } else {
          await new Promise((resolve) => setTimeout(resolve, 300))
          actionResponseCompleted = true
        }
      }
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: keyword }] }),
      })
    })

    const { firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-OLD-001`,
      listPath,
      timeoutMs: 2_000,
    })
    const staleRequest = page.waitForRequest(
      (request) =>
        new URL(request.url()).pathname === listPath &&
        new URL(request.url()).searchParams.get('keyword') === 'SO-WALK-001',
    )
    await page.evaluate(() => {
      void fetch('/api/issue1912-filter-policy-list?keyword=SO-WALK-001')
    })
    await staleRequest

    const result = await fillFilterAndWaitForListResponse(page, {
      route: page.url(),
      listPath,
      filterLabel: '关键字搜索',
      stableText: 'SO-WALK-001',
      responseMode: 'server',
      initialListResponse: firstList,
      initialListNavigationEpoch: navigationEpoch,
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(actionResponseCompleted).toBe(true)
    expect(queries).toEqual(['SO-OLD-001', 'SO-WALK-001', 'SO-WALK-001'])
  })

  test('服务端筛选拒绝同 keyword 的错误分页轮询并等待 exact query 的 fill 请求', async ({
    page,
  }) => {
    const queries: string[] = []
    let fillResponseCompleted = false
    const staticQuery = '&organizationId=org-001&environmentId=env-dev&status=open&skip=0&take=10'
    const raceFixtureHtml = `<!doctype html>
      <meta charset="utf-8">
      <label>关键字搜索 <input aria-label="关键字搜索" /></label>
      <script>
        const input = document.querySelector('input')
        input.value = 'SO-OLD-001'
        void fetch('${listPath}?keyword=SO-OLD-001${staticQuery}')
        input.addEventListener('input', event => {
          const value = event.target.value
          void fetch('${listPath}?keyword=' + encodeURIComponent(value) +
            '&organizationId=org-001&environmentId=env-dev&status=open&skip=0&take=999')
        })
        input.addEventListener('input', event => {
          const value = event.target.value
          queueMicrotask(() => void fetch('${listPath}?keyword=' + encodeURIComponent(value) + '${staticQuery}'))
        })
      </script>`

    await page.route(`**${fixturePath}*`, (route) =>
      route.fulfill({ contentType: 'text/html', body: raceFixtureHtml }),
    )
    await page.route(`**${listPath}*`, async (route) => {
      const url = new URL(route.request().url())
      const keyword = url.searchParams.get('keyword') ?? ''
      const take = url.searchParams.get('take') ?? ''
      queries.push(`${keyword}:${take}`)
      if (keyword === 'SO-WALK-001' && take === '10') {
        await new Promise((resolve) => setTimeout(resolve, 100))
        fillResponseCompleted = true
      }
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ items: [{ code: keyword }] }),
      })
    })

    const { firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
      route: `${fixturePath}?keyword=SO-OLD-001`,
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
      timeoutMs: 2_000,
    })

    expect(result).toEqual({ waitedForResponse: true, reason: 'server-response' })
    expect(fillResponseCompleted).toBe(true)
    expect(queries).toEqual(['SO-OLD-001:10', 'SO-WALK-001:999', 'SO-WALK-001:10'])
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

    const { navigation, firstList, navigationEpoch } = await navigateAndWaitForInitialList(page, {
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
      initialListResponse: firstList,
      initialListNavigationEpoch: navigationEpoch,
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
})
