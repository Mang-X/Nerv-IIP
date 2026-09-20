import { expect, test } from '@playwright/test'

import {
  clickRefreshAndWaitForListResponse,
  clickTabAndConfirmUnmount,
  RequestFailureEvidenceTracker,
} from './issue1912-walkthrough-policy'

test.describe('walkthrough action and lifecycle boundary', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', '浏览器无关的 helper 只在 desktop 项目运行')
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
            document.querySelector('button').addEventListener('mousedown', () => {
              void fetch('${refreshPath}?revision=between')
            })
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

    const freshResponse = page.waitForResponse(
      (response) => new URL(response.url()).searchParams.get('revision') === 'fresh',
    )
    const refreshed = await clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)
    await freshResponse

    expect(new URL(refreshed.url()).searchParams.get('revision')).toBe('fresh')
    expect(refreshed.status()).toBe(200)
    expect(revisions).toEqual(['stale', 'between', 'fresh'])
    await expect(page.locator('#revision')).toHaveText('fresh')
  })

  test('刷新 action marker 拒绝点击后轮询、支持重复 refresh 且不选取最后到达的响应', async ({
    page,
  }) => {
    const refreshFixturePath = '/issue1912-refresh-ownership-fixture'
    const refreshPath = '/api/issue1912-refresh-ownership-list'
    const revisions: string[] = []
    const markersByRevision = new Map<string, string | undefined>()

    await page.route(`**${refreshFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <meta charset="utf-8">
          <button type="button">刷新</button>
          <script>
            let refreshCount = 0
            document.querySelector('button').addEventListener('click', () => {
              const revision = 'fresh-' + (++refreshCount)
              void fetch('${refreshPath}?revision=' + revision)
              setTimeout(() => void fetch('${refreshPath}?revision=after-' + refreshCount), 0)
            })
          </script>`,
      }),
    )
    await page.route(`**${refreshPath}*`, async (route) => {
      const request = route.request()
      const revision = new URL(request.url()).searchParams.get('revision') ?? ''
      revisions.push(revision)
      markersByRevision.set(revision, request.headers()['x-nerv-walkthrough-action'])
      if (revision.startsWith('after-')) await new Promise((resolve) => setTimeout(resolve, 100))
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ revision }),
      })
    })

    await page.goto(refreshFixturePath)
    const first = await clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)
    const second = await clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)

    expect(new URL(first.url()).searchParams.get('revision')).toBe('fresh-1')
    expect(new URL(second.url()).searchParams.get('revision')).toBe('fresh-2')
    expect(revisions).toEqual(['fresh-1', 'after-1', 'fresh-2', 'after-2'])
    expect(markersByRevision.get('fresh-1')).toBeTruthy()
    expect(markersByRevision.get('fresh-2')).toBeTruthy()
    expect(markersByRevision.get('fresh-1')).not.toBe(markersByRevision.get('fresh-2'))
    expect(markersByRevision.get('after-1')).toBeUndefined()
    expect(markersByRevision.get('after-2')).toBeUndefined()
  })

  test('刷新响应延迟时点击前 capture listener 的 zero-delay timer 仍在 action 外', async ({
    page,
  }) => {
    const refreshFixturePath = '/issue1912-refresh-after-window-fixture'
    const refreshPath = '/api/issue1912-refresh-after-window-list'
    const revisions: string[] = []
    const markersByRevision = new Map<string, string | undefined>()

    await page.route(`**${refreshFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <meta charset="utf-8">
          <button type="button">刷新</button>
          <script>
            const button = document.querySelector('button')
            document.addEventListener('click', () => {
              setTimeout(() => void fetch('${refreshPath}?revision=after-window'), 0)
            }, { capture: true })
            button.addEventListener('click', () => {
              void fetch('${refreshPath}?revision=fresh')
            })
          </script>`,
      }),
    )
    await page.route(`**${refreshPath}*`, async (route) => {
      const request = route.request()
      const revision = new URL(request.url()).searchParams.get('revision') ?? ''
      revisions.push(revision)
      markersByRevision.set(revision, request.headers()['x-nerv-walkthrough-action'])
      if (revision === 'fresh') await new Promise((resolve) => setTimeout(resolve, 100))
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ revision }),
      })
    })

    await page.goto(refreshFixturePath)
    const refreshed = await clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)

    expect(new URL(refreshed.url()).searchParams.get('revision')).toBe('fresh')
    expect(revisions).toEqual(expect.arrayContaining(['after-window', 'fresh']))
    expect(markersByRevision.get('after-window')).toBeUndefined()
    expect(markersByRevision.get('fresh')).toBeTruthy()
  })

  test('生成 client 的异步 auth 与 request interceptor 仍携带本次 click action context', async ({
    page,
  }) => {
    const refreshFixturePath = '/issue1912-refresh-async-client-fixture'
    const refreshPath = '/api/issue1912-refresh-async-client-list'
    let observedMarker: string | undefined

    await page.route(`**${refreshFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <meta charset="utf-8">
          <button type="button">刷新</button>
          <script>
            const microtaskHops = async count => {
              for (let index = 0; index < count; index++) await Promise.resolve()
            }
            const getToken = async () => {
              await microtaskHops(4)
              return 'token'
            }
            const setAuthParams = async options => {
              const token = await getToken()
              options.headers.set('Authorization', 'Bearer ' + token)
            }
            const requestInterceptor = async request => {
              await microtaskHops(4)
              return new Request(request, { headers: request.headers })
            }
            const generatedClientGet = async () => {
              const fetchImpl = globalThis.fetch
              const options = { headers: new Headers() }
              await setAuthParams(options)
              let request = new Request('${refreshPath}?revision=async-client', {
                headers: options.headers,
              })
              request = await requestInterceptor(request)
              await microtaskHops(4)
              await new Promise(resolve => setTimeout(resolve, 0))
              return fetchImpl(request)
            }
            document.querySelector('button').addEventListener('click', () => {
              void generatedClientGet()
            })
          </script>`,
      }),
    )
    await page.route(`**${refreshPath}*`, async (route) => {
      observedMarker = route.request().headers()['x-nerv-walkthrough-action']
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ revision: 'async-client' }),
      })
    })

    await page.goto(refreshFixturePath)
    const refreshed = await clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)

    expect(new URL(refreshed.url()).searchParams.get('revision')).toBe('async-client')
    expect(observedMarker).toBeTruthy()
  })

  test('刷新首个 marked 请求响应等待期间晚到 duplicate 时失败关闭', async ({ page }) => {
    const refreshFixturePath = '/issue1912-refresh-delayed-duplicate-fixture'
    const refreshPath = '/api/issue1912-refresh-delayed-duplicate-list'
    const markedRevisions: string[] = []

    await page.route(`**${refreshFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <meta charset="utf-8">
          <button type="button">刷新</button>
          <script>
            let clickCount = 0
            document.querySelector('button').addEventListener('click', () => {
              const revision = clickCount++ === 0 ? 'first' : 'late-duplicate'
              void fetch('${refreshPath}?revision=' + revision)
            })
          </script>`,
      }),
    )
    await page.route(`**${refreshPath}*`, async (route) => {
      const request = route.request()
      const revision = new URL(request.url()).searchParams.get('revision') ?? ''
      if (request.headers()['x-nerv-walkthrough-action']) markedRevisions.push(revision)
      if (revision === 'first') {
        await new Promise((resolve) => setTimeout(resolve, 30))
        await page.evaluate(() => document.querySelector('button')?.click())
        await new Promise((resolve) => setTimeout(resolve, 70))
      }
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ revision }),
      })
    })

    await page.goto(refreshFixturePath)
    await expect(clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)).rejects.toThrow(
      'more than one marked list request',
    )
    expect(markedRevisions).toEqual(['first', 'late-duplicate'])
  })

  test('刷新 action 同一次 click 发出多个同路径请求时失败关闭', async ({ page }) => {
    const refreshFixturePath = '/issue1912-refresh-duplicate-fixture'
    const refreshPath = '/api/issue1912-refresh-duplicate-list'
    const markedRevisions: string[] = []

    await page.route(`**${refreshFixturePath}*`, (route) =>
      route.fulfill({
        contentType: 'text/html',
        body: `<!doctype html>
          <meta charset="utf-8">
          <button type="button">刷新</button>
          <script>
            document.querySelector('button').addEventListener('click', () => {
              void fetch('${refreshPath}?revision=duplicate-1')
              void fetch('${refreshPath}?revision=duplicate-2')
            })
          </script>`,
      }),
    )
    await page.route(`**${refreshPath}*`, async (route) => {
      const request = route.request()
      const revision = new URL(request.url()).searchParams.get('revision') ?? ''
      if (request.headers()['x-nerv-walkthrough-action']) markedRevisions.push(revision)
      await route.fulfill({
        contentType: 'application/json',
        body: JSON.stringify({ revision }),
      })
    })

    await page.goto(refreshFixturePath)
    await expect(clickRefreshAndWaitForListResponse(page, refreshPath, 2_000)).rejects.toThrow(
      'more than one marked list request',
    )
    expect(markedRevisions).toEqual(['duplicate-1', 'duplicate-2'])
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
