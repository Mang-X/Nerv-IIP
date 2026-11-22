import { test, expect } from '@playwright/test'

export function registerOperationScope() {
  test('工序执行：完整双强 ID deep link 用 exact filter 命中被 20 个相似 ID 挤出首屏的任务', async ({
    page,
  }) => {
    const targetOperationTaskId = 'OP-COLLISION-2'
    const workOrderId = 'WO-COLLISION'
    const requests: Array<{ operationTaskId: string | null; keyword: string | null }> = []
    const collisions = Array.from({ length: 21 }, (_, index) => ({
      operationTaskId: `${targetOperationTaskId}-${String(index + 1).padStart(2, '0')}`,
      workOrderId,
      workOrderNo: null,
      operationTaskNo: null,
      status: 'Queued',
      operationSequence: index + 1,
      workCenterId: 'WC-A',
      qualityStatus: 'Pending',
      allowedActions: [],
    }))
    const target = {
      operationTaskId: targetOperationTaskId,
      workOrderId,
      workOrderNo: null,
      operationTaskNo: null,
      status: 'Queued',
      operationSequence: 99,
      workCenterId: 'WC-A',
      qualityStatus: 'Pending',
      allowedActions: [],
    }

    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      const url = new URL(route.request().url())
      const exact = url.searchParams.get('operationTaskId')
      const keyword = url.searchParams.get('keyword')
      requests.push({ operationTaskId: exact, keyword })
      const all = [...collisions, target]
      const matched = exact
        ? all.filter((task) => task.operationTaskId === exact)
        : keyword
          ? all.filter((task) => task.operationTaskId.includes(keyword))
          : all
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { items: matched.slice(skip, skip + take), total: matched.length },
        }),
      })
    })

    await page.goto(
      `/mes/operation?workOrderId=${workOrderId}&operationTaskId=${targetOperationTaskId}`,
    )

    await expect(
      page.getByRole('heading', { name: 'WO-COLLISION · 工序 99', exact: true }),
    ).toBeVisible()
    expect(requests).toContainEqual({ operationTaskId: targetOperationTaskId, keyword: null })
  })

  test('工序执行：固定双强 ID 切换 scope 后关闭旧对象并只从新 scope 响应重开', async ({ page }) => {
    let releaseScopeB!: () => void
    const scopeBReleased = new Promise<void>((resolve) => {
      releaseScopeB = resolve
    })
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      const scopeId = new URL(route.request().url()).searchParams.get('scopeId')
      if (scopeId === 'WC-B') await scopeBReleased
      const operationSequence = scopeId === 'WC-B' ? 30 : 10
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            items: [
              {
                operationTaskId: 'OP-1',
                workOrderId: 'WO-1',
                workOrderNo: null,
                operationTaskNo: null,
                status: 'InProgress',
                operationSequence,
                workCenterId: scopeId,
                qualityStatus: 'Pending',
              },
            ],
            total: 1,
          },
        }),
      })
    })

    try {
      await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toHaveCount(
        0,
      )

      const scopeTrigger = page.getByTestId('mes-work-scope-select').locator('button').first()
      await scopeTrigger.click()
      await page.getByRole('button', { name: '精加工二线（工作中心）', exact: true }).click()
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toHaveCount(
        0,
      )

      releaseScopeB()
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 30', exact: true })).toBeVisible()
    } finally {
      releaseScopeB()
    }
  })

  test('工序执行：固定双强 ID 在新 scope 缺失时关闭旧对象并 fail closed', async ({ page }) => {
    let releaseScopeB!: () => void
    const scopeBReleased = new Promise<void>((resolve) => {
      releaseScopeB = resolve
    })
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      const scopeId = new URL(route.request().url()).searchParams.get('scopeId')
      if (scopeId === 'WC-B') await scopeBReleased
      const items =
        scopeId === 'WC-B'
          ? []
          : [
              {
                operationTaskId: 'OP-1',
                workOrderId: 'WO-1',
                workOrderNo: null,
                operationTaskNo: null,
                status: 'InProgress',
                operationSequence: 10,
                workCenterId: 'WC-A',
                qualityStatus: 'Pending',
              },
            ]
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true, data: { items, total: items.length } }),
      })
    })

    try {
      await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toBeVisible()
      await page.keyboard.press('Escape')
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toHaveCount(
        0,
      )

      const scopeTrigger = page.getByTestId('mes-work-scope-select').locator('button').first()
      await scopeTrigger.click()
      await page.getByRole('button', { name: '精加工二线（工作中心）', exact: true }).click()
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toHaveCount(
        0,
      )

      releaseScopeB()
      await expect(page.getByTestId('operation-deep-link-message')).toContainText(
        '未在当前作业范围内找到指定工序任务',
      )
      await expect(page.getByRole('heading', { name: /WO-1 · 工序/ })).toHaveCount(0)
    } finally {
      releaseScopeB()
    }
  })

  test('工序执行：scope 快速切换后迟到的旧响应不能复活固定双强 ID 对象', async ({ page }) => {
    let releaseScopeA!: () => void
    let markScopeAStarted!: () => void
    const scopeAReleased = new Promise<void>((resolve) => {
      releaseScopeA = resolve
    })
    const scopeAStarted = new Promise<void>((resolve) => {
      markScopeAStarted = resolve
    })
    await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
      const scopeId = new URL(route.request().url()).searchParams.get('scopeId')
      if (scopeId === 'WC-A') {
        markScopeAStarted()
        await scopeAReleased
      }
      const items =
        scopeId === 'WC-A'
          ? [
              {
                operationTaskId: 'OP-1',
                workOrderId: 'WO-1',
                workOrderNo: null,
                operationTaskNo: null,
                status: 'InProgress',
                operationSequence: 10,
                workCenterId: 'WC-A',
                qualityStatus: 'Pending',
              },
            ]
          : []
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true, data: { items, total: items.length } }),
      })
    })

    try {
      await page.goto('/mes/operation?workOrderId=WO-1&operationTaskId=OP-1')
      await scopeAStarted

      const scopeTrigger = page.getByTestId('mes-work-scope-select').locator('button').first()
      await scopeTrigger.click()
      await page.getByRole('button', { name: '精加工二线（工作中心）', exact: true }).click()
      await expect(page.getByTestId('operation-deep-link-message')).toContainText(
        '未在当前作业范围内找到指定工序任务',
      )

      releaseScopeA()
      await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toHaveCount(
        0,
      )
      await expect(page.getByTestId('operation-deep-link-message')).toContainText(
        '未在当前作业范围内找到指定工序任务',
      )
    } finally {
      releaseScopeA()
    }
  })
}
