import { expect, test, type Locator } from '@playwright/test'
import {
  expectNoHorizontalOverflow,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

// 网关 Mock + 已登录主体（含 org/env + loginName，见 fixtures.principal）。
test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

test('报修：375×812 路由/扫码/设备搜索 → ActionSheet → 键盘态单次提交', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.setViewportSize({ width: 375, height: 812 })
  const postBodies: unknown[] = []
  page.on('request', (request) => {
    const { pathname } = new URL(request.url())
    if (
      request.method() === 'POST' &&
      pathname === '/api/business-console/v1/maintenance/work-orders'
    ) {
      postBodies.push(request.postDataJSON())
    }
  })

  const expect48 = async (locator: Locator) => {
    const box = await locator.boundingBox()
    expect(
      box,
      `missing box for ${await locator.evaluate((element) => element.outerHTML)}`,
    ).not.toBeNull()
    expect(box!.height).toBeGreaterThanOrEqual(48)
    expect(box!.width).toBeGreaterThanOrEqual(48)
  }

  await page.goto('/equipment/repair?deviceAssetId=DEV-ROUTE&sourceAlarmId=ALM-9')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-ROUTE')
  await expect(page.getByTestId('device-trigger')).toContainText('报警上下文 · ALM-9')
  await expect(page.getByTestId('device-input')).toHaveCount(0)
  await expect(page.locator('select')).toHaveCount(0)
  await expectNoHorizontalOverflow(page)

  // 首屏当前可见交互均为 ≥48px 命中盒；扫码以组件容器作为完整命中区域。
  const scan = page.locator('input[placeholder="扫描设备码"]')
  await expect48(scan.locator('..'))
  await expect48(page.getByTestId('device-trigger'))
  await expect48(page.getByTestId('priority-trigger'))
  await expect48(page.getByTestId('reason-input'))
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

  // 报警路由设备可被扫码替换；已有优先级与自由文本描述保持不变。
  await page.getByTestId('reason-input').fill('主轴异响，无法运转')
  await scan.click()
  await scan.pressSequentially('DEV-SCAN')
  await scan.press('Enter')
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-SCAN')
  await expect(page.getByTestId('priority-trigger')).toContainText('高')
  await expect(page.getByTestId('reason-input')).toHaveValue('主轴异响，无法运转')

  // 再用现有 facade 的服务端 keyword 选择稳定 ID；请求保持 principal scope + 有界分页。
  await page.getByTestId('device-trigger').click()
  const deviceSheet = page.locator('[data-slot="mobile-sheet-content"]')
  const searchInput = deviceSheet.locator('input[type="search"]')
  await searchInput.fill('数控')
  await expect48(searchInput)
  await expect48(deviceSheet.getByRole('button', { name: '清除' }))
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
  await expect(deviceOption).toContainText('CNC-01')
  await expect(deviceOption).toContainText('WS-1 · LINE-A · ST-9')
  await expect48(deviceOption)
  await deviceOption.click()
  await expect(page.getByTestId('device-trigger')).toContainText('一号数控机床')
  await expect(page.getByTestId('device-trigger')).toContainText('CNC-01')

  // 仅属 mock Chromium 证据：缩短 viewport 模拟软键盘占位，不能代表 Android/iOS 真 IME。
  await page.setViewportSize({ width: 375, height: 520 })
  const reason = page.getByTestId('reason-input')
  await reason.focus()
  await reason.fill('主轴异响，无法运转')
  const submit = page.getByTestId('submit')
  await submit.scrollIntoViewIfNeeded()
  const submitBox = await submit.boundingBox()
  expect(submitBox).not.toBeNull()
  expect(submitBox!.y + submitBox!.height).toBeLessThanOrEqual(520)
  await submit.click()

  // 单击只产生一次 create；设备 ID 必须是 facade 返回的强 ID，报警 ID 保持 route-only。
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('报修已提交')).toBeVisible()
  expect(postBodies).toEqual([
    {
      deviceAssetId: 'device-asset-cnc-01',
      priority: 'high',
      assetUnavailableReason: '主轴异响，无法运转',
      sourceAlarmId: 'ALM-9',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      openedBy: 'operator01',
      idempotencyKey: expect.any(String),
    },
  ])
})

test('点检：选保养计划 → 选「通过」→ 提交 → 成功 Result', async ({ page }) => {
  await page.goto('/equipment/inspect')
  await expect(page.getByRole('heading', { name: '点检', exact: true })).toBeVisible()

  // 选择保养计划（PM-001 ← PLAN-1）
  await page.getByText('PM-001').click()
  // 选结果「通过」（pass → 通过）
  await page.getByTestId('result-pass').click()

  await page.getByTestId('submit').click()

  // 成功离场态（POST inspections → { inspectionId: 'INS-new' }）
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('点检已记录')).toBeVisible()
})

