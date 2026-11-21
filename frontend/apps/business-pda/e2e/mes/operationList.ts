import { test, expect } from '@playwright/test'
import { routeBusinessConsoleApi } from '../fixtures'

export function registerOperationList() {
  test('任务列表壳：375×812 服务端筛选、20 条分页与返回状态恢复', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const requests: Array<{ skip: string | null; take: string | null; status: string | null }> = []
    const tasks = Array.from({ length: 45 }, (_, index) => ({
      operationTaskId: `OP-SHELL-${index + 1}`,
      workOrderId: `WO-SHELL-${index + 1}`,
      status: 'InProgress',
      operationSequence: index + 1,
      workCenterId: 'WC-A',
      qualityStatus: 'Pending',
    }))
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      const url = new URL(route.request().url())
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      requests.push({
        skip: url.searchParams.get('skip'),
        take: url.searchParams.get('take'),
        status: url.searchParams.get('status'),
      })
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { items: tasks.slice(skip, skip + take), total: tasks.length },
        }),
      })
    })

    await page.goto('/mes/operation')
    await expect(page.locator('[data-row]')).toHaveCount(20)
    expect(requests[0]).toMatchObject({ skip: '0', take: '20' })

    await page.getByRole('button', { name: '全部状态' }).click()
    await page.getByRole('button', { name: '进行中', exact: true }).click()
    await expect.poll(() => requests.at(-1)?.status).toBe('inProgress')

    const scroller = page.locator('[data-slot="pull-refresh"] .nv-m-pr-scroll')
    const firstPageMax = await scroller.evaluate((element) =>
      Math.max(0, element.scrollHeight - element.clientHeight),
    )
    await scroller.evaluate((element) => element.scrollTo({ top: element.scrollHeight }))
    await expect.poll(() => requests.some((request) => request.skip === '20')).toBe(true)
    await expect(page.locator('[data-row]')).toHaveCount(40)

    const deepTarget = await scroller.evaluate((element, target) => {
      element.scrollTo({ top: target })
      return element.scrollTop
    }, firstPageMax + 300)
    expect(deepTarget).toBeGreaterThan(firstPageMax)
    await expect
      .poll(() =>
        page.evaluate(() => {
          const state = JSON.parse(
            sessionStorage.getItem('nerv-iip.business-pda.task-list.mes-operation-tasks') ?? '{}',
          )
          return Number(state.scrollTop ?? 0)
        }),
      )
      .toBeGreaterThanOrEqual(deepTarget - 1)

    await page.goto('/me')
    requests.length = 0
    await page.goBack()
    await expect(page.getByRole('button', { name: '进行中' })).toBeVisible()
    await expect.poll(() => requests.some((request) => request.skip === '20')).toBe(true)
    await expect
      .poll(() => scroller.evaluate((element) => element.scrollTop))
      .toBeGreaterThanOrEqual(deepTarget - 1)
    const restored = await page.evaluate(() =>
      JSON.parse(
        sessionStorage.getItem('nerv-iip.business-pda.task-list.mes-operation-tasks') ?? '{}',
      ),
    )
    expect(restored.filters).toMatchObject({ status: 'inProgress' })
    expect(restored.scrollTop).toBeGreaterThanOrEqual(deepTarget - 1)
  })

  test('任务列表壳：375×812 深恢复次页失败停止自旋，显式重试后继续恢复', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await page.addInitScript(() => {
      sessionStorage.setItem(
        'nerv-iip.business-pda.task-list.mes-operation-tasks',
        JSON.stringify({ filters: { status: '' }, scrollTop: 2_200 }),
      )
    })
    const tasks = Array.from({ length: 60 }, (_, index) => ({
      operationTaskId: `OP-RETRY-${index + 1}`,
      workOrderId: `WO-RETRY-${index + 1}`,
      status: 'InProgress',
      operationSequence: index + 1,
      workCenterId: 'WC-A',
      qualityStatus: 'Pending',
    }))
    const attempts = new Map<number, number>()
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      const url = new URL(route.request().url())
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      attempts.set(skip, (attempts.get(skip) ?? 0) + 1)
      if (skip === 20 && attempts.get(skip) === 1) {
        return route.fulfill({
          status: 503,
          contentType: 'application/json',
          body: JSON.stringify({ message: 'persistent page failure' }),
        })
      }
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { items: tasks.slice(skip, skip + take), total: tasks.length },
        }),
      })
    })

    await page.goto('/mes/operation')
    await expect(page.getByTestId('task-list-load-error')).toBeVisible()
    await expect.poll(() => attempts.get(20) ?? 0).toBe(1)
    await page.waitForTimeout(600)
    expect(attempts.get(20)).toBe(1)

    await page.getByRole('button', { name: '重试', exact: true }).click()
    await expect.poll(() => attempts.get(20) ?? 0).toBe(2)
    await expect(page.getByTestId('task-list-load-error')).toBeHidden()
    await expect(page.locator('[data-row]')).toHaveCount(40)

    const scroller = page.locator('[data-slot="pull-refresh"] .nv-m-pr-scroll')
    await expect
      .poll(() => scroller.evaluate((element) => element.scrollTop))
      .toBeGreaterThanOrEqual(2_199)
    expect(attempts.get(20)).toBe(2)
    expect(attempts.get(40) ?? 0).toBe(0)
  })

  // #1297：PDA 侧作业范围闭环。后端不带 scopeKind/scopeId 只回授权清单、selectedScope 恒空，
  // 所以「操作工登录后能不能拿到工序/工单数据」完全取决于前端是否走完
  // 「清单 → 选择 → 带参重核验」。这条断言的是范围参数真的进了业务查询，且换范围会重取。
  test('作业范围：授权清单渲染成移动端选择器，切换后按新范围重取工序任务', async ({ page }) => {
    const requestedScopeIds: string[] = []
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      requestedScopeIds.push(new URL(route.request().url()).searchParams.get('scopeId') ?? '')
      return routeBusinessConsoleApi(route)
    })

    await page.goto('/mes/operation')
    await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()

    // 自动兜底选中清单第一项，并且工序任务查询只在带上该范围之后才发出。
    const scopeTrigger = page.getByTestId('mes-work-scope-select').locator('button').first()
    await expect(scopeTrigger).toHaveText('精加工一线（工作中心）')
    await expect.poll(() => requestedScopeIds).toEqual(['WC-A'])

    // 单手可达的下拉面板里换范围 → 重新核验 → 按新范围重取。
    await scopeTrigger.click()
    await page.getByRole('button', { name: '精加工二线（工作中心）', exact: true }).click()
    await expect(scopeTrigger).toHaveText('精加工二线（工作中心）')
    await expect.poll(() => requestedScopeIds).toEqual(['WC-A', 'WC-B'])
  })

  test('工序执行：列表 → 完成（二次确认）→ 成功结果', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const exactPairs: Array<{ workOrderId: string | null; operationTaskId: string | null }> = []
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      if (route.request().method() === 'GET') {
        const url = new URL(route.request().url())
        if (url.searchParams.get('operationTaskId')) {
          exactPairs.push({
            workOrderId: url.searchParams.get('workOrderId'),
            operationTaskId: url.searchParams.get('operationTaskId'),
          })
        }
      }
      return routeBusinessConsoleApi(route)
    })
    await page.goto('/mes/operation')

    await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()
    const headerBackButton = page.getByRole('button', { name: '返回', exact: true })
    const headerBackBox = await headerBackButton.boundingBox()
    expect(headerBackBox).not.toBeNull()
    expect(headerBackBox!.height).toBeGreaterThanOrEqual(44)

    // MES 工单号即工单 ID（WorkOrderIdValue），列表与详情直接展示工单号和工序任务号。
    const row = page.getByRole('button', {
      name: /WO-1 · 工序 10 执行中 · 工作中心 WC-A/,
    })
    await expect(row).toBeVisible()

    // 点行打开 BottomSheet 动作面板（teleport 到 body）。
    await row.click()
    await expect(page.getByText('WO-1', { exact: true })).toBeVisible()
    await expect(page.getByText('工序 10', { exact: true })).toBeVisible()
    await expect(page.getByText('OP-1', { exact: true })).toHaveCount(0)
    await expect(page.getByText('设备信息未提供', { exact: true })).toBeVisible()
    await expect(page.getByText('device-asset-cnc-01')).toHaveCount(0)
    await expect(page.getByText(/门禁评估/)).toBeVisible()
    // Running → 可用动作含「完成」（终态、destructive）。
    const completeBtn = page.getByTestId('action-complete')
    await expect(completeBtn).toBeVisible()

    // 第一次点「完成」进入二次确认，并未直接调用。
    await completeBtn.click()
    const confirmBtn = page.getByTestId('confirm-complete')
    await expect(confirmBtn).toBeVisible()

    // 确认完成 → 成功 Result。
    await confirmBtn.click()
    const result = page.locator('[data-result][data-status="success"]')
    await expect(result).toBeVisible()
    await expect(result.getByText('工序已完成')).toBeVisible()
    await expect(result.getByText('WO-1 · 工序 10')).toBeVisible()
    await expect
      .poll(() => exactPairs)
      .toContainEqual({
        workOrderId: 'WO-1',
        operationTaskId: 'OP-1',
      })
  })

  test('工序执行：375×812 阻塞任务展示前序/齐套/设备/质量原因且不能开始', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-2')

    await expect(page.getByRole('heading', { name: 'WO-1 · 工序 20', exact: true })).toBeVisible()
    await expect(page.getByText('工序 20', { exact: true })).toBeVisible()
    const blockers = page.getByTestId('operation-block-reasons')
    await expect(blockers).toContainText('当前不能开始')
    await expect(blockers).toContainText('前序工序尚未完成（工序 10）')
    await expect(blockers).toContainText('物料齐套')
    await expect(blockers).toContainText('设备')
    await expect(blockers).toContainText('质量')
    await expect(page.getByText('OP-TASK-0020', { exact: true })).toHaveCount(0)
    await expect(blockers).not.toContainText('OP-1')
    await expect(blockers).not.toContainText('OP-2')
    await expect(page.getByTestId('action-start')).toHaveCount(0)
    await expect(page.getByText('当前状态无可执行动作')).toBeVisible()
    await expect(page.locator('html')).toHaveJSProperty('scrollWidth', 375)
  })

  test('工序执行：accepted/unconfirmed 回执不显示成功并保留双强 ID', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await page.route(
      /\/api\/business-console\/v1\/mes\/operation-tasks\/OP-1\/complete(?:\?|$)/,
      async (route) => {
        const body = (route.request().postDataJSON() ?? {}) as { idempotencyKey?: string }
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              operationReceipt: {
                operationType: 'mes.operation-task.complete',
                authority: 'business-gateway',
                resourceType: 'mes.operation-task',
                resourceId: 'OP-1',
                idempotencyKey: body.idempotencyKey,
                outcome: 'accepted',
                stateConfirmed: false,
                readbackRequired: true,
                readbackMethod: 'GET',
                readbackPath:
                  '/api/business-console/v1/mes/operation-tasks?organizationId=org-001&environmentId=env-dev&operationTaskId=OP-1',
                changedAtUtc: '2026-08-02T08:35:00.000Z',
              },
            },
          }),
        })
      },
    )
    await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
    await page.getByTestId('action-complete').click()
    await page.getByTestId('confirm-complete').click()

    await expect(page.locator('[data-result][data-status="success"]')).toHaveCount(0)
    const errorResult = page.locator('[data-result][data-status="error"]')
    await expect(errorResult).toBeVisible()
    await expect(errorResult).toContainText('WO-1 · 工序 10')
    await expect(errorResult).toContainText('结果尚未核实')
  })

  test('工序执行：409 后权威刷新并撤销旧动作', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    let listReads = 0
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      if (route.request().method() === 'GET') listReads += 1
      return routeBusinessConsoleApi(route)
    })
    await page.route(
      /\/api\/business-console\/v1\/mes\/operation-tasks\/OP-1\/complete(?:\?|$)/,
      async (route) => {
        await route.fulfill({
          status: 409,
          contentType: 'application/json',
          body: JSON.stringify({ success: false, message: 'lifecycle-conflict', data: null }),
        })
      },
    )
    await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
    const readsBeforeAction = listReads
    await page.getByTestId('action-complete').click()
    await page.getByTestId('confirm-complete').click()

    await expect(page.getByText('状态已被其他操作更新')).toBeVisible()
    await expect(page.getByTestId('confirm-complete')).toHaveCount(0)
    await expect(page.getByTestId('retry-action')).toHaveCount(0)
    await expect.poll(() => listReads).toBeGreaterThan(readsBeforeAction)
  })

  test('工序执行：完成态服务端返回空动作时详情只读', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      if (route.request().method() !== 'GET') return routeBusinessConsoleApi(route)
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'OP-DONE',
                workOrderId: 'WO-DONE',
                workOrderNo: null,
                operationTaskNo: null,
                status: 'Completed',
                operationSequence: 30,
                workCenterId: 'WC-A',
                qualityStatus: 'Passed',
                allowedActions: [],
                blockReasons: [],
                evaluatedAtUtc: '2026-08-02T08:40:00.000Z',
              },
            ],
            total: 1,
          },
        }),
      })
    })
    await page.goto('/mes/operation?workOrderId=WO-DONE&operationTaskId=OP-DONE')

    await expect(
      page.getByRole('heading', { name: 'WO-DONE · 工序 30', exact: true }),
    ).toBeVisible()
    await expect(page.getByText('当前状态无可执行动作')).toBeVisible()
    await expect(page.locator('[data-testid^="action-"]')).toHaveCount(0)
  })

  test('工序执行：same-route query push 与 back/forward 始终只打开当前双强 ID', async ({
    page,
  }) => {
    await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
    await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('action-complete')).toBeVisible()
    await expect(page.getByTestId('action-start')).toHaveCount(0)

    await page.evaluate(async (target) => {
      const { router } = await import(/* @vite-ignore */ '/src/router/index.ts')
      await router.push(target)
    }, '/mes/operation?workOrderId=WO-2&operationTaskId=OP-3')
    await expect(page.getByRole('heading', { name: 'WO-2 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('action-start')).toBeVisible()
    await expect(page.getByTestId('action-complete')).toHaveCount(0)

    await page.goBack()
    await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('action-complete')).toBeVisible()
    await expect(page.getByTestId('action-start')).toHaveCount(0)
    await page.goForward()
    await expect(page.getByRole('heading', { name: 'WO-2 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('action-start')).toBeVisible()
    await expect(page.getByTestId('action-complete')).toHaveCount(0)
  })
}
