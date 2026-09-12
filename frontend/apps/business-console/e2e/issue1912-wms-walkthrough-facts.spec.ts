import { expect, test } from '@playwright/test'

import {
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
  NERV_1571_WMS_PAGE_WINDOW_INPUT,
} from './issue1912-wms-walkthrough-authority'
import { queryPath } from './issue1912-walkthrough-query'
import { navigateAndWaitForInitialList } from './issue1912-walkthrough-policy'
import {
  assertWmsPageProofOptions,
  fillWmsKeywordAndConfirm,
  proveWmsListPage,
  refreshWmsListAndConfirm,
  selectWmsPageWindow,
} from './issue1912-wms-walkthrough-facts'
import { mountWmsProductionFixture } from './issue1912-wms-production-fixture'

const inboundPath = '/api/business-console/v1/wms/inbound-orders'
const outboundPath = '/api/business-console/v1/wms/outbound-orders'

function inboundProof(expectedQuery = NERV_1571_WMS_INBOUND_QUERY_FACTS) {
  const { keyword: _keyword, ...selectionQuery } = expectedQuery
  return {
    kind: 'inbound' as const,
    listPath: inboundPath,
    selectionQuery,
    keywordQuery: expectedQuery,
    forbiddenQueryKeys: [] as const,
  }
}

function outboundProof(expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS) {
  const { keyword: _keyword, ...selectionQuery } = expectedQuery
  return {
    kind: 'outbound' as const,
    listPath: outboundPath,
    selectionQuery,
    keywordQuery: expectedQuery,
    forbiddenQueryKeys: ['siteCode'] as const,
  }
}

