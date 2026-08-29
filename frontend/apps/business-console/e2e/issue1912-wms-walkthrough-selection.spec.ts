import { expect, test } from '@playwright/test'

import {
  NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT,
  NERV_1571_WMS_INBOUND_QUERY_FACTS,
  NERV_1571_WMS_OUTBOUND_QUERY_FACTS,
  NERV_1571_WMS_PAGE_WINDOW_INPUT,
} from './issue1912-wms-walkthrough-authority'
import {
  proveWmsListPage,
  selectWmsPageOption,
  selectWmsPageWindow,
  selectWmsScopeOption,
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

test.describe('NERV-1571 / #1912 WMS selection facts (production page fixture)', () => {
  test.beforeEach(() => {
    test.skip(
      test.info().project.name !== 'desktop',
      'WMS selection probe 仅在 desktop project 运行',
    )
  })

  test('生产入库页面必须显式选择范围、工厂和页窗口，公开请求带有所选事实', async ({ page }) => {
    const expectedQuery = NERV_1571_WMS_INBOUND_QUERY_FACTS
    const { targetRequests } = await mountWmsProductionFixture(page, {
      kind: 'inbound',
      targetPath: inboundPath,
    })
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
      pageWindow: NERV_1571_WMS_PAGE_WINDOW_INPUT,
      query: inboundProof(expectedQuery),
    })

    expect(response.status()).toBe(200)
    const responseUrl = new URL(response.url())
    expect(responseUrl.searchParams.get('siteCode')).toBe(expectedQuery.siteCode)
    expect(responseUrl.searchParams.get('take')).toBe(String(expectedQuery.take))
    await expect(page.getByLabel('作业范围', { exact: true })).toContainText('收货作业池')
    const markedRefreshRequests = targetRequests.filter((entry) => entry.marked)
    expect(markedRefreshRequests).toHaveLength(1)
    expect(new URL(markedRefreshRequests[0]!.request.url()).search).toBe(responseUrl.search)
  })

  test('生产低基数入库页面按默认页窗口证明请求，不要求分页控件', async ({ page }) => {
    const expectedQuery = {
      ...NERV_1571_WMS_INBOUND_QUERY_FACTS,
      take: NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT.take,
    }
    const { targetRequests } = await mountWmsProductionFixture(page, {
      kind: 'inbound',
      targetPath: inboundPath,
      targetTotal: 1,
      expectPagination: false,
    })
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
      pageWindow: NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT,
      query: inboundProof(expectedQuery),
    })

    expect(response.status()).toBe(200)
    expect(new URL(response.url()).searchParams.get('take')).toBe('10')
    await expect(page.getByLabel('每页条数', { exact: true })).toHaveCount(0)
    const markedRefreshRequests = targetRequests.filter((entry) => entry.marked)
    expect(markedRefreshRequests).toHaveLength(1)
    expect(new URL(markedRefreshRequests[0]!.request.url()).searchParams.get('take')).toBe('10')
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

  test('生产 NvPagination 的分页窗口必须通过公开 DOM 回读并产生对应请求', async ({ page }) => {
    const expectedQuery = NERV_1571_WMS_OUTBOUND_QUERY_FACTS
    const { targetRequests } = await mountWmsProductionFixture(page, {
      kind: 'outbound',
      targetPath: outboundPath,
    })
    await selectWmsScopeOption(page, {
      label: '作业范围',
      option: '发货作业池',
      scopeKind: expectedQuery.scopeKind,
      scopeId: expectedQuery.scopeId,
    })
    const pageSizeRequest = page.waitForRequest((request) => {
      const url = new URL(request.url())
      return (
        request.method() === 'GET' &&
        request.frame() === page.mainFrame() &&
        url.pathname === outboundPath &&
        !request.headers()['x-nerv-walkthrough-action'] &&
        url.searchParams.get('take') === String(expectedQuery.take)
      )
    }, 120_000)
    await expect(
      selectWmsPageWindow(page, { ...NERV_1571_WMS_PAGE_WINDOW_INPUT, take: expectedQuery.take }),
    ).resolves.toEqual({
      skip: 0,
      take: expectedQuery.take,
    })
    await pageSizeRequest
    const pageSizeTargetRequest = targetRequests.find((entry) => {
      const url = new URL(entry.request.url())
      return !entry.marked && url.searchParams.get('take') === String(expectedQuery.take)
    })
    expect(pageSizeTargetRequest).toBeDefined()
    await expect(page.locator('[aria-current="page"][aria-label^="第 "]')).toHaveAttribute(
      'aria-label',
      '第 1 页',
    )
  })

  test('低基数列表的默认页窗口不要求不存在的分页控件', async ({ page }) => {
    await page.setContent(`
      <base href="http://walkthrough.fixture/">
      <table aria-label="入库单列表">
        <tbody><tr><td>IN-WALK-001</td></tr></tbody>
      </table>
    `)

    await expect(
      selectWmsPageWindow(page, NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT, 2_000),
    ).resolves.toEqual({ skip: 0, take: 10 })
  })
})
