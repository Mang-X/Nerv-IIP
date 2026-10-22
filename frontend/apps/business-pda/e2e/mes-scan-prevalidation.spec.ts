import { expect, test, type Page, type Route } from '@playwright/test'

import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

function resolveEnvelope(
  status: string,
  candidates: Array<Record<string, unknown>> = [],
  reasonCode: string | null = null,
) {
  return {
    success: true,
    data: {
      status,
      reasonCode,
      candidates: candidates.map((candidate) => ({
        authority: 'barcode-label',
        source: 'barcode-label-print-item',
        observedAtUtc: '2026-08-28T10:00:00Z',
        ...candidate,
      })),
      total: candidates.length,
    },
  }
}

async function fulfillJson(route: Route, body: unknown, status = 200) {
  await route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })
}

async function scan(page: Page, value: string) {
  const input = page.locator('[data-testid="mes-scan-prevalidation"] input').last()
  await input.fill(value)
  await input.press('Enter')
}

test('MES 四个现场页面在 375×812 明确呈现歧义、未知、不支持与无权状态', async ({ page }) => {
  await page.route('**/api/business-console/v1/barcode/resolve', async (route) => {
    const { scannedValue } = route.request().postDataJSON() as { scannedValue: string }
    if (scannedValue === 'AMBIGUOUS') {
      return fulfillJson(
        route,
        resolveEnvelope(
          'ambiguous',
          [
            { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } },
            { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-2' } },
          ],
          'multiple-source-documents',
        ),
      )
    }
    const outcomes: Record<string, { status: string; reasonCode: string }> = {
      UNKNOWN: { status: 'unknown', reasonCode: 'managed-label-not-found' },
      UNSUPPORTED: { status: 'unsupported', reasonCode: 'resolved-object-type-unsupported' },
      FORBIDDEN: { status: 'forbidden', reasonCode: 'label-voided' },
    }
    const outcome = outcomes[scannedValue]!
    return fulfillJson(route, resolveEnvelope(outcome.status, [], outcome.reasonCode))
  })

  const scenarios = [
    { path: '/mes/issue', value: 'AMBIGUOUS', message: '找到多个候选，请手动选择' },
    { path: '/mes/operation', value: 'UNKNOWN', message: '未找到' },
    { path: '/mes/report', value: 'UNSUPPORTED', message: '当前页面不支持使用该对象' },
    { path: '/mes/receipt', value: 'FORBIDDEN', message: '当前账号无权解析或预校验' },
  ]

  for (const scenario of scenarios) {
    await page.goto(scenario.path)
    await scan(page, scenario.value)
    await expect(page.getByTestId('mes-scan-status')).toContainText(scenario.message)
    await expect(page.locator('html')).toHaveJSProperty('scrollWidth', 375)
  }
})

