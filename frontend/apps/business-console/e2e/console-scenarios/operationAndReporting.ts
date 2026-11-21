import { test } from './fixture'
import { expect } from '@playwright/test'

export function registerOperationAndReporting() {
  test('工序执行：队列渲染、可报工行直显「报工」按钮且能进报工弹窗', async ({ page }) => {
    // 工序任务数据来自共享 mock（op-1：Ready + WO-001 → 可报工）。
    await page.goto('/mes/operation-tasks', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工序执行' }),
    ).toBeVisible({ timeout: 15_000 })

    // 可报工行：行尾直显「报工」按钮（不必翻下拉）。
    const reportBtn = page.getByRole('button', { name: '报工' }).first()
    await expect(reportBtn).toBeVisible({ timeout: 15_000 })

    // 点「报工」→ 就地打开报工弹窗（不跳页），上下文随行带出（抓"点不了"+上下文丢失）。
    await reportBtn.click()
    const reportDialog = page.getByRole('dialog')
    await expect(reportDialog).toBeVisible({ timeout: 15_000 })
    await expect(page).toHaveURL(/\/mes\/operation-tasks/)
    await expect(reportDialog.locator('[data-slot="carried-context"]')).toContainText('WO-001')
  })

  test('报工记录：报工历史渲染产量、查看工单就地速览不跳页', async ({ page }) => {
    // 报工记录数据来自共享 mock（report-1：WO-001，良品 5）。
    await page.goto('/mes/production-reports', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '报工记录' }),
    ).toBeVisible({ timeout: 15_000 })
    // 产量可读（良品 5）。
    await expect(page.getByText(/良品\s*5/)).toBeVisible({ timeout: 15_000 })
    // 点工单号（WO-001）→ 就地弹出「工单速览」，URL 不变（不跳页、不打断操作）。
    await page.getByRole('button', { name: 'WO-001' }).first().click()
    await expect(page.getByRole('dialog').filter({ hasText: '工单速览' })).toBeVisible({
      timeout: 15_000,
    })
    await expect(page).toHaveURL(/\/mes\/production-reports/)
  })

  test('MES 实际工时读面在工序与报工页使用同一累计口径', async ({ page }, testInfo) => {
    await page.goto('/mes/operation-tasks', { waitUntil: 'domcontentloaded' })
    const operationActualHours = page.locator('[data-testid="actual-hours"]').first()
    await expect(operationActualHours).toContainText('人工')
    await expect(operationActualHours).toContainText('1.25 小时')
    await expect(operationActualHours).toContainText('机器')
    await expect(operationActualHours).toContainText('0.5 小时')
    await page.screenshot({ path: testInfo.outputPath('operation-actual-hours.png') })

    await page.goto('/mes/production-reports', { waitUntil: 'domcontentloaded' })
    const reportActualHours = page.locator('[data-testid="actual-hours"]').first()
    await expect(reportActualHours).toContainText('人工')
    await expect(reportActualHours).toContainText('2.75 小时')
    await expect(reportActualHours).toContainText('机器')
    await expect(reportActualHours).toContainText('1.5 小时')
    await page.screenshot({ path: testInfo.outputPath('report-actual-hours.png') })
  })

  test('完工入库：直接开为只读、回链工单；带工单上下文进来自动开登记弹窗', async ({ page }) => {
    // 直接打开：登记需从工单详情带上下文，按钮禁用并提示「从工单详情发起」。
    await page.goto('/mes/receipts', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '完工入库' }),
    ).toBeVisible({ timeout: 15_000 })
    await expect(page.getByRole('button', { name: 'WO-001' }).first()).toBeVisible({
      timeout: 15_000,
    })
    await expect(page.getByRole('button', { name: '从工单详情发起' })).toBeDisabled()

    // 带工单上下文进来（模拟工单完工跳转）→ 登记弹窗自动打开（抓跨页带参+可登记）。
    await page.goto('/mes/receipts?workOrderId=WO-001&skuId=sku-1&quantity=5', {
      waitUntil: 'domcontentloaded',
    })
    await expect(page.getByRole('dialog')).toBeVisible({ timeout: 15_000 })
  })

  test('在制跟踪：在制进度渲染、查看工单就地速览不跳页', async ({ page }) => {
    // 在制数据来自共享 mock（WO-001：已产 5 / 计划 10）。
    await page.goto('/mes/wip', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '在制跟踪' }),
    ).toBeVisible({ timeout: 15_000 })
    // 在制进度可读（已产 5）。
    await expect(page.getByText(/已产\s*5/)).toBeVisible({ timeout: 15_000 })
    // 点工单号（WO-001）→ 就地弹出「工单速览」，URL 不变（不跳页、不打断操作）。
    await page.getByRole('button', { name: 'WO-001' }).first().click()
    await expect(page.getByRole('dialog').filter({ hasText: '工单速览' })).toBeVisible({
      timeout: 15_000,
    })
    await expect(page).toHaveURL(/\/mes\/wip/)
  })
}
