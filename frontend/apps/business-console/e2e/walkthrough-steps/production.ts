import {
  type WalkthroughContext,
  baseURL,
  QUANTITY,
  SALES_ORDER_NO,
  FINISHED_SKU,
  SITE_CODE,
  FINISHED_GOODS_LOCATION,
  queryPath,
} from './context'
import type { runPlanning } from './planning'
import {
  rowsOf,
  safeText,
  asRecord,
  dataOf,
  textOf,
  publicJson,
} from '../issue1912-walkthrough-evidence'
import { expect } from '@playwright/test'
import { runFinishedProduction } from '../issue1853-finished-production'
import { join } from 'node:path'

export async function runProduction(
  context: WalkthroughContext,
  workOrderIdentity: Awaited<ReturnType<typeof runPlanning>>,
) {
  const {
    browser,
    page,
    workerPassword,
    organizationId,
    environmentId,
    screenshotDirectory,
    evidenceDirectory,
  } = context
  const { call, pollRows, workerPollData, workerPollRows } = context.session
  const { setup, failedRequests, pageErrors, record } = context.ledger
  const { provePageSafely } = context.proofs
  const { workOrderId, workOrderNo } = workOrderIdentity

  const operatorId = 'user-emp-010'
  const operatorPage = await browser.newPage({ baseURL })
  operatorPage.on('response', async (response) => {
    const path = new URL(response.url()).pathname
    if (
      response.ok() &&
      response.request().method() === 'GET' &&
      ['work-orders', 'operation-tasks'].some((name) => path.endsWith('/mes/' + name))
    ) {
      const url = new URL(response.url())
      setup.push({
        actor: operatorId,
        path,
        scope: url.searchParams.get('scopeKind'),
        rows: rowsOf(await response.json()).map((row) => ({
          workOrderId: row.workOrderId,
          operationTaskId: row.operationTaskId,
          assignedUserId: row.assignedUserId,
        })),
      })
    }
    if (path.startsWith('/api/') && response.status() >= 400)
      failedRequests.push({ actor: operatorId, path, status: response.status() })
  })
  operatorPage.on('pageerror', (error) => pageErrors.push(safeText(error.message)))
  const loginOperator = async (target: typeof page, loginName: string, expectedId: string) => {
    await target.goto('/login')
    await target.getByLabel('登录名').fill(loginName)
    await target.getByLabel('密码').fill(workerPassword)
    const [response] = await Promise.all([
      target.waitForResponse((r) => new URL(r.url()).pathname === '/api/console/v1/auth/login'),
      target.getByRole('button', { name: '登录' }).click(),
    ])
    expect(response.status()).toBe(200)
    const principal = asRecord(asRecord(dataOf(await response.json())).principal)
    expect(principal).toMatchObject({ principalId: expectedId, organizationId, environmentId })
    return principal.permissionCodes
  }
  for (const [userId, name, idempotencyKey] of [
    [operatorId, '走查末道操作工', 'issue1912-final-operator'],
    ['user-emp-012', '走查未派工操作工', 'issue1912-unassigned-operator'],
  ] as const) {
    const worker = await call('POST', '/api/business-console/v1/master-data/workers', {
      organizationId,
      environmentId,
      userId,
      name,
      employmentStatus: 'active',
      idempotencyKey,
    })
    setup.push({ request: worker.summary, worker: worker.publicPayload })
  }
  let finalReportNo = ''
  const finishedProduction = await runFinishedProduction({
    browser,
    workOrderId,
    assignedPrincipalId: operatorId,
    finishAssignedTask: async (taskId) => {
      const permissions = await loginOperator(operatorPage, 'emp010', operatorId)
      const other = await browser.newPage({ baseURL })
      try {
        expect(await loginOperator(other, 'emp012', 'user-emp-012')).toEqual(permissions)
        const [tasks] = await Promise.all([
          other.waitForResponse((r) => {
            const url = new URL(r.url())
            return (
              url.pathname.endsWith('/mes/operation-tasks') &&
              url.searchParams.get('scopeKind') === 'self' &&
              url.searchParams.get('scopeId') === 'user-emp-012'
            )
          }),
          other.goto('/mes/operation-tasks'),
        ])
        expect(tasks.status()).toBe(200)
        expect(asRecord(dataOf(await tasks.json()))).toMatchObject({ total: 0, items: [] })
        await expect(other.getByText('0 个工序任务', { exact: true })).toBeVisible()
        const [detail] = await Promise.all([
          other.waitForResponse((r) => {
            const url = new URL(r.url())
            return (
              url.pathname ===
                '/api/business-console/v1/mes/work-orders/' + encodeURIComponent(workOrderId) &&
              url.searchParams.get('scopeKind') === 'self' &&
              url.searchParams.get('scopeId') === 'user-emp-012'
            )
          }),
          other.goto('/mes/work-orders/' + encodeURIComponent(workOrderId)),
        ])
        expect(detail.status()).toBe(403)
        setup.push({
          actor: 'user-emp-012',
          scope: 'self',
          workOrderId,
          taskId,
          listStatus: tasks.status(),
          taskCount: 0,
          detailStatus: detail.status(),
        })
      } finally {
        await other.close()
      }
      await operatorPage.getByRole('link', { name: '制造执行' }).first().click()
      await expect(
        operatorPage
          .getByRole('link')
          .filter({ has: operatorPage.getByText(workOrderNo, { exact: true }) }),
      ).toBeVisible()
      await operatorPage.getByRole('link', { name: '工序执行' }).first().click()
      const row = operatorPage.getByRole('row').filter({ hasText: workOrderNo })
      await expect(row).toHaveCount(1)
      await expect(row.getByTitle(taskId, { exact: true })).toBeVisible()
      await operatorPage.screenshot({
        path: join(screenshotDirectory, 'operator-final-task.png'),
      })
      await row.getByRole('button', { name: /工序任务操作/ }).click()
      const [start] = await Promise.all([
        operatorPage.waitForResponse((r) => new URL(r.url()).pathname.endsWith(`/${taskId}/start`)),
        operatorPage.getByRole('menuitem', { name: '开工' }).click(),
      ])
      expect(start.ok()).toBe(true)
      await row.getByRole('button', { name: '报工', exact: true }).click()
      const dialog = operatorPage.getByRole('dialog')
      await dialog.locator('#report-good').fill(String(QUANTITY))
      if ((await dialog.locator('#report-complete').getAttribute('aria-checked')) !== 'true')
        await dialog.locator('#report-complete').click()
      const [posted] = await Promise.all([
        operatorPage.waitForResponse(
          (r) =>
            r.request().method() === 'POST' &&
            new URL(r.url()).pathname.endsWith('/mes/production-reports'),
        ),
        dialog.getByRole('button', { name: '提交报工' }).click(),
      ])
      expect(posted.ok()).toBe(true)
      expect(posted.request().postDataJSON()).toMatchObject({
        workOrderId,
        operationTaskId: taskId,
        completesOperation: true,
      })
      const result = asRecord(dataOf(await posted.json()))
      finalReportNo = textOf(result.reportNo)
      expect(finalReportNo).not.toBe('')
      setup.push({
        actor: operatorId,
        taskId,
        workOrderId,
        reportNo: finalReportNo,
        httpStatus: posted.status(),
      })
      await operatorPage.getByRole('button', { name: '去完工入库' }).click()
      await expect(operatorPage.getByRole('dialog', { name: '登记完工入库' })).toBeVisible()
      return result
    },
    evidencePath: join(evidenceDirectory, 'nerv1853-finished-production.json'),
  })
  const productionReports = await pollRows(
    '/api/business-console/v1/mes/production-reports',
    { organizationId, environmentId, keyword: workOrderNo, skip: 0, take: 100 },
    (row) =>
      row.workOrderId === workOrderId &&
      row.reportNo === finalReportNo &&
      Boolean(row.producedLotNo),
    120_000,
  )
  const producedLotNo = textOf(productionReports.match.producedLotNo)
  const productionUi = await provePageSafely('mes-work-order-production', {
    route: '/mes/production-reports',
    listPath: '/api/business-console/v1/mes/production-reports',
    stableText: workOrderNo,
    emptyText: '还没有报工记录。报工后这里会出现对应记录，去工序执行报工。',
    screenshotName: '13-mes-production-reports.png',
  })
  record({
    node: 'mes-work-order-production',
    sourceObject: workOrderNo,
    downstreamObject: textOf(
      productionReports.match.reportNo ?? productionReports.match.productionReportId,
    ),
    stableKey: `${SALES_ORDER_NO} -> ${workOrderNo} -> ${producedLotNo}`,
    automationMode: 'mixed',
    request: null,
    responseOrLog: {
      reports: publicJson(productionReports.match),
      finalProduction: finishedProduction,
      ui: productionUi,
    },
    conclusion: 'runtime-confirmed',
    demoWording:
      'MES 真实工单的全部工序通过公开 start/report 生命周期完成，末道由 emp010 在工序执行页报工，系统生成成品批次；报工页面渲染工单业务号。',
    responsibilityIssue: null,
  })

  const sheet = operatorPage.getByRole('dialog', { name: '登记完工入库' })
  await expect(sheet.getByLabel('选择产出批次')).toContainText(producedLotNo)
  await sheet.getByLabel('入库数量').fill(String(QUANTITY))
  const unit = sheet.getByLabel('单位', { exact: true })
  if (!(await unit.innerText()).includes('件')) {
    await unit.click()
    await operatorPage.getByRole('option', { name: /件/ }).click()
  }
  const receiptPath = '/api/business-console/v1/mes/finished-goods-receipt-requests'
  const [submitted] = await Promise.all([
    operatorPage.waitForResponse(
      (response) =>
        response.request().method() === 'POST' && new URL(response.url()).pathname === receiptPath,
    ),
    sheet.getByRole('button', { name: '登记入库' }).click(),
  ])
  expect(submitted.ok()).toBe(true)
  const body = submitted.request().postDataJSON()
  expect(body).toMatchObject({
    organizationId,
    environmentId,
    workOrderId,
    skuId: FINISHED_SKU,
    producedLotNo,
    quantity: QUANTITY,
    uomCode: 'pcs',
  })
  const receiptRequestNo = textOf(asRecord(dataOf(await submitted.json())).requestNo)
  const receiptCommand = {
    actor: 'production-operator',
    principalId: operatorId,
    method: 'POST',
    path: receiptPath,
    status: submitted.status(),
    body: publicJson(body),
  }
  if (!receiptRequestNo)
    throw new Error(`Finished goods receipt for ${workOrderNo} did not return requestNo.`)
  const finishedReceiptRow = await pollRows(
    '/api/business-console/v1/mes/finished-goods-receipt-requests',
    { organizationId, environmentId, workOrderId, keyword: receiptRequestNo, skip: 0, take: 100 },
    (row) =>
      textOf(row.requestNo) === receiptRequestNo && textOf(row.producedLotNo) === producedLotNo,
    180_000,
  )
  const receiptPageUi = await provePageSafely('production-finished-goods-receipt', {
    route: '/mes/receipts',
    listPath: '/api/business-console/v1/mes/finished-goods-receipt-requests',
    stableText: receiptRequestNo,
    emptyText: '还没有完工入库登记。末道工序报完工后，在此把成品登记入库即会出现对应记录。',
    screenshotName: '14-finished-goods-receipt.png',
  })
  await operatorPage.goto('/mes/receipts')
  await expect(operatorPage.getByText(receiptRequestNo, { exact: true }).first()).toBeVisible()
  await operatorPage.screenshot({ path: join(screenshotDirectory, 'operator-receipt.png') })
  await operatorPage.close()
  const finishedInventory = await workerPollData(
    '/api/business-console/v1/inventory/availability',
    {
      organizationId,
      environmentId,
      skuCode: FINISHED_SKU,
      uomCode: 'pcs',
      siteCode: SITE_CODE,
      locationCode: FINISHED_GOODS_LOCATION,
      lotNo: producedLotNo,
      ownerType: 'company',
    },
    (data) => Number(data.availableQuantity ?? 0) >= QUANTITY,
    180_000,
  )
  const postedReceipt = await pollRows(
    '/api/business-console/v1/mes/finished-goods-receipt-requests',
    { organizationId, environmentId, workOrderId, keyword: receiptRequestNo, skip: 0, take: 100 },
    (row) =>
      row.requestNo === receiptRequestNo &&
      row.producedLotNo === producedLotNo &&
      row.receiptStatus === 'Posted' &&
      row.postedQuantity === QUANTITY &&
      Boolean(row.postedInventoryMovementId),
    180_000,
  )
  const finishedMovement = await workerPollRows(
    '/api/business-console/v1/inventory/movements',
    { organizationId, environmentId, sourceDocumentId: receiptRequestNo, page: 1, pageSize: 100 },
    (row) =>
      row.movementId === postedReceipt.match.postedInventoryMovementId &&
      row.sourceDocumentId === receiptRequestNo &&
      row.sourceService === 'business-mes' &&
      row.movementType === 'inbound' &&
      row.skuCode === FINISHED_SKU &&
      row.siteCode === SITE_CODE &&
      row.locationCode === FINISHED_GOODS_LOCATION &&
      row.lotNo === producedLotNo &&
      row.ownerType === 'company' &&
      row.quantity === QUANTITY,
  )
  record({
    node: 'production-finished-goods-receipt',
    sourceObject: textOf(
      productionReports.match.reportNo ?? productionReports.match.productionReportId,
    ),
    downstreamObject: receiptRequestNo,
    stableKey: `${workOrderNo} -> ${receiptRequestNo} -> ${producedLotNo}`,
    automationMode: 'manual',
    request: receiptCommand,
    responseOrLog: {
      firstRead: publicJson(finishedReceiptRow.match),
      receipt: publicJson(postedReceipt.match),
      ui: receiptPageUi,
    },
    conclusion: 'runtime-confirmed',
    demoWording:
      '最终报工通过公开完工入库请求登记固定批次，MES 完工入库页面以 HTTP 200 渲染 requestNo；随后 Inventory 公开可用量为正。',
    responsibilityIssue: null,
  })
  record({
    node: 'finished-goods-inventory',
    sourceObject: receiptRequestNo,
    downstreamObject: producedLotNo,
    stableKey: `${receiptRequestNo} -> ${SITE_CODE}/${FINISHED_GOODS_LOCATION}/${producedLotNo}`,
    automationMode: 'automatic',
    request: finishedInventory.call.summary,
    responseOrLog: {
      availability: publicJson(finishedInventory.data),
      receipt: publicJson(postedReceipt.match),
      movement: publicJson(finishedMovement.match),
      ui: await provePageSafely('finished-goods-inventory', {
        actor: 'wms-worker',
        route: queryPath('/inventory/availability', {
          skuCode: FINISHED_SKU,
          siteCode: SITE_CODE,
          locationCode: FINISHED_GOODS_LOCATION,
          lotNo: producedLotNo,
        }),
        listPath: '/api/business-console/v1/inventory/availability',
        stableText: FINISHED_SKU,
        selectOptions: [
          {
            label: '质量状态',
            option: '全部状态',
            expectedQuery: {
              organizationId,
              environmentId,
              skuCode: FINISHED_SKU,
              uomCode: 'pcs',
              siteCode: SITE_CODE,
              locationCode: FINISHED_GOODS_LOCATION,
              lotNo: producedLotNo,
              ownerType: 'owned',
            },
          },
        ],
        emptyText: '没有查到库存明细。换个物料、工厂或库位再查一次。',
        screenshotName: '15-finished-goods-inventory.png',
      }),
    },
    conclusion: 'runtime-confirmed',
    demoWording:
      '完工入库跨边界落到 Inventory 的固定成品批次分区，公开 availability 证明库存为正。',
    responsibilityIssue: null,
  })

  return producedLotNo
}