test.describe('NERV-1571 / #1912 WMS walkthrough facts (Playwright mock fixture)', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', 'WMS facts probe 仅在 desktop project 运行')
  })

  test('WMS proof 拒绝 client/reuse/旁路查询选项，不能短路 HTTP 证明', () => {
    for (const key of [
      'filterResponseMode',
      'reuseCurrentRoute',
      'refreshListBeforeProof',
      'expectedListQuery',
      'listPath',
    ]) {
      expect(() => assertWmsPageProofOptions({ [key]: true })).toThrow(
        `option ${key} is not allowed`,
      )
    }
  })

  test('关键字来自独立 authority，并绑定到实际 server filter 请求', async ({ page }) => {
    const listPath = outboundPath
    const expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    await page.route(`**${listPath}*`, async (route) => {
      await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
    })
    await page.route('**/wms/outbound', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        body: `
          <base href="http://walkthrough.fixture/">
          <label for="keyword-filter">关键字搜索</label>
          <input id="keyword-filter">
          <script>
            const scenario = ${JSON.stringify(expectedQuery)}
            const base = {
              organizationId: scenario.organizationId,
              environmentId: scenario.environmentId,
              scopeKind: scenario.scopeKind,
              scopeId: scenario.scopeId,
              skip: String(scenario.skip),
              take: String(scenario.take),
            }
            const request = (keyword) => {
              const query = new URLSearchParams({ ...base })
              if (keyword) query.set('keyword', keyword)
              void fetch('${listPath}?' + query.toString())
            }
            setTimeout(() => request(''), 100)
            document.querySelector('#keyword-filter').addEventListener('input', (event) => {
              request(event.target.value)
            })
          </script>
        `,
      })
    })

    const initial = await navigateAndWaitForInitialList(page, {
      route: '/wms/outbound',
      listPath,
      timeoutMs: 2_000,
    })
    const response = await fillWmsKeywordAndConfirm(
      page,
      outboundProof(expectedQuery),
      initial.firstList,
      initial.navigationEpoch,
      '关键字搜索',
      2_000,
    )
    expect(response.status()).toBe(200)
    const actualUrl = new URL(response.url())
    expect(actualUrl.searchParams.getAll('keyword')).toEqual([expectedQuery.keyword])
  })

  test('关键字证明拒绝延迟旧 action 的同路径响应', async ({ page }) => {
    const listPath = outboundPath
    const expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    let oldActionStarted!: () => void
    let releaseOldAction!: () => void
    const oldActionStartedPromise = new Promise<void>((resolve) => {
      oldActionStarted = resolve
    })
    const oldActionResponsePromise = new Promise<void>((resolve) => {
      releaseOldAction = resolve
    })

    await page.route(`**${listPath}*`, async (route) => {
      const request = route.request()
      const marker = request.headers()['x-nerv-walkthrough-action']
      if (marker === 'old-action') {
        oldActionStarted()
        await oldActionResponsePromise
        await route.fulfill({ status: 503, body: JSON.stringify({ items: [] }) })
        return
      }
      await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
    })
    await page.route('**/wms/outbound', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'text/html; charset=utf-8',
        body: `
          <base href="http://walkthrough.fixture/">
          <label for="keyword-filter">关键字搜索</label>
          <input id="keyword-filter">
          <script>
            const scenario = ${JSON.stringify(expectedQuery)}
            const base = {
              organizationId: scenario.organizationId,
              environmentId: scenario.environmentId,
              scopeKind: scenario.scopeKind,
              scopeId: scenario.scopeId,
              skip: String(scenario.skip),
              take: String(scenario.take),
            }
            const request = (keyword) => {
              const query = new URLSearchParams({ ...base })
              if (keyword) query.set('keyword', keyword)
              void fetch('${listPath}?' + query.toString())
            }
            setTimeout(() => request(''), 100)
            document.querySelector('#keyword-filter').addEventListener('input', (event) => {
              request(event.target.value)
            })
          </script>
        `,
      })
    })

    const initial = await navigateAndWaitForInitialList(page, {
      route: '/wms/outbound',
      listPath,
      timeoutMs: 2_000,
    })
    const oldAction = page.evaluate(
      async ({ url }) => {
        await fetch(url, { headers: { 'x-nerv-walkthrough-action': 'old-action' } })
      },
      {
        url: `http://walkthrough.fixture${queryPath(listPath, expectedQuery)}`,
      },
    )
    await oldActionStartedPromise

    let currentActionResponseSeen!: () => void
    const currentActionResponse = new Promise<void>((resolve) => {
      currentActionResponseSeen = resolve
    })
    const responseObserver = (response: import('@playwright/test').Response) => {
      const marker = response.request().headers()['x-nerv-walkthrough-action']
      const url = new URL(response.url())
      if (
        response.status() === 200 &&
        marker !== undefined &&
        marker !== 'old-action' &&
        url.pathname === listPath &&
        url.searchParams.get('keyword') === expectedQuery.keyword
      ) {
        releaseOldAction()
        currentActionResponseSeen()
        page.off('response', responseObserver)
      }
    }
    page.on('response', responseObserver)
    try {
      const response = await fillWmsKeywordAndConfirm(
        page,
        outboundProof(expectedQuery),
        initial.firstList,
        initial.navigationEpoch,
        '关键字搜索',
        2_000,
      )
      expect(response.request().headers()['x-nerv-walkthrough-action']).not.toBe('old-action')
      await currentActionResponse
      await oldAction
    } finally {
      releaseOldAction()
      page.off('response', responseObserver)
    }
  })

  test('生产 WMS 页面通过真实分页动作绑定刷新请求', async ({ page }) => {
    const listPath = outboundPath
    const expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    const { targetRequests } = await mountWmsProductionFixture(page, {
      kind: 'outbound',
      targetPath: listPath,
    })

    const response = await proveWmsListPage({
      kind: 'outbound',
      page,
      selection: {
        scope: {
          label: '作业范围',
          option: '发货作业池',
          scopeKind: expectedQuery.scopeKind,
          scopeId: expectedQuery.scopeId,
        },
      },
      pageWindow: NERV_1571_WMS_PAGE_WINDOW_INPUT,
      query: outboundProof(expectedQuery),
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('take')).toBe(String(expectedQuery.take))
    await expect(page.getByLabel('作业范围', { exact: true })).toContainText('发货作业池')
    const markedRefreshRequests = targetRequests.filter((entry) => entry.marked)
    expect(markedRefreshRequests).toHaveLength(1)
    expect(new URL(markedRefreshRequests[0]!.request.url()).searchParams.get('take')).toBe(
      String(expectedQuery.take),
    )

    await selectWmsPageWindow(page, { ...NERV_1571_WMS_PAGE_WINDOW_INPUT, take: 10 }, 2_000)
    await expect(
      refreshWmsListAndConfirm(page, outboundProof(expectedQuery), 2_000),
    ).rejects.toThrow('query facts')
  })

  test('范围类型与列表路径不一致时在发起选择前失败关闭', async ({ page }) => {
    await expect(
      proveWmsListPage({
        kind: 'inbound',
        page,
        selection: {
          scope: {
            label: '作业范围',
            option: '收货作业池',
            scopeKind: 'work-pool',
            scopeId: 'pool-receiving-001',
          },
          site: { label: '工厂', optionCode: 'SITE-001' },
        },
        pageWindow: NERV_1571_WMS_PAGE_WINDOW_INPUT,
        query: {
          ...inboundProof(NERV_1571_WMS_INBOUND_QUERY_FACTS),
          listPath: outboundPath as never,
        },
      }),
    ).rejects.toThrow('unexpected list path')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        selection: {
          scope: {
            label: '作业范围',
            option: '收货作业池',
            scopeKind: 'work-pool',
            scopeId: 'pool-shipping-001',
          },
        },
        pageWindow: NERV_1571_WMS_PAGE_WINDOW_INPUT,
        query: {
          ...outboundProof(NERV_1571_WMS_OUTBOUND_QUERY_FACTS),
          listPath: inboundPath as never,
        },
      }),
    ).rejects.toThrow('unexpected list path')
  })
})