// MAN-458 #812：数字键盘录入（含 ± 负号）+ 超差即时警示 + 提交前汇总确认。真实 Chromium /
// Pixel 5 视口验证 jsdom 测不到的：Teleport 键盘浮层、计算样式红警示、触点尺寸、ScanBar 抢焦。
test('点检：数字键盘录入（含负号）+ 超差警示 + 提交确认', async ({ page }) => {
  // 键盘/弹窗过渡置 none：组件 @media(prefers-reduced-motion) 走 transition:none，
  // Teleport + Transition 的离场即时移除，消除 headless 下 transitionend 滞留（测行为非动画）。
  await page.emulateMedia({ reducedMotion: 'reduce' })

  await page.goto('/equipment/inspect')
  await page.getByText('PM-001').click()
  await page.getByTestId('result-pass').click()

  // 特性 + 单位：**真实 tap + fill**。文本获焦时页面停用 ScanBar 回焦（focusin），故正常录入
  // 不再被 ScanBar 抢走（#812 戴手套可完成录入的核心验收，不再靠原生 setter 假绿）。
  const characteristic = page.getByTestId('measurement-characteristic')
  await characteristic.click()
  await characteristic.fill('轴承温度')
  await expect(characteristic).toHaveValue('轴承温度')
  const uom = page.getByTestId('measurement-uom')
  await uom.click()
  await uom.fill('C')
  await expect(uom).toHaveValue('C')

  // 数字键盘录入（只读 Cell 触发，防系统键盘）：下限 0 / 上限 70 / 测量值 -80（± 负号 →
  // 低于下限超差）。键盘是底部 sheet + fixed inset-0 背板：字段间须「完成」收起再点下一格。
  const keyboard = page.locator('[data-slot="number-keyboard"]')
  const enterViaKeyboard = async (cell: string, digits: string) => {
    await page.getByTestId(cell).click()
    await expect(keyboard).toBeVisible()
    for (const d of digits) {
      await keyboard.getByRole('button', { name: d, exact: true }).click()
    }
  }
  const closeKeyboard = async () => {
    await keyboard.getByRole('button', { name: '完成' }).last().click()
    await expect(keyboard).toBeHidden()
  }

  await enterViaKeyboard('measurement-lower', '0')
  // 戴手套触点 ≥44px：数字键（键盘开着时量）+ 提交动作「完成」键（删除头部小按钮后仅剩
  // 底部大键，此前 E2E 只量数字键与 Cell、漏了提交动作，本处补上）。
  const digitBox = await keyboard.getByRole('button', { name: '8', exact: true }).boundingBox()
  expect(digitBox!.height).toBeGreaterThanOrEqual(44)
  expect(digitBox!.width).toBeGreaterThanOrEqual(44)
  const doneButtons = await keyboard.getByRole('button', { name: '完成' }).all()
  expect(doneButtons).toHaveLength(1)
  const doneBox = await doneButtons[0].boundingBox()
  expect(doneBox!.height).toBeGreaterThanOrEqual(44)
  expect(doneBox!.width).toBeGreaterThanOrEqual(44)
  await closeKeyboard()

  await enterViaKeyboard('measurement-upper', '70')
  await closeKeyboard()

  // 测量值：± → 8 → 0 = -80（负号回归覆盖；-80 < 下限 0 → 超差）。
  await page.getByTestId('measurement-value').click()
  await expect(keyboard).toBeVisible()
  await keyboard.getByRole('button', { name: '正负号' }).click()
  await keyboard.getByRole('button', { name: '8', exact: true }).click()
  await keyboard.getByRole('button', { name: '0', exact: true }).click()
  await closeKeyboard()
  await expect(page.getByTestId('measurement-value-text')).toHaveText('-80')

  // 测量值 Cell 触点 ≥44px。
  const cellBox = await page.getByTestId('measurement-value').boundingBox()
  expect(cellBox!.height).toBeGreaterThanOrEqual(44)

  // 超差即时警示：红标 + 数值变红 + 规格公差呈现；移动视口无横向溢出。
  await expect(page.getByTestId('out-of-tolerance')).toBeVisible()
  await expect(page.getByTestId('measurement-value-text')).toHaveClass(/text-destructive/)
  await expect(page.getByTestId('spec-range')).toHaveText('0 ~ 70 C')
  await expectNoHorizontalOverflow(page)

  // 提交 → 超差汇总确认「1 项测量值超差」→ 仍要提交 → 成功离场。
  await page.getByTestId('submit').click()
  const dialog = page.locator('[data-slot="mobile-dialog-content"]')
  await expect(dialog).toContainText('1 项测量值超差')
  await dialog.getByRole('button', { name: '仍要提交' }).click()
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('点检已记录')).toBeVisible()
})

test('报警 → 报修穿透：行详情「去报修」带 deviceAssetId + sourceAlarmId 跳报修页', async ({
  page,
}) => {
  await page.goto('/equipment/alarms')
  await expect(page.getByRole('heading', { name: '查看报警' })).toBeVisible()

  // 报警行渲染：设备 + 报警码 + 级别中文（严重，而非工程语言 'critical'）
  await expect(page.getByText('DEV-A · 报警码 E-101')).toBeVisible()
  await expect(page.getByText('严重')).toBeVisible()
  await expect(page.getByText('critical')).toHaveCount(0)

  // 去报修承载在行详情抽屉内（MAN-456 从行内移入详情）：先开详情再点。
  await page.getByTestId('detail-ALM-1').click()
  await page.getByTestId('repair-ALM-1').click()
  await expect(page).toHaveURL(/\/equipment\/repair\?/)
  const url = new URL(page.url())
  expect(url.pathname).toBe('/equipment/repair')
  expect(url.searchParams.get('deviceAssetId')).toBe('DEV-A')
  expect(url.searchParams.get('sourceAlarmId')).toBe('ALM-1')

  // 穿透后报修页设备已预填
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-A')
  await expect(page.getByTestId('device-trigger')).toContainText('报警上下文 · ALM-1')
})

test('首页 → 报修：点应用墙「报修」跳 /equipment/repair', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('home-name')).toBeVisible()

  await page.getByRole('button', { name: '报修' }).click()
  await expect(page).toHaveURL('/equipment/repair')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
})