test('报工区分未找到与已识别类型不支持，拒绝结果不写入且支持工序仍经过预校验', async ({ page }) => {
  let productionReportWrites = 0
  let materialPrevalidations = 0
  const contextRequests: Array<Record<string, unknown>> = []
  await page.route('**/api/business-console/v1/mes/production-reports', (route) => {
    if (route.request().method() !== 'POST') return route.fallback()
    productionReportWrites += 1
    return fulfillJson(route, { success: false, message: '报工写入不应发生。' }, 400)
  })
  await page.route('**/api/business-console/v1/mes/material-scan-prevalidation', (route) => {
    materialPrevalidations += 1
    return fulfillJson(route, { success: false, message: '不应调用物料预校验。' }, 400)
  })
  await page.route('**/api/business-console/v1/mes/context-scan-prevalidation', (route) => {
    const request = route.request().postDataJSON() as Record<string, unknown>
    contextRequests.push(request)
    return fulfillJson(route, {
      success: true,
      data: {
        workOrderId: 'WO-1',
        operationTaskId: 'OP-1',
        objectType: 'operationTask',
        scannedObjectId: 'OP-1',
        decision: 'accepted',
        reasonCode: 'operation-task-scan-accepted',
        evaluatedAtUtc: '2026-08-28T10:00:00Z',
      },
    })
  })
  const outcomes = [
    {
      value: 'FGAWO9990001',
      status: 'unknown',
      reasonCode: 'managed-label-not-found',
      message: '未找到',
    },
    {
      value: 'WO-NOT-EXIST-3869',
      status: 'unsupported',
      reasonCode: 'barcode-format-unsupported',
      message: '未找到',
    },
    {
      value: 'OTHER-TYPE-LABEL',
      status: 'unsupported',
      reasonCode: 'resolved-object-type-unsupported',
      message: '不支持',
    },
    {
      value: 'VOIDED-LABEL',
      status: 'forbidden',
      reasonCode: 'label-voided',
      message: '无权',
    },
    {
      value: 'LOCATION-LABEL',
      status: 'resolved',
      reasonCode: null,
      message: '不支持',
      candidates: [
        { objectType: 'inventory-location', strongIds: { inventoryLocationId: 'LOC-1' } },
      ],
    },
  ]
  await page.route('**/api/business-console/v1/barcode/resolve', (route) => {
    const { scannedValue } = route.request().postDataJSON() as { scannedValue: string }
    if (scannedValue === 'OP-LABEL') {
      return fulfillJson(
        route,
        resolveEnvelope('resolved', [
          {
            objectType: 'mes-operation',
            strongIds: { workOrderId: 'WO-1', operationTaskId: 'OP-1' },
          },
        ]),
      )
    }
    const outcome = outcomes.find((item) => item.value === scannedValue)!
    return fulfillJson(
      route,
      resolveEnvelope(outcome.status, outcome.candidates, outcome.reasonCode),
    )
  })

  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByTestId('good-quantity')).toBeVisible()
  await page.getByTestId('good-quantity').fill('1')
  const status = page.getByTestId('mes-scan-status')
  for (const outcome of outcomes) {
    await scan(page, outcome.value)
    await expect(status).toContainText(outcome.message)
    if (outcome.message === '未找到') {
      await expect(status).not.toContainText('已识别')
    } else {
      await expect(status).not.toContainText('未找到')
    }
    await expect(page.getByTestId('submit-report')).toBeDisabled()
    await expect(page).toHaveURL(/\/mes\/report\?workOrderId=WO-1&operationTaskId=OP-1$/)
    expect(contextRequests).toHaveLength(0)
    expect(materialPrevalidations).toBe(0)
    expect(productionReportWrites).toBe(0)
  }

  await scan(page, 'OP-LABEL')
  await expect(status).toContainText('已通过当前工单工序预校验')
  expect(contextRequests).toEqual([
    {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'WO-1',
      operationTaskId: 'OP-1',
      objectType: 'operationTask',
      scannedObjectId: 'OP-1',
    },
  ])
  expect(materialPrevalidations).toBe(0)
  expect(productionReportWrites).toBe(0)
})

