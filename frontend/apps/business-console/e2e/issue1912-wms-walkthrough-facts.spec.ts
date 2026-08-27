import { expect, test } from '@playwright/test'

import { queryPath } from './issue1912-walkthrough-query'
import {
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
} from './issue1912-wms-walkthrough-authority'
import { proveWmsListPage, selectWmsPageOption } from './issue1912-wms-walkthrough-facts'

test.describe('NERV-1571 / #1912 WMS walkthrough facts (Playwright mock fixture)', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', 'WMS facts probe 仅在 desktop project 运行')
  })

  test('显式作业范围选择后，刷新只接受当前租户和默认分页的列表响应', async ({ page }) => {
    // This fixture exercises the WMS proof wiring only. It does not provide real identity,
    // provider, HTTP service, persistence, or cleanup evidence for FullStack/FullChain.
    const listPath = '/api/business-console/v1/wms/outbound-orders'
    const expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    const validQuery = queryPath(listPath, expectedQuery, 'http://walkthrough.fixture')
    const invalidQuery = queryPath(
      listPath,
      { ...expectedQuery, take: 999 },
      'http://walkthrough.fixture',
    )
    const forbiddenSiteQuery = queryPath(
      listPath,
      { ...expectedQuery, siteCode: 'SITE-001' },
      'http://walkthrough.fixture',
    )
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
      <div role="listbox" hidden></div>
      <button id="refresh" type="button">刷新</button>
      <script>
        const scope = document.querySelector('[aria-label="作业范围"]')
        const menu = document.querySelector('[role="listbox"]')
        let scopeSelected = false
        let refreshCount = 0
        scope.addEventListener('click', () => {
          menu.hidden = false
          scope.setAttribute('aria-expanded', 'true')
          if (!menu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.textContent = '收货作业池'
              option.addEventListener('click', () => {
                scopeSelected = true
                menu.hidden = true
                scope.textContent = '收货作业池'
                scope.setAttribute('aria-expanded', 'false')
              })
              menu.append(option)
            }, 60)
          }
        })
        document.querySelector('#refresh').addEventListener('click', () => {
          refreshCount += 1
          const query = scopeSelected && refreshCount === 1
            ? '${validQuery.slice(validQuery.indexOf('?') + 1)}'
            : refreshCount === 2
              ? '${invalidQuery.slice(invalidQuery.indexOf('?') + 1)}'
              : '${forbiddenSiteQuery.slice(forbiddenSiteQuery.indexOf('?') + 1)}'
          void fetch('${listPath}?' + query)
        })
      </script>
    `)

    const response = await proveWmsListPage({
      kind: 'outbound',
      page,
      listPath,
      selection: { scope: { label: '作业范围', option: '收货作业池' } },
      expectedQuery,
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('take')).toBe('10')
    expect(requests).toHaveLength(1)

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        listPath,
        selection: { scope: { label: '作业范围', option: '收货作业池' } },
        expectedQuery,
      }),
    ).rejects.toThrow('query facts')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        listPath,
        selection: { scope: { label: '作业范围', option: '收货作业池' } },
        expectedQuery,
      }),
    ).rejects.toThrow('must not send query field siteCode')
  })

  test('范围类型与列表路径不一致时在发起选择前失败关闭', async ({ page }) => {
    await expect(
      proveWmsListPage({
        kind: 'inbound',
        page,
        listPath: '/api/business-console/v1/wms/outbound-orders' as never,
        selection: {
          scope: { label: '作业范围', option: '收货作业池' },
          site: { label: '工厂', optionCode: 'SITE-001' },
        },
        expectedQuery: NERV_1571_WMS_INBOUND_QUERY_FACTS,
      }),
    ).rejects.toThrow('unexpected list path')

    await expect(
      proveWmsListPage({
        kind: 'outbound',
        page,
        listPath: '/api/business-console/v1/wms/inbound-orders' as never,
        selection: { scope: { label: '作业范围', option: '收货作业池' } },
        expectedQuery: NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
      }),
    ).rejects.toThrow('unexpected list path')
  })

  test('入库必须显式选择范围和已加载工厂，公开请求带有所选 siteCode', async ({ page }) => {
    // 该 mock route 不是 FullStack 证据，只用于把两个页面选择绑定到一个可观察请求，
    // 并确保移除任一选择都会失败关闭。
    const listPath = '/api/business-console/v1/wms/inbound-orders'
    const expectedQuery = NERV_1571_WMS_INBOUND_QUERY_FACTS
    const validQuery = queryPath(listPath, expectedQuery, 'http://walkthrough.fixture')
    const missingSiteQuery = queryPath(
      listPath,
      Object.fromEntries(Object.entries(expectedQuery).filter(([key]) => key !== 'siteCode')),
      'http://walkthrough.fixture',
    )
    const requests: string[] = []

    await page.route(`**${listPath}*`, async (route) => {
      const url = route.request().url()
      requests.push(url)
      const valid = url === `http://walkthrough.fixture${validQuery}`
      await route.fulfill({
        status: valid ? 200 : 400,
        contentType: 'application/json',
        body: JSON.stringify({ items: [] }),
      })
    })
    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="作业范围" aria-expanded="false">未选择范围</button>
      <div id="scope-menu" role="listbox" hidden></div>
      <button type="button" aria-label="工厂" aria-expanded="false">未选择工厂</button>
      <div id="site-menu" role="listbox" hidden></div>
      <button id="refresh" type="button">刷新</button>
      <script>
        const scope = document.querySelector('[aria-label="作业范围"]')
        const scopeMenu = document.querySelector('#scope-menu')
        const site = document.querySelector('[aria-label="工厂"]')
        const siteMenu = document.querySelector('#site-menu')
        let scopeSelected = false
        let siteSelected = false
        scope.addEventListener('click', () => {
          scopeMenu.hidden = false
          scope.setAttribute('aria-expanded', 'true')
          if (!scopeMenu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.textContent = '收货作业池'
              option.addEventListener('click', () => {
                scopeSelected = true
                scopeMenu.hidden = true
                scope.textContent = '收货作业池'
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
              option.addEventListener('click', () => {
                siteSelected = true
                siteMenu.hidden = true
                site.textContent = '一号工厂（SITE-001）'
                site.setAttribute('aria-expanded', 'false')
              })
              siteMenu.append(option)
            }, 80)
          }
        })
        document.querySelector('#refresh').addEventListener('click', () => {
          const query = scopeSelected && siteSelected
            ? '${validQuery.slice(validQuery.indexOf('?'))}'
            : '${missingSiteQuery.slice(missingSiteQuery.indexOf('?'))}'
          void fetch('${listPath}' + query)
        })
      </script>
    `)

    const response = await proveWmsListPage({
      kind: 'inbound',
      page,
      listPath,
      selection: {
        scope: { label: '作业范围', option: '收货作业池' },
        site: { label: '工厂', optionCode: 'SITE-001' },
      },
      expectedQuery,
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
