import { expect, test } from '@playwright/test'

import { NERV_1571_WMS_INBOUND_QUERY_FACTS } from './issue1912-wms-walkthrough-authority'
import {
  proveWmsListPage,
  selectWmsPageOption,
  selectWmsScopeOption,
} from './issue1912-wms-walkthrough-facts'

const inboundPath = '/api/business-console/v1/wms/inbound-orders'

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

test.describe('NERV-1571 / #1912 WMS selection facts (Playwright mock fixture)', () => {
  test.beforeEach(() => {
    test.skip(
      test.info().project.name !== 'desktop',
      'WMS selection probe 仅在 desktop project 运行',
    )
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
        const scopeSearch = scopeMenu.querySelector('input')
        const site = document.querySelector('[aria-label="工厂"]')
        const siteMenu = document.querySelector('#site-menu')
        const scenario = ${JSON.stringify(expectedQuery)}
        let selectedScopeId = ''
        let selectedSiteCode = ''
        const syncScopeOption = () => {
          const option = scopeMenu.querySelector('[role="option"]')
          if (option) option.hidden = scopeSearch.value.trim() !== option.dataset.scopeValue
        }
        scopeSearch.addEventListener('input', syncScopeOption)
        scope.addEventListener('click', () => {
          scopeMenu.hidden = false
          scope.setAttribute('aria-expanded', 'true')
          if (!scopeMenu.querySelector('[role="option"]')) {
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.setAttribute('aria-selected', 'false')
              option.textContent = '收货作业池'
              option.dataset.scopeId = scenario.scopeId
              option.dataset.scopeValue = scenario.scopeKind + ':' + scenario.scopeId
              option.hidden = scopeSearch.value.trim() !== option.dataset.scopeValue
              option.addEventListener('click', () => {
                selectedScopeId = option.dataset.scopeId || ''
                option.setAttribute('aria-selected', 'true')
                scopeMenu.hidden = true
                scope.textContent = option.textContent
                scope.dataset.scopeId = selectedScopeId
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
        scope: {
          label: '作业范围',
          option: '收货作业池',
          scopeKind: expectedQuery.scopeKind,
          scopeId: expectedQuery.scopeId,
        },
        site: { label: '工厂', optionCode: expectedQuery.siteCode },
      },
      query: inboundProof(expectedQuery),
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('siteCode')).toBe('SITE-001')
    await expect(page.getByLabel('作业范围', { exact: true })).toHaveAttribute(
      'data-scope-id',
      expectedQuery.scopeId,
    )
    expect(requests).toHaveLength(1)
  })

  test('作业范围 option 的底层 value 未回读为已选时失败关闭', async ({ page }) => {
    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <button type="button" aria-label="作业范围" aria-expanded="false">未选择范围</button>
      <div role="listbox" hidden>
        <input role="combobox" aria-label="搜索作业范围">
        <button type="button" role="option" aria-selected="false">发货作业池</button>
      </div>
      <script>
        const trigger = document.querySelector('[aria-label="作业范围"]')
        const menu = document.querySelector('[role="listbox"]')
        const search = menu.querySelector('[role="combobox"]')
        const option = menu.querySelector('[role="option"]')
        trigger.addEventListener('click', () => {
          menu.hidden = false
          trigger.setAttribute('aria-expanded', 'true')
        })
        search.addEventListener('input', () => {
          option.hidden = search.value.trim() !== 'work-pool:pool-shipping-001'
        })
        option.addEventListener('click', () => {
          menu.hidden = true
          trigger.textContent = '发货作业池'
          trigger.setAttribute('aria-expanded', 'false')
        })
      </script>
    `)

    await expect(
      selectWmsScopeOption(
        page,
        {
          label: '作业范围',
          option: '发货作业池',
          scopeKind: 'work-pool',
          scopeId: 'pool-shipping-001',
        },
        2_000,
      ),
    ).rejects.toThrow(/true/)
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
      <div role="listbox" hidden></div>
      <script>
        (() => {
          const trigger = document.querySelector('[aria-label="工厂"]')
          const menu = document.querySelector('[role="listbox"]')
          trigger.addEventListener('click', () => {
            menu.hidden = false
            trigger.setAttribute('aria-expanded', 'true')
            setTimeout(() => {
              const option = document.createElement('button')
              option.type = 'button'
              option.setAttribute('role', 'option')
              option.innerHTML = '<span>一号工厂</span><span>SITE-001</span>'
              option.addEventListener('click', () => {
                menu.hidden = true
                trigger.textContent = '一号工厂（SITE-001）'
                trigger.setAttribute('aria-expanded', 'false')
              })
              menu.append(option)
              setTimeout(() => {
                const duplicate = document.createElement('button')
                duplicate.type = 'button'
                duplicate.setAttribute('role', 'option')
                duplicate.innerHTML = '<span>备用工厂</span><span>SITE-001</span>'
                menu.append(duplicate)
              }, 90)
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
