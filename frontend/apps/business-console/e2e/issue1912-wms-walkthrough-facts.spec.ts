import { expect, test } from '@playwright/test'

import {
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
} from './issue1912-wms-walkthrough-authority'
import { queryPath } from './issue1912-walkthrough-query'
import { navigateAndWaitForInitialList } from './issue1912-walkthrough-policy'
import {
  assertWmsPageProofOptions,
  fillWmsKeywordAndConfirm,
  proveWmsListPage,
} from './issue1912-wms-walkthrough-facts'

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

  test('显式作业范围和页窗口选择后，刷新只接受当前租户与已选分页的列表响应', async ({ page }) => {
    // This fixture exercises the WMS proof wiring only. It does not provide real identity,
    // provider, HTTP service, persistence, or cleanup evidence for FullStack/FullChain.
    const listPath = '/api/business-console/v1/wms/outbound-orders'
    const expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    const requests: string[] = []

    await page.route(`**${listPath}*`, async (route) => {
      const url = route.request().url()
      requests.push(url)
      if (new URL(url).searchParams.get('environmentId') === 'env-stale') {
        await new Promise((resolve) => setTimeout(resolve, 100))
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [] }),
      })
    })
    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="作业范围" aria-expanded="false">自动首项</button>
      <div role="listbox" hidden><input role="combobox" aria-label="搜索作业范围"></div>
      <button type="button" aria-label="每页条数" aria-expanded="false">10</button>
      <div id="page-size-menu" role="listbox" hidden>
        <button type="button" role="option" aria-selected="true" data-value="10">10</button>
        <button type="button" role="option" aria-selected="false" data-value="20">20</button>
      </div>
      <span aria-label="当前页">1 / 1</span>
      <button id="refresh" type="button">刷新</button>
      <script>
        const scope = document.querySelector('[aria-label="作业范围"]')
        const menu = document.querySelector('[role="listbox"]')
        const search = menu.querySelector('input')
        const pageSizeTrigger = document.querySelector('[aria-label="每页条数"]')
        const pageSizeMenu = document.querySelector('#page-size-menu')
        const scenario = ${JSON.stringify(expectedQuery)}
        let selectedScopeId = ''
        let selectedPageSize = 10
        let refreshCount = 0
        const syncScopeOption = () => {
          const option = menu.querySelector('[role="option"]')
          if (option) option.hidden = search.value.trim() !== option.dataset.scopeValue
        }
        search.addEventListener('input', syncScopeOption)
        scope.addEventListener('click', () => {
          menu.hidden = false
          scope.setAttribute('aria-expanded', 'true')
          if (!menu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.setAttribute('aria-selected', 'false')
              option.textContent = '发货作业池'
              option.dataset.scopeId = scenario.scopeId
              option.dataset.scopeValue = scenario.scopeKind + ':' + scenario.scopeId
              option.hidden = search.value.trim() !== option.dataset.scopeValue
              option.addEventListener('click', () => {
                selectedScopeId = option.dataset.scopeId || ''
                option.setAttribute('aria-selected', 'true')
                menu.hidden = true
                scope.textContent = option.textContent
                scope.dataset.scopeId = selectedScopeId
                scope.setAttribute('aria-expanded', 'false')
              })
              menu.append(option)
            }, 60)
          }
        })
        pageSizeTrigger.addEventListener('click', () => {
          pageSizeMenu.hidden = false
          pageSizeTrigger.setAttribute('aria-expanded', 'true')
        })
        pageSizeMenu.querySelectorAll('[role="option"]').forEach(option => option.addEventListener('click', () => {
          pageSizeMenu.querySelectorAll('[role="option"]').forEach(item => item.setAttribute('aria-selected', String(item === option)))
          selectedPageSize = Number(option.dataset.value)
          pageSizeTrigger.textContent = option.dataset.value
          pageSizeMenu.hidden = true
          pageSizeTrigger.setAttribute('aria-expanded', 'false')
        }))
        document.querySelector('#refresh').addEventListener('click', () => {
          refreshCount += 1
          const query = new URLSearchParams({
            organizationId: scenario.organizationId,
            environmentId: scenario.environmentId,
            scopeKind: scenario.scopeKind,
            scopeId: selectedScopeId,
            skip: String(scenario.skip),
            take: String(refreshCount === 2 ? 999 : selectedPageSize),
          })
          if (refreshCount === 3) query.set('siteCode', 'SITE-001')
          if (refreshCount === 4) query.set('environmentId', 'env-stale')
          if (refreshCount === 5) query.set('scopeId', 'pool-other-001')
          void fetch('${listPath}?' + query.toString())
        })
      </script>
    `)

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
      query: outboundProof(expectedQuery),
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('take')).toBe('20')
    await expect(page.getByLabel('作业范围', { exact: true })).toHaveAttribute(
      'data-scope-id',
      expectedQuery.scopeId,
    )
    expect(requests).toHaveLength(1)

    await expect(
      proveWmsListPage({
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
        query: outboundProof(expectedQuery),
      }),
    ).rejects.toThrow('query facts')

    await expect(
      proveWmsListPage({
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
        query: outboundProof(expectedQuery),
      }),
    ).rejects.toThrow('must not send query field siteCode')

    await expect(
      proveWmsListPage({
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
        query: outboundProof(expectedQuery),
      }),
    ).rejects.toThrow('query facts')

    await expect(
      proveWmsListPage({
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
        query: outboundProof(expectedQuery),
      }),
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
        query: {
          ...outboundProof(NERV_1571_WMS_OUTBOUND_QUERY_FACTS),
          listPath: inboundPath as never,
        },
      }),
    ).rejects.toThrow('unexpected list path')
  })
})
