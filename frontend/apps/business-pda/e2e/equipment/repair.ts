import { test, type Locator, expect } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures'

export function registerRepair() {
  test('报修：375×812 路由/扫码/设备搜索 → ActionSheet → 键盘态单次提交', async ({ page }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await page.setViewportSize({ width: 375, height: 812 })
    const postBodies: unknown[] = []
    const legacyPostPaths: string[] = []
    const reasonDirectoryRequests: URL[] = []
    page.on('request', (request) => {
      const url = new URL(request.url())
      const { pathname } = url
      if (pathname === '/api/business-console/v1/directories/downtime-reason') {
        reasonDirectoryRequests.push(url)
      }
      if (
        request.method() === 'POST' &&
        pathname === '/api/business-console/v2/maintenance/work-orders'
      ) {
        postBodies.push(request.postDataJSON())
      }
      // v1 创建入口不得再被 PDA 生产路径调用（#2970 验收条件）。
      if (
        request.method() === 'POST' &&
        pathname === '/api/business-console/v1/maintenance/work-orders'
      ) {
        legacyPostPaths.push(pathname)
      }
    })

    const expect48 = async (locator: Locator) => {
      const subpixelTolerance = 0.001
      const box = await locator.boundingBox()
      expect(
        box,
        `missing box for ${await locator.evaluate((element) => element.outerHTML)}`,
      ).not.toBeNull()
      expect(box!.height).toBeGreaterThanOrEqual(48 - subpixelTolerance)
      expect(box!.width).toBeGreaterThanOrEqual(48 - subpixelTolerance)
    }

    await page.goto('/equipment/repair?deviceAssetId=DEV-ROUTE&sourceAlarmId=ALM-9')
    await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
    await expect(page.getByTestId('device-trigger')).toContainText('DEV-ROUTE')
    await expect(page.getByTestId('device-trigger')).toContainText('由报警发起报修')
    await expect(page.getByTestId('device-input')).toHaveCount(0)
    await expect(page.locator('select')).toHaveCount(0)
    await expectNoHorizontalOverflow(page)

    // 首屏当前可见交互均为 ≥48px 命中盒；扫码以组件容器作为完整命中区域。
    const scan = page.locator('input[placeholder="扫描设备码"]')
    await expect48(scan.locator('..'))
    await expect48(page.getByTestId('device-trigger'))
    await expect48(page.getByTestId('priority-trigger'))
    await expect48(page.getByTestId('reason-trigger'))
    await expect48(page.getByTestId('submit'))

    // ActionSheet 三项及取消均为 48px；取消保持已选值。
    await page.getByTestId('priority-trigger').click()
    const prioritySheet = page.locator('[data-slot="mobile-sheet-content"]')
    await expect(prioritySheet).toBeVisible()
    for (const label of ['高', '中', '低', '取消']) {
      await expect48(prioritySheet.getByRole('button', { name: label, exact: true }))
    }
    await prioritySheet.getByRole('button', { name: '高', exact: true }).click()
    await expect(prioritySheet).toBeHidden()
    await page.getByTestId('priority-trigger').click()
    await prioritySheet.getByRole('button', { name: '取消', exact: true }).click()
    await expect(page.getByTestId('priority-trigger')).toContainText('高')

    // 停机原因来自权威 downtime-reason 目录：抽屉里只有目录条目和「不登记设备不可用」，
    // 没有任何自由文本入口；页面上也不存在 textarea。
    await expect(page.locator('textarea')).toHaveCount(0)
    await page.getByTestId('reason-trigger').click()
    const reasonSheet = page.locator('[data-slot="mobile-sheet-content"]')
    await expect(reasonSheet).toBeVisible()
    await expect(reasonSheet.locator('textarea')).toHaveCount(0)
    await expect48(page.getByTestId('reason-option-none'))
    await expect48(page.getByTestId('reason-option-Spindle-Noise'))
    await page.getByTestId('reason-option-Spindle-Noise').click()
    await expect(reasonSheet).toBeHidden()
    await expect(page.getByTestId('reason-trigger')).toContainText('主轴异响')

    // 报警路由设备可被扫码替换；已有优先级与已选停机原因保持不变。
    await scan.click()
    await scan.pressSequentially('DEV-SCAN')
    await scan.press('Enter')
    await expect(page.getByTestId('device-trigger')).toContainText('DEV-SCAN')
    await expect(page.getByTestId('priority-trigger')).toContainText('高')
    await expect(page.getByTestId('reason-trigger')).toContainText('主轴异响')

    // 再用现有 facade 的服务端 keyword 选择设备编码；请求保持 principal scope + 有界分页。
    await page.getByTestId('device-trigger').click()
    const deviceSheet = page.locator('[data-slot="mobile-sheet-content"]')
    const searchInput = deviceSheet.locator('input[type="search"]')
    await searchInput.fill('数控')
    await expect48(searchInput)
    // 清除按钮是 Vue <Transition> 进场的：enter-from 那一帧 `scale(0.6)` 会把 48px 量成
    // 28.8px。取一次性读数会随前置步骤耗时变红变绿，所以按最终稳定态轮询。
    const clearButton = deviceSheet.getByRole('button', { name: '清除' })
    await expect(clearButton).toBeVisible()
    // 高宽一次读完：分两次 poll 理论上可以在不同瞬间各自达标，凑不出"同一时刻同时达标"。
    await expect
      .poll(async () => {
        const box = await clearButton.boundingBox()
        return box && box.height >= 48 - 0.001 && box.width >= 48 - 0.001
      })
      .toBe(true)
    await expect48(deviceSheet.getByRole('button', { name: '取消', exact: true }))
    const keywordRequest = page.waitForRequest((request) => {
      const url = new URL(request.url())
      return (
        url.pathname === '/api/business-console/v1/master-data/device-assets' &&
        url.searchParams.get('keyword') === '数控'
      )
    })
    await searchInput.press('Enter')
    const requestUrl = new URL((await keywordRequest).url())
    expect(requestUrl.searchParams.get('organizationId')).toBe('org-001')
    expect(requestUrl.searchParams.get('environmentId')).toBe('env-dev')
    expect(requestUrl.searchParams.get('includeDisabled')).toBe('false')
    expect(requestUrl.searchParams.get('skip')).toBe('0')
    expect(requestUrl.searchParams.get('take')).toBe('20')
    const deviceOption = deviceSheet.getByRole('button', { name: /一号数控机床/ })
    await expect(deviceOption).toContainText('DEV-CNC-01')
    await expect(deviceOption).toContainText('WS-1 · LINE-A · ST-9')
    await expect48(deviceOption)
    await deviceOption.click()
    await expect(page.getByTestId('device-trigger')).toContainText('一号数控机床')
    await expect(page.getByTestId('device-trigger')).toContainText('DEV-CNC-01')

    // 仅属 mock Chromium 证据：缩短 viewport 模拟软键盘占位，不能代表 Android/iOS 真 IME。
    // 停机原因改成目录选择后，本页写面已不再有需要软键盘的自由文本框；这里保留窄视口
    // 断言的是提交按钮在被压缩的可视区内仍完整可点。
    await page.setViewportSize({ width: 375, height: 520 })
    const submit = page.getByTestId('submit')
    await submit.scrollIntoViewIfNeeded()
    const submitBox = await submit.boundingBox()
    expect(submitBox).not.toBeNull()
    expect(submitBox!.y + submitBox!.height).toBeLessThanOrEqual(520)
    await submit.click()

    // 单击只产生一次 create；显式换设备后旧报警来源被清除，服务端仍会做最终关联校验。
    await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
    await expect(page.getByText('报修已提交')).toBeVisible()
    await expect(page.getByTestId('view-created-work-order')).toHaveCount(0)
    expect(postBodies).toEqual([
      {
        deviceAssetId: 'DEV-CNC-01',
        priority: 'high',
        // 目录码原值上线：大小写未被改写，也没有回退成自由文本字段。
        assetUnavailableReasonCode: 'Spindle-Noise',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        openedBy: 'operator01',
        idempotencyKey: expect.any(String),
      },
    ])
    expect(legacyPostPaths).toEqual([])
    // 停机原因目录只按登录主体的 organization/environment 查询；别的租户/环境的码
    // 既不会被请求，也就不会出现在抽屉里（跨租户过滤的权威在网关，前端不另设过滤）。
    expect(reasonDirectoryRequests.length).toBeGreaterThan(0)
    for (const url of reasonDirectoryRequests) {
      expect(url.searchParams.get('organizationId')).toBe('org-001')
      expect(url.searchParams.get('environmentId')).toBe('env-dev')
    }
  })

  test('报修：匹配的非 GUID payload/receipt 也不能进入成功或工单核验态', async ({ page }) => {
    await page.route('**/api/business-console/v2/maintenance/work-orders', async (route) => {
      if (route.request().method() !== 'POST') return route.fallback()
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            workOrderId: 'WO-INVALID',
            operationReceipt: {
              operationType: 'maintenance.work-order.create',
              resourceType: 'maintenance-work-order',
              resourceId: 'WO-INVALID',
              outcome: 'confirmed',
              stateConfirmed: true,
              accepted: false,
              idempotencyKey: 'invalid-receipt',
            },
          },
        }),
      })
    })

    await page.goto('/equipment/repair?deviceAssetId=DEV-CNC-01')
    await page.getByTestId('priority-trigger').click()
    await page
      .locator('[data-slot="mobile-sheet-content"]')
      .getByRole('button', { name: '高', exact: true })
      .click()
    await page.getByTestId('submit').click()

    await expect(page.locator('[data-result][data-status="success"]')).toHaveCount(0)
    await expect(page.locator('[data-result][data-status="error"]')).toBeVisible()
    await expect(page.getByTestId('recheck-created-work-order-assignment')).toHaveCount(0)
  })

  test('报修：停机原因目录读失败时只给错误态，不回退自由文本且仍只能提交 null', async ({
    page,
  }) => {
    await page.route('**/api/business-console/v1/directories/downtime-reason**', (route) =>
      route.fulfill({
        status: 502,
        contentType: 'application/json',
        body: JSON.stringify({ success: false, message: 'downstream-unavailable', data: null }),
      }),
    )
    const postBodies: unknown[] = []
    page.on('request', (request) => {
      if (
        request.method() === 'POST' &&
        new URL(request.url()).pathname === '/api/business-console/v2/maintenance/work-orders'
      ) {
        postBodies.push(request.postDataJSON())
      }
    })

    await page.goto('/equipment/repair?deviceAssetId=DEV-CNC-01')
    await expect(page.getByTestId('reason-directory-blocked')).toBeVisible()
    await expect(page.getByTestId('reason-directory-blocked')).toContainText('不能手工填写原因')
    // 关键否定证据：整页没有任何自由文本输入可以承载原因。
    await expect(page.locator('textarea')).toHaveCount(0)

    await page.getByTestId('reason-trigger').click()
    const sheet = page.locator('[data-slot="mobile-sheet-content"]')
    await expect(sheet.getByTestId('reason-directory-error')).toBeVisible()
    // 排除搜索框自身（`input[type="search"]`）后，抽屉里不许再有任何可输入控件。
    await expect(sheet.locator('textarea, input:not([type="search"])')).toHaveCount(0)
    await expect(sheet.getByTestId('reason-retry')).toBeVisible()
    await sheet.getByTestId('reason-option-none').click()
    await expect(sheet).toBeHidden()

    await page.getByTestId('priority-trigger').click()
    await page
      .locator('[data-slot="mobile-sheet-content"]')
      .getByRole('button', { name: '高', exact: true })
      .click()
    await page.getByTestId('submit').click()
    await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()

    expect(postBodies).toHaveLength(1)
    expect((postBodies[0] as Record<string, unknown>).assetUnavailableReasonCode).toBeNull()
  })

  test('报修：清空停机原因搜索后列表恢复全量，不留"空输入框 + 过滤结果"的错位', async ({
    page,
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' })
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/equipment/repair?deviceAssetId=DEV-CNC-01')
    await page.getByTestId('reason-trigger').click()
    const sheet = page.locator('[data-slot="mobile-sheet-content"]')
    await expect(sheet.getByTestId('reason-option-Hyd-Leak_01')).toBeVisible()
    await expect(sheet.getByTestId('reason-option-Spindle-Noise')).toBeVisible()

    const search = sheet.locator('input[type="search"]')
    await search.fill('主轴')
    await search.press('Enter')
    await expect(sheet.getByTestId('reason-option-Hyd-Leak_01')).toHaveCount(0)
    await expect(sheet.getByTestId('reason-option-Spindle-Noise')).toBeVisible()

    // 点 X 清除：输入框空了，列表必须同时回到全量。否则空结果会显示"没有匹配的停机原因"，
    // 工人会以为本组织没配这个码，转而选"不登记设备不可用"——本该记录的停机原因就丢了。
    await sheet.getByRole('button', { name: '清除' }).click()
    await expect(search).toHaveValue('')
    await expect(sheet.getByTestId('reason-option-Hyd-Leak_01')).toBeVisible()
    await expect(sheet.getByTestId('reason-option-Spindle-Noise')).toBeVisible()
    await expect(sheet.getByTestId('reason-directory-state')).toHaveCount(0)
  })
}