test('报工扫码只采用当前 pair 的服务端预校验，并显示过期、错配与来源失败', async ({ page }) => {
  let productionReportWrites = 0
  let productionReportBody: Record<string, unknown> | undefined
  await page.route('**/api/business-console/v1/mes/material-issue-requests*', (route) =>
    fulfillJson(route, {
      success: true,
      data: {
        items: [
          {
            requestId: 'ISS-GOOD',
            workOrderId: 'WO-1',
            operationTaskId: 'OP-1',
            materialId: 'MAT-1',
            uomCode: 'kg',
            materialLotId: 'LOT-1',
            requestedQuantity: 5,
            receivedQuantity: 5,
            consumedQuantity: 0,
            // #3912：领料单状态运行时是 PascalCase，小写 `received` 会让
            // isAvailableMaterialLot 判否、可选批次恒为空（下面的 toBeChecked 就会红）。
            status: 'Received',
          },
        ],
        total: 1,
      },
    }),
  )
  await page.route('**/api/business-console/v1/mes/production-reports', async (route) => {
    productionReportWrites += 1
    productionReportBody = route.request().postDataJSON() as Record<string, unknown>
    await fulfillJson(route, {
      success: true,
      data: {
        productionReportId: 'RPT-1',
        reportNo: 'RPT-E2E-1',
        operationReceipt: {
          operationId: 'OPERATION-1',
          operationType: 'mes.production-report.record',
          aggregateId: 'RPT-1',
          idempotencyKey: String(productionReportBody.idempotencyKey ?? ''),
          recordedAtUtc: '2026-08-28T10:00:00Z',
          replayed: false,
        },
      },
    })
  })
  await page.route('**/api/business-console/v1/barcode/resolve', async (route) => {
    const { scannedValue } = route.request().postDataJSON() as { scannedValue: string }
    if (scannedValue === 'QUALIFICATION') {
      return fulfillJson(
        route,
        resolveEnvelope('resolved', [
          { objectType: 'personnel', strongIds: { userId: 'WORKER-EXPIRED' } },
        ]),
      )
    }
    return fulfillJson(
      route,
      resolveEnvelope('resolved', [
        {
          objectType: 'mes-material-issue-request',
          strongIds: { materialIssueRequestId: `ISS-${scannedValue}` },
        },
      ]),
    )
  })
  await page.route('**/api/business-console/v1/mes/material-scan-prevalidation', async (route) => {
    const request = route.request().postDataJSON() as {
      materialIssueRequestId: string
      workOrderId: string
      operationTaskId: string
    }
    expect(request).toMatchObject({ workOrderId: 'WO-1', operationTaskId: 'OP-1' })
    if (request.materialIssueRequestId === 'ISS-SOURCE') {
      return fulfillJson(route, { success: false, message: 'SOURCE_UNAVAILABLE' }, 503)
    }
    const reasonCode =
      request.materialIssueRequestId === 'ISS-EXPIRED'
        ? 'material-lot-expired'
        : request.materialIssueRequestId === 'ISS-WRONG'
          ? 'work-order-mismatch'
          : 'material-scan-accepted'
    return fulfillJson(route, {
      success: true,
      data: {
        decision: reasonCode === 'material-scan-accepted' ? 'accepted' : 'rejected',
        reasonCode,
        materialIssueRequestId: request.materialIssueRequestId,
        workOrderId: request.workOrderId,
        operationTaskId: request.operationTaskId,
        materialId: 'MAT-1',
        materialLotId: 'LOT-1',
        materialQualification: 'primary',
        evaluatedAtUtc: '2026-08-28T10:00:00Z',
      },
    })
  })
  await page.route('**/api/business-console/v1/mes/context-scan-prevalidation', (route) =>
    fulfillJson(route, { success: false, message: '人员技能资格已过期，不能执行当前工序。' }, 400),
  )

  await page.goto('/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
  await expect(page.getByTestId('good-quantity')).toBeVisible()
  await page.waitForLoadState('networkidle')

  await scan(page, 'WRONG')
  await expect(page.getByTestId('mes-scan-status')).toContainText('不属于当前工单')
  await page.getByTestId('good-quantity').fill('1')
  await expect(page.getByTestId('good-quantity')).toHaveValue('1')
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  expect(productionReportWrites).toBe(0)

  await scan(page, 'EXPIRED')
  await expect(page.getByTestId('mes-scan-status')).toContainText('物料批次已过期')
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  expect(productionReportWrites).toBe(0)
  await scan(page, 'QUALIFICATION')
  await expect(page.getByTestId('mes-scan-status')).toContainText('人员技能资格已过期')
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  expect(productionReportWrites).toBe(0)
  await scan(page, 'SOURCE')
  await expect(page.getByTestId('mes-scan-status')).toContainText('预校验来源暂不可用')
  await expect(page.getByTestId('submit-report')).toBeDisabled()
  expect(productionReportWrites).toBe(0)

  await scan(page, 'GOOD')
  await expect(page.getByTestId('mes-scan-status')).toContainText('已通过当前工单工序预校验')
  await expect(page.getByTestId('material-lot-ISS-GOOD')).toBeChecked()
  await page.getByTestId('good-quantity').fill('1')
  await page.getByTestId('material-quantity-ISS-GOOD').fill('1')
  await expect(page.getByTestId('submit-report')).toBeEnabled()
  await page.getByTestId('submit-report').click()
  await expect.poll(() => productionReportWrites).toBe(1)
  expect(productionReportBody).toMatchObject({
    workOrderId: 'WO-1',
    operationTaskId: 'OP-1',
    consumedMaterialLots: [
      {
        materialId: 'MAT-1',
        materialLotId: 'LOT-1',
        consumedQuantity: 1,
        materialIssueRequestNo: 'ISS-GOOD',
      },
    ],
  })
})

test('领料快速连续扫码丢弃迟到结果，且 pending 期间阻断相关写入口', async ({ page }) => {
  let releaseSlow!: () => void
  const slowReleased = new Promise<void>((resolve) => {
    releaseSlow = resolve
  })
  await page.route('**/api/business-console/v1/barcode/resolve', async (route) => {
    const { scannedValue } = route.request().postDataJSON() as { scannedValue: string }
    if (scannedValue === 'SLOW') {
      await slowReleased
      return fulfillJson(
        route,
        resolveEnvelope('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-STALE' } },
        ]),
      )
    }
    if (scannedValue === 'VALID') {
      return fulfillJson(
        route,
        resolveEnvelope('resolved', [
          { objectType: 'mes-work-order', strongIds: { workOrderId: 'WO-1' } },
        ]),
      )
    }
    return fulfillJson(route, resolveEnvelope('unknown', [], 'managed-label-not-found'))
  })

  await page.goto('/mes/issue')
  await scan(page, 'SLOW')
  await expect(page.getByTestId('new-issue')).toBeDisabled()
  await scan(page, 'LATEST')
  await expect(page.getByTestId('mes-scan-status')).toContainText('未找到')
  await expect(page.getByTestId('new-issue')).toBeEnabled()
  releaseSlow()
  await expect(page.getByTestId('mes-scan-status')).toContainText('未找到')
  await scan(page, 'VALID')
  await expect(page.getByTestId('new-issue')).toBeEnabled()
})
