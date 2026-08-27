import { expect, test } from '@playwright/test'

import { proveWmsListPage, selectWmsPageOption } from './issue1912-wms-walkthrough-facts'

test.describe('NERV-1571 / #1912 WMS walkthrough facts (Playwright mock fixture)', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', 'WMS facts probe 仅在 desktop project 运行')
  })

  test('显式作业范围选择后，刷新只接受当前租户和默认分页的列表响应', async ({ page }) => {
    // This fixture exercises the WMS proof wiring only. It does not provide real identity,
    // provider, HTTP service, persistence, or cleanup evidence for FullStack/FullChain.
    const listPath = '/api/business-console/v1/wms/outbound-orders'
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
            ? 'organizationId=org-live&environmentId=env-live&scopeKind=work-pool&scopeId=pool-shipping-001&skip=0&take=10'
            : 'organizationId=org-live&environmentId=env-live&scopeKind=work-pool&scopeId=pool-shipping-001&skip=0&take=999'
          void fetch('${listPath}?' + query)
        })
      </script>
    `)

    const response = await proveWmsListPage({
      kind: 'outbound',
      page,
      listPath,
      selection: { scope: { label: '作业范围', option: '收货作业池' } },
      expectedQuery: {
        organizationId: 'org-live',
        environmentId: 'env-live',
        scopeKind: 'work-pool',
        scopeId: 'pool-shipping-001',
        skip: 0,
        take: 10,
      },
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
        expectedQuery: {
          organizationId: 'org-live',
          environmentId: 'env-live',
          scopeKind: 'work-pool',
          scopeId: 'pool-shipping-001',
          skip: 0,
          take: 10,
        },
      }),
    ).rejects.toThrow('query facts')
  })

  test('工厂选择按公开编码匹配，缺失或重复编码均失败关闭', async ({ page }) => {
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
          trigger.setAttribute('aria-expanded', 'false')
        }))
      </script>
    `)

    await selectWmsPageOption(page, { label: '工厂', optionCode: 'SITE-001' }, 2_000)
    await expect(page.getByLabel('工厂', { exact: true })).toHaveAttribute('aria-expanded', 'false')

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
  })
})
