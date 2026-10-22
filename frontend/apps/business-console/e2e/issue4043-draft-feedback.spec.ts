import { expect, test, type Locator, type Page } from '@playwright/test'
// #4043 DomainInvariant：真实 DHTMLX 鼠标操作 + HTTP 冻结方案 fixture；不证明真实后端。
const utc = (hour: number) => new Date(Date.UTC(2026, 8, 30, hour)).toISOString()
const principal = {
  principalId: 'planner-1',
  principalType: 'User',
  loginName: 'planner',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.scheduling.plans.read',
    'business.scheduling.plans.manage',
    'business.mes.work-orders.read',
  ],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  principal,
}
const plan = {
  planId: 'APS-260930-001',
  status: 'generated',
  algorithmVersion: 'aps-lite-v1',
  assignments: [
    {
      assignmentId: 'A-10',
      orderId: 'WO-260930-001',
      operationId: '粗车',
      operationSequence: 10,
      resourceId: 'CNC-01',
      workCenterId: 'WC-TURN',
      startUtc: utc(8),
      endUtc: utc(10),
      isLocked: false,
    },
    {
      assignmentId: 'A-20',
      orderId: 'WO-260930-001',
      operationId: '精车',
      operationSequence: 20,
      resourceId: 'CNC-01',
      workCenterId: 'WC-TURN',
      startUtc: utc(10),
      endUtc: utc(12),
      isLocked: false,
    },
    {
      assignmentId: 'A-30',
      orderId: 'WO-260930-002',
      operationId: '粗车',
      operationSequence: 10,
      resourceId: 'CNC-01',
      workCenterId: 'WC-TURN',
      startUtc: utc(14),
      endUtc: utc(16),
      isLocked: false,
    },
  ],
  calendars: [
    {
      calendarId: 'DAY',
      resourceIds: ['CNC-01'],
      workCenterIds: ['WC-TURN'],
      shiftWindows: [
        { startUtc: utc(8), endUtc: utc(12), shiftCode: 'EARLY' },
        { startUtc: utc(12), endUtc: utc(18), shiftCode: 'MIDDLE' },
      ],
    },
  ],
  validationContext: {
    horizonStartUtc: utc(0),
    horizonEndUtc: utc(24),
    resources: [
      {
        resourceId: 'CNC-01',
        workCenterId: 'WC-TURN',
        calendarId: 'DAY',
        capacityUnits: 1,
        utilizationRate: 1,
      },
    ],
    operations: [
      {
        orderId: 'WO-260930-001',
        operationId: '粗车',
        predecessorOperationIds: [],
        dueUtc: utc(12),
        durationMinutes: 120,
        setupMinutes: 0,
        isFixed: false,
      },
      {
        orderId: 'WO-260930-001',
        operationId: '精车',
        predecessorOperationIds: ['粗车'],
        dueUtc: utc(12),
        durationMinutes: 120,
        setupMinutes: 0,
        isFixed: false,
      },
      {
        orderId: 'WO-260930-002',
        operationId: '粗车',
        predecessorOperationIds: [],
        dueUtc: utc(16),
        durationMinutes: 120,
        setupMinutes: 0,
        isFixed: false,
      },
    ],
    fixedReservations: [],
  },
}
async function drag(page: Page, bar: Locator, shiftInBarWidths: number, resize = false) {
  // Select 关闭动画期间 body 暂停命中，真实鼠标操作需等待输入边沿。
  await expect(page.locator('body')).toHaveCSS('pointer-events', 'auto')
  await bar.scrollIntoViewIfNeeded()
  const box = (await bar.boundingBox())!
  await bar.hover({ position: { x: box.width / 3, y: box.height * 0.8 } })
  const handle = resize ? await bar.locator('.gantt_task_drag.task_right').boundingBox() : null
  const x = resize ? handle!.x + 1 : box.x + box.width / 3
  const y = resize ? handle!.y + handle!.height - 2 : box.y + box.height * 0.8
  if (resize)
    expect(
      await page.evaluate(
        ({ x, y }) =>
          Boolean(document.elementFromPoint(x, y)?.closest('.gantt_task_drag.task_right')),
        { x, y },
      ),
    ).toBe(true)
  await page.mouse.move(x, y)
  await page.mouse.down()
  await page.mouse.move(x + box.width * shiftInBarWidths, y, { steps: 12 })
  await page.mouse.up()
}

