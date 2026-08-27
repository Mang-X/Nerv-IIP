import { expect, test } from '@playwright/test'

import {
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
} from './issue1912-wms-walkthrough-authority'
import {
  proveWmsListPage,
  selectWmsPageOption,
  withWmsInitialListResponseGuard,
} from './issue1912-wms-walkthrough-facts'

test.describe('NERV-1571 / #1912 WMS walkthrough facts (Playwright mock fixture)', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', 'WMS facts probe 仅在 desktop project 运行')
  })

  test('显式作业范围选择后，刷新只接受当前租户和默认分页的列表响应', async ({ page }) => {
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
      <button id="refresh" type="button">刷新</button>
      <script>
        const scope = document.querySelector('[aria-label="作业范围"]')
        const menu = document.querySelector('[role="listbox"]')
        const scenario = ${JSON.stringify(expectedQuery)}
        let selectedScopeId = ''
        let refreshCount = 0
        scope.addEventListener('click', () => {
          menu.hidden = false
          scope.setAttribute('aria-expanded', 'true')
          if (!menu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.textContent = '发货作业池'
              option.dataset.scopeId = scenario.scopeId
              option.addEventListener('click', () => {
                selectedScopeId = option.dataset.scopeId || ''
                menu.hidden = true
                scope.textContent = option.textContent
                scope.setAttribute('aria-expanded', 'false')
              })
              menu.append(option)
            }, 60)
          }
        })
        document.querySelector('#refresh').addEventListener('click', () => {
          refreshCount += 1
          const query = new URLSearchParams({
            organizationId: scenario.organizationId,
            environmentId: scenario.environmentId,
            scopeKind: scenario.scopeKind,
            scopeId: selectedScopeId,
            skip: String(scenario.skip),
            take: String(refreshCount === 2 ? 999 : scenario.take),
            keyword: scenario.keyword,
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
        scope: { label: '作业范围', option: '发货作业池', scopeId: expectedQuery.scopeId },
      },
      query: {
        kind: 'outbound',
        listPath,
        expectedQuery,
        forbiddenQueryKeys: ['siteCode'],
      },
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('take')).toBe('10')
    expect(requests).toHaveLength(1)

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        selection: {
          scope: { label: '作业范围', option: '发货作业池', scopeId: expectedQuery.scopeId },
        },
        query: { kind: 'outbound', listPath, expectedQuery, forbiddenQueryKeys: ['siteCode'] },
      }),
    ).rejects.toThrow('query facts')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        selection: {
          scope: { label: '作业范围', option: '发货作业池', scopeId: expectedQuery.scopeId },
        },
        query: { kind: 'outbound', listPath, expectedQuery, forbiddenQueryKeys: ['siteCode'] },
      }),
    ).rejects.toThrow('must not send query field siteCode')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        selection: {
          scope: { label: '作业范围', option: '发货作业池', scopeId: expectedQuery.scopeId },
        },
        query: { kind: 'outbound', listPath, expectedQuery, forbiddenQueryKeys: ['siteCode'] },
      }),
    ).rejects.toThrow('query facts')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        selection: {
          scope: { label: '作业范围', option: '发货作业池', scopeId: expectedQuery.scopeId },
        },
        query: { kind: 'outbound', listPath, expectedQuery, forbiddenQueryKeys: ['siteCode'] },
      }),
    ).rejects.toThrow('query facts')
  })

  test('范围类型与列表路径不一致时在发起选择前失败关闭', async ({ page }) => {
    await expect(
      proveWmsListPage({
        kind: 'inbound',
        page,
        selection: {
          scope: { label: '作业范围', option: '收货作业池', scopeId: 'pool-receiving-001' },
          site: { label: '工厂', optionCode: 'SITE-001' },
        },
        query: {
          kind: 'inbound',
          listPath: '/api/business-console/v1/wms/outbound-orders' as never,
          expectedQuery: NERV_1571_WMS_INBOUND_QUERY_FACTS,
          forbiddenQueryKeys: [],
        },
      }),
    ).rejects.toThrow('unexpected list path')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        selection: {
          scope: { label: '作业范围', option: '收货作业池', scopeId: 'pool-shipping-001' },
        },
        query: {
          kind: 'outbound',
          listPath: '/api/business-console/v1/wms/inbound-orders' as never,
          expectedQuery: NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
          forbiddenQueryKeys: ['siteCode'],
        },
      }),
    ).rejects.toThrow('unexpected list path')
  })

  test('首个 WMS 列表响应为 503 或错误路径时，不接受后续 200 自洽通过', async ({ page }) => {
    const inboundPath = '/api/business-console/v1/wms/inbound-orders'
    const outboundPath = '/api/business-console/v1/wms/outbound-orders'
    const keyword = NERV_1571_WMS_OUTBOUND_QUERY_FACTS.keyword
    await page.route('**/api/business-console/v1/wms/*', async (route) => {
      const phase = new URL(route.request().url()).searchParams.get('phase')
      const attempt = new URL(route.request().url()).searchParams.get('attempt')
      await route.fulfill({
        status: phase === '503-first' && attempt === '1' ? 503 : 200,
        body: JSON.stringify({ items: [] }),
      })
    })
    await page.setContent('<base href="http://walkthrough.fixture/">')

    await expect(
      withWmsInitialListResponseGuard(
        page,
        inboundPath,
        async () =>
          page.evaluate(
            async ({ path, keyword: requestKeyword }) => {
              await fetch(`${path}?phase=503-first&attempt=1&keyword=${requestKeyword}`)
              await fetch(`${path}?phase=503-first&attempt=2&keyword=${requestKeyword}`)
            },
            { path: `http://walkthrough.fixture${inboundPath}`, keyword },
          ),
        2_000,
      ),
    ).rejects.toThrow('HTTP 503')

    const secondPage = await page.context().newPage()
    try {
      await secondPage.route('**/api/business-console/v1/wms/*', async (route) => {
        await route.fulfill({ status: 200, body: JSON.stringify({}) })
      })
      await secondPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          secondPage,
          inboundPath,
          async () =>
            secondPage.evaluate(
              async ({ wrongPath, targetPath, requestKeyword }) => {
                await fetch(`${wrongPath}&keyword=${requestKeyword}`)
                await fetch(`${targetPath}&keyword=${requestKeyword}`)
              },
              {
                wrongPath: `http://walkthrough.fixture${outboundPath}?phase=wrong-first`,
                targetPath: `http://walkthrough.fixture${inboundPath}?phase=wrong-first`,
                requestKeyword: keyword,
              },
            ),
          2_000,
        ),
      ).rejects.toThrow('response path')
    } finally {
      await secondPage.close()
    }
  })

  test('入库必须显式选择范围和已加载工厂，公开请求带有所选 siteCode', async ({ page }) => {
    // 该 mock route 不是 FullStack 证据，只用于把两个页面选择绑定到一个可观察请求，
    // 并确保移除任一选择都会失败关闭。
    const listPath = '/api/business-console/v1/wms/inbound-orders'
    const expectedQuery = NERV_1571_WMS_INBOUND_QUERY_FACTS
    const requests: string[] = []

    await page.route(`**${listPath}*`, async (route) => {
      const url = route.request().url()
      requests.push(url)
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [] }),
      })
    })
    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="作业范围" aria-expanded="false">未选择范围</button>
      <div id="scope-menu" role="listbox" hidden>
        <input role="combobox" aria-label="搜索作业范围">
      </div>
      <button type="button" aria-label="工厂" aria-expanded="false">未选择工厂</button>
      <div id="site-menu" role="listbox" hidden></div>
      <button id="refresh" type="button">刷新</button>
      <script>
        const scope = document.querySelector('[aria-label="作业范围"]')
        const scopeMenu = document.querySelector('#scope-menu')
        const site = document.querySelector('[aria-label="工厂"]')
        const siteMenu = document.querySelector('#site-menu')
        const scenario = ${JSON.stringify(expectedQuery)}
        let selectedScopeId = ''
        let selectedSiteCode = ''
        scope.addEventListener('click', () => {
          scopeMenu.hidden = false
          scope.setAttribute('aria-expanded', 'true')
          if (!scopeMenu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.textContent = '收货作业池'
              option.dataset.scopeId = scenario.scopeId
              option.addEventListener('click', () => {
                selectedScopeId = option.dataset.scopeId || ''
                scopeMenu.hidden = true
                scope.textContent = option.textContent
                scope.setAttribute('aria-expanded', 'false')
              })
              scopeMenu.append(option)
            }, 40)
          }
        })
        site.addEventListener('click', () => {
          siteMenu.hidden = false
          site.setAttribute('aria-expanded', 'true')
          if (!siteMenu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.innerHTML = '<span>一号工厂</span><span>SITE-001</span>'
              option.dataset.siteCode = scenario.siteCode
              option.addEventListener('click', () => {
                selectedSiteCode = option.dataset.siteCode || ''
                siteMenu.hidden = true
                site.textContent = '一号工厂（' + selectedSiteCode + '）'
                site.setAttribute('aria-expanded', 'false')
              })
              siteMenu.append(option)
            }, 80)
          }
        })
        document.querySelector('#refresh').addEventListener('click', () => {
          const query = new URLSearchParams({
            organizationId: scenario.organizationId,
            environmentId: scenario.environmentId,
            scopeKind: scenario.scopeKind,
            scopeId: selectedScopeId,
            skip: String(scenario.skip),
            take: String(scenario.take),
            keyword: scenario.keyword,
            siteCode: selectedSiteCode,
          })
          void fetch('${listPath}?' + query.toString())
        })
      </script>
    `)

    const response = await proveWmsListPage({
      kind: 'inbound',
      page,
      selection: {
        scope: { label: '作业范围', option: '收货作业池', scopeId: expectedQuery.scopeId },
        site: { label: '工厂', optionCode: 'SITE-001' },
      },
      query: { kind: 'inbound', listPath, expectedQuery, forbiddenQueryKeys: [] },
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('siteCode')).toBe('SITE-001')
    expect(requests).toHaveLength(1)
  })

  test('工厂选择按公开编码匹配，缺失或重复编码均失败关闭', async ({ page }) => {
    await expect(selectWmsPageOption(page, { label: '工厂', option: '' }, 2_000)).rejects.toThrow(
      'exactly one',
    )
    await expect(
      selectWmsPageOption(page, { label: '工厂', optionCode: '' }, 2_000),
    ).rejects.toThrow('exactly one')

    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="工厂" aria-expanded="false">工厂</button>
      <div role="listbox" hidden>
        <button type="button" role="option"><span>一号工厂</span><span>SITE-001</span></button>
        <button type="button" role="option"><span>二号工厂</span><span>SITE-002</span></button>
      </div>
      <script>
        const trigger = document.querySelector('[aria-label="工厂"]')
        const menu = document.querySelector('[role="listbox"]')
        trigger.addEventListener('click', () => {
          menu.hidden = false
          trigger.setAttribute('aria-expanded', 'true')
        })
        menu.querySelectorAll('[role="option"]').forEach(option => option.addEventListener('click', () => {
          menu.hidden = true
          trigger.textContent = '一号工厂（SITE-001）'
          trigger.setAttribute('aria-expanded', 'false')
        }))
      </script>
    `)

    await selectWmsPageOption(page, { label: '工厂', optionCode: 'SITE-001' }, 2_000)
    await expect(page.getByLabel('工厂', { exact: true })).toHaveAttribute('aria-expanded', 'false')
    await expect(page.getByLabel('工厂', { exact: true })).toContainText('SITE-001')

    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="工厂" aria-expanded="false">工厂</button>
      <div role="listbox" hidden>
        <button type="button" role="option"><span>一号工厂</span><span>SITE-001</span></button>
        <button type="button" role="option"><span>备用工厂</span><span>SITE-001</span></button>
      </div>
      <script>
        (() => {
          const duplicateTrigger = document.querySelector('[aria-label="工厂"]')
          const duplicateMenu = document.querySelector('[role="listbox"]')
          duplicateTrigger.addEventListener('click', () => {
            duplicateMenu.hidden = false
            duplicateTrigger.setAttribute('aria-expanded', 'true')
          })
        })()
      </script>
    `)
    await expect(
      selectWmsPageOption(page, { label: '工厂', optionCode: 'SITE-001' }, 2_000),
    ).rejects.toThrow('expected one catalog option, found 2')

    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="工厂" aria-expanded="false">工厂</button>
      <div role="listbox" hidden>
        <button type="button" role="option"><span>二号工厂</span><span>SITE-002</span></button>
      </div>
      <script>
        (() => {
          const missingTrigger = document.querySelector('[aria-label="工厂"]')
          const missingMenu = document.querySelector('[role="listbox"]')
          missingTrigger.addEventListener('click', () => {
            missingMenu.hidden = false
            missingTrigger.setAttribute('aria-expanded', 'true')
          })
        })()
      </script>
    `)
    await expect(
      selectWmsPageOption(page, { label: '工厂', optionCode: 'SITE-001' }, 2_000),
    ).rejects.toThrow('expected one catalog option, found 0')

    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="工厂" aria-expanded="false">工厂</button>
      <div role="listbox" hidden>
        <button type="button" role="option"><span>一号工厂</span><span>SITE-001</span></button>
      </div>
      <script>
        (() => {
          const noReadbackTrigger = document.querySelector('[aria-label="工厂"]')
          const noReadbackMenu = document.querySelector('[role="listbox"]')
          noReadbackTrigger.addEventListener('click', () => {
            noReadbackMenu.hidden = false
            noReadbackTrigger.setAttribute('aria-expanded', 'true')
          })
          noReadbackMenu.querySelector('[role="option"]').addEventListener('click', () => {
            noReadbackMenu.hidden = true
            noReadbackTrigger.setAttribute('aria-expanded', 'false')
          })
        })()
      </script>
    `)
    await expect(
      selectWmsPageOption(page, { label: '工厂', optionCode: 'SITE-001' }, 2_000),
    ).rejects.toThrow('did not expose selected')
  })

  test('工厂目录首项后续变为重复编码时不得提前选择', async ({ page }) => {
    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="工厂" aria-expanded="false">工厂</button>
      <div role="listbox" hidden>
        <button type="button" role="option"><span>一号工厂</span><span>SITE-001</span></button>
      </div>
      <script>
        (() => {
          const trigger = document.querySelector('[aria-label="工厂"]')
          const menu = document.querySelector('[role="listbox"]')
          trigger.addEventListener('click', () => {
            menu.hidden = false
            trigger.setAttribute('aria-expanded', 'true')
            setTimeout(() => {
              const duplicate = document.createElement('button')
              duplicate.type = 'button'
              duplicate.setAttribute('role', 'option')
              duplicate.innerHTML = '<span>备用工厂</span><span>SITE-001</span>'
              menu.append(duplicate)
            }, 40)
          })
        })()
      </script>
    `)
    await expect(
      selectWmsPageOption(page, { label: '工厂', optionCode: 'SITE-001' }, 2_000),
    ).rejects.toThrow('expected one catalog option, found 2')
  })
})