test('move、资源 resize、表格编辑即时反馈并随撤销重做清除', async ({ page, isMobile }) => {
  test.skip(isMobile, '本例验证桌面计划员鼠标操作')
  let revisions = 0
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route('**/api/console/v1/**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: new URL(route.request().url()).pathname.endsWith('/me') ? principal : session,
      },
    }),
  )
  await page.route('**/api/business-console/v1/**', async (route) => {
    const pathname = new URL(route.request().url()).pathname
    let data: unknown = { items: [], total: 0 }
    if (pathname.endsWith('/me/work-context'))
      data = {
        authorizedScopes: [{ kind: 'work-center', id: 'WC-TURN', displayName: '车削中心' }],
        selectedScope: new URL(route.request().url()).searchParams.has('scopeId')
          ? { kind: 'work-center', id: 'WC-TURN' }
          : null,
      }
    else if (pathname.endsWith('/mes/work-orders'))
      data = {
        items: [
          {
            workOrderId: 'WO-260930-001',
            productionVersionId: 'PV-1',
            status: 'released',
            productName: '主轴',
            priority: 100,
            dueUtc: utc(12),
          },
        ],
        total: 1,
      }
    else if (pathname.endsWith('/scheduling/workbench/plans')) data = plan
    else if (pathname.includes('/scheduling/plans/APS-260930-001')) data = plan
    if (pathname.endsWith('/revisions')) revisions++
    await route.fulfill({ json: { success: true, data } })
  })
  await page.goto('/scheduling')
  await expect(page.getByRole('button', { name: '全部加入', exact: true })).toBeEnabled()
  await page.getByRole('button', { name: '全部加入', exact: true }).click()
  await page.getByRole('button', { name: '生成首版', exact: true }).click()
  const board = page.getByTestId('scheduling-draft-board')
  await expect(board.locator('[data-engine="dhtmlx"]')).toBeVisible()
  await board.getByRole('combobox', { name: '时间刻度' }).click()
  await page.getByRole('option', { name: '小时', exact: true }).click()
  const bar = board.locator('.gantt_task_line[task_id="A-20"]')
  await expect(bar).toBeVisible()
  await drag(page, bar, -1)
  const detail = board.getByTestId('scheduling-draft-task-detail')
  await expect(detail).toContainText('前序倒置')
  await expect(detail).toContainText('占用冲突')
  await expect(detail).toContainText('提前')
  await page.screenshot({ path: test.info().outputPath('gantt-move.png'), fullPage: true })
  await page.getByRole('button', { name: '撤销', exact: true }).click()
  await expect(detail).not.toContainText('前序倒置')
  await page.getByRole('button', { name: '重做', exact: true }).click()
  await expect(detail).toContainText('前序倒置')
  await page.getByRole('button', { name: '撤销', exact: true }).click()
  await board.getByRole('tab', { name: '资源排产板', exact: true }).click()
  await expect(bar).toBeVisible()
  // DHTMLX evaluation 的关闭按钮位于 closed shadow root；正常点击通知右上角。
  const evaluationNotice = board.locator('[data-view="resource"] > div[style*="z-index: 3"]')
  const noticeBox = (await evaluationNotice.boundingBox())!
  await evaluationNotice.click({ position: { x: noticeBox.width - 10, y: 10 } })
  await drag(page, bar, 2.5, true)
  await expect(detail).toContainText('占用冲突')
  await expect(detail).toContainText('延期')
  await page.screenshot({ path: test.info().outputPath('resource-resize.png'), fullPage: true })
  await board.getByRole('tab', { name: '表格编辑', exact: true }).click()
  const row = board.locator('tbody tr').filter({ hasText: 'WO-260930-001 · 精车' })
  await row.locator('input').nth(1).fill(utc(19))
  await expect(row).toContainText('日历外')
  await expect(row).toContainText('延期 420 分钟')
  await row.locator('input').nth(1).fill(utc(12))
  await expect(row).toContainText('按期')
  await expect(row).not.toContainText('日历外')
  await page.screenshot({ path: test.info().outputPath('table-restored.png'), fullPage: true })
  expect(revisions).toBe(0)
})
