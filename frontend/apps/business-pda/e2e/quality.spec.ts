import { expect, test, type Route } from '@playwright/test'
import { INSPECTION_TASK_SOURCE_TYPES } from '@nerv-iip/business-core'
import { principal, routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

const QUALITY_BASE = '/api/business-console/v1/quality'
const TASK_ID = 'TASK-640'
const PLAN_ID = 'PLAN-640'
const RECORD_ID = 'RECORD-640'
const OLD_TASK_ID = 'TASK-OLD-640'

function envelope<T>(data: T) {
  return { success: true, message: null, data }
}

function taskFacts(
  state: { claimed: boolean; completed: boolean },
  overrides: Record<string, unknown> = {},
) {
  return {
    inspectionTaskId: TASK_ID,
    inspectionPlanId: PLAN_ID,
    sourceType: 'operation',
    sourceService: 'mes',
    sourceDocumentId: 'WO-9001',
    skuCode: 'SKU-640',
    quantity: 10,
    uomCode: 'pcs',
    batchNo: null,
    serialNo: null,
    status: state.completed ? 'completed' : state.claimed ? 'in-progress' : 'pending',
    dueAtUtc: '2026-07-31T00:00:00.000Z',
    createdAtUtc: '2026-07-30T00:00:00.000Z',
    inspectionRecordId: state.completed ? RECORD_ID : null,
    // The real Self query applies AssignedUserId == principalId before pagination.
    assignedInspectorUserId: principal.principalId,
    assignedTeamId: 'team-a',
    version: state.completed ? 3 : state.claimed ? 2 : 1,
    isOverdue: true,
    allowedActions: state.completed ? [] : state.claimed ? ['submit-inspection'] : ['claim'],
    blockReasons: [],
    ...overrides,
  }
}

function filteredTaskFacts(url: URL, state: { claimed: boolean; completed: boolean }) {
  const candidates = [
    taskFacts(state, {
      inspectionTaskId: OLD_TASK_ID,
      inspectionPlanId: 'PLAN-OLD-640',
      sourceType: 'receiving',
      sourceService: 'wms',
      sourceDocumentId: 'RCV-OLD-640',
      skuCode: 'SKU-OLD-640',
      dueAtUtc: '2026-08-20T00:00:00.000Z',
      inspectionRecordId: null,
      status: 'pending',
      version: 1,
      isOverdue: false,
      allowedActions: ['claim'],
    }),
    taskFacts(state, {
      inspectionTaskId: 'TASK-OLD-641',
      inspectionPlanId: 'PLAN-OLD-641',
      sourceType: 'final',
      sourceService: 'erp',
      sourceDocumentId: 'SO-OLD-641',
      skuCode: 'SKU-OLD-641',
      dueAtUtc: '2026-08-21T00:00:00.000Z',
      inspectionRecordId: null,
      status: 'pending',
      version: 1,
      isOverdue: false,
      allowedActions: ['claim'],
    }),
    taskFacts(state),
  ]
  const status = url.searchParams.get('status')
  const sourceType = url.searchParams.get('sourceType')
  const sourceService = url.searchParams.get('sourceService')
  const keyword = url.searchParams.get('keyword')?.trim().toLowerCase()
  const overdue = url.searchParams.get('overdue') === 'true'
  return candidates.filter((task) => {
    if (status && task.status !== status) return false
    if (sourceType && task.sourceType !== sourceType) return false
    if (sourceService && task.sourceService !== sourceService) return false
    if (
      keyword &&
      ![task.sourceDocumentId, task.skuCode].some((value) =>
        String(value ?? '')
          .toLowerCase()
          .includes(keyword),
      )
    ) {
      return false
    }
    return !overdue || task.isOverdue === true
  })
}

function confirmedReceipt(idempotencyKey: string) {
  return {
    operationType: 'quality.inspection-task.submit',
    authority: 'business-gateway',
    resourceType: 'quality.inspection-record',
    resourceId: RECORD_ID,
    idempotencyKey,
    outcome: 'confirmed',
    stateConfirmed: true,
    readbackRequired: false,
    changedAtUtc: '2026-08-01T00:00:00.000Z',
    resourceStatus: 'completed',
    readbackMethod: null,
    readbackPath: null,
  }
}

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

test('375x812：服务端筛选 → 领取 → 逐项录入 → task/record 强 ID 回读 → 权威结果', async ({
  page,
}) => {
  const state = { claimed: false, completed: false }
  const listRequests: string[] = []
  const listResponses: Array<{
    requestUrl: string
    ids: string[]
    sourceTypes: string[]
    sources: Array<{ sourceType: string; sourceService: string }>
    total: number
    assignedInspectorUserIds: Array<string | null | undefined>
  }> = []
  const readbacks: string[] = []
  let claimRequest: { query: URLSearchParams; body: Record<string, unknown> } | undefined
  let submittedBody: Record<string, unknown> | undefined

  await page.route('**/api/business-console/v1/quality/**', async (route: Route) => {
    const request = route.request()
    const url = new URL(request.url())
    const { pathname } = url
    const method = request.method()
    const fulfill = (body: unknown, status = 200) =>
      route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })

    if (method === 'GET' && pathname === `${QUALITY_BASE}/inspection-tasks`) {
      listRequests.push(url.toString())
      const items = filteredTaskFacts(url, state)
      listResponses.push({
        requestUrl: url.toString(),
        ids: items.map((item) => String(item.inspectionTaskId)),
        sourceTypes: items.map((item) => String(item.sourceType)),
        sources: items.map((item) => ({
          sourceType: String(item.sourceType),
          sourceService: String(item.sourceService),
        })),
        total: items.length,
        assignedInspectorUserIds: items.map((item) => item.assignedInspectorUserId),
      })
      return fulfill(envelope({ items, total: items.length }))
    }
    if (method === 'GET' && pathname === `${QUALITY_BASE}/reason-codes`) {
      return fulfill(envelope({ items: [], total: 0 }))
    }
    if (method === 'POST' && pathname === `${QUALITY_BASE}/inspection-tasks/${TASK_ID}/claim`) {
      state.claimed = true
      const body = (request.postDataJSON() ?? {}) as Record<string, unknown>
      claimRequest = { query: new URLSearchParams(url.search), body }
      return fulfill(
        envelope({
          inspectionTaskId: TASK_ID,
          status: 'in-progress',
          assignedInspectorUserId: principal.principalId,
          version: 2,
          idempotencyKey: body.idempotencyKey,
        }),
      )
    }
    if (method === 'GET' && pathname === `${QUALITY_BASE}/inspection-tasks/${TASK_ID}`) {
      readbacks.push(pathname)
      return fulfill(
        envelope({
          task: taskFacts(state),
          planCode: 'IP-640',
          category: 'operation',
          characteristics: [],
        }),
      )
    }
    if (
      method === 'GET' &&
      pathname === `${QUALITY_BASE}/inspection-plans/${PLAN_ID}/characteristics`
    ) {
      return fulfill(
        envelope({
          inspectionPlanId: PLAN_ID,
          planCode: 'IP-640',
          category: 'operation',
          skuCode: 'SKU-640',
          items: [
            {
              characteristicCode: 'OD',
              name: '外径',
              characteristicType: 'variable',
              required: true,
              nominalValue: 10,
              lowerSpecLimit: 9,
              upperSpecLimit: 11,
              unitCode: 'mm',
            },
          ],
        }),
      )
    }
    if (
      method === 'POST' &&
      pathname === `${QUALITY_BASE}/inspection-tasks/${TASK_ID}/inspection-record`
    ) {
      submittedBody = (request.postDataJSON() ?? {}) as Record<string, unknown>
      state.completed = true
      return fulfill(
        envelope({
          inspectionRecordId: RECORD_ID,
          result: 'passed',
          nonconformanceReportId: null,
          nonconformanceReportCode: null,
          operationReceipt: confirmedReceipt(String(submittedBody.idempotencyKey ?? '')),
        }),
      )
    }
    if (method === 'GET' && pathname === `${QUALITY_BASE}/inspection-records/${RECORD_ID}`) {
      readbacks.push(pathname)
      return fulfill(
        envelope({
          inspectionRecordId: RECORD_ID,
          sourceType: 'operation',
          sourceService: 'mes',
          sourceDocumentId: 'WO-9001',
          skuCode: 'SKU-640',
          inspectedQuantity: 10,
          uomCode: 'pcs',
          result: 'passed',
          dispositionReason: null,
          nonconformanceReportId: null,
          resultLines: [
            {
              characteristicCode: 'OD',
              observedValue: '10',
              measuredValue: 10,
              unitCode: 'mm',
              result: 'passed',
              defectReason: null,
              defectQuantity: null,
            },
          ],
          createdAtUtc: '2026-08-01T00:00:00.000Z',
          attemptNumber: 1,
          reinspectionOfInspectionRecordId: null,
        }),
      )
    }
    return route.fallback()
  })

  await page.goto('/quality/tasks')
  await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()

  await expect(page.getByText('来源单 RCV-OLD-640')).toBeVisible()
  await expect(page.getByText('已加载 3 / 共 3')).toBeVisible()
  const initialResponse = listResponses.find((response) => {
    const query = new URL(response.requestUrl).searchParams
    return query.get('status') === 'pending' && !query.has('sourceType')
  })
  expect(initialResponse?.ids).toContain(OLD_TASK_ID)
  expect(initialResponse?.total).toBe(3)
  expect(
    initialResponse?.sourceTypes.every((sourceType) =>
      INSPECTION_TASK_SOURCE_TYPES.includes(sourceType),
    ),
  ).toBe(true)
  expect(initialResponse?.sources).toEqual([
    { sourceType: 'receiving', sourceService: 'wms' },
    { sourceType: 'final', sourceService: 'erp' },
    { sourceType: 'operation', sourceService: 'mes' },
  ])
  expect(initialResponse?.assignedInspectorUserIds).toEqual([
    principal.principalId,
    principal.principalId,
    principal.principalId,
  ])

  await page.getByRole('button', { name: '待领取', exact: true }).click()
  await page.getByRole('button', { name: '进行中', exact: true }).first().click()
  await expect
    .poll(() =>
      listRequests.some(
        (requestUrl) => new URL(requestUrl).searchParams.get('status') === 'in-progress',
      ),
    )
    .toBe(true)
  await expect(
    page
      .getByText('当前账号没有符合筛选条件的质检任务；缺少登录主体或组织环境时不会发起查询。')
      .last(),
  ).toBeVisible()
  await page.getByRole('button', { name: '进行中', exact: true }).first().click()
  await page.getByRole('button', { name: '待领取', exact: true }).click()

  await page.getByTestId('chip-operation').click()
  await page.getByRole('button', { name: '全部来源服务' }).click()
  await page.getByRole('button', { name: 'MES', exact: true }).click()
  await page.getByRole('button', { name: '全部时效' }).click()
  await page.getByRole('button', { name: '仅看超期' }).click()
  const scan = page.getByPlaceholder('扫描或输入来源单据 / SKU 以筛选')
  await scan.fill('WO-9001')
  await scan.press('Enter')

  await expect
    .poll(() =>
      listRequests.some((requestUrl) => {
        const query = new URL(requestUrl).searchParams
        return (
          query.get('scopeKind') === 'self' &&
          query.get('scopeId') === principal.principalId &&
          query.get('status') === 'pending' &&
          query.get('sourceType') === 'operation' &&
          query.get('sourceService') === 'mes' &&
          query.get('keyword') === 'WO-9001' &&
          query.get('overdue') === 'true' &&
          query.get('skip') === '0' &&
          query.get('take') === '20'
        )
      }),
    )
    .toBe(true)
  await expect(page.getByText('来源单 RCV-OLD-640')).toHaveCount(0)
  await expect(page.getByText('来源单 WO-9001')).toBeVisible()
  await expect(page.getByText('已加载 1 / 共 1')).toBeVisible()
  await expect(page.getByTestId('task-row')).toHaveCount(1)
  const fullyFilteredResponse = listResponses.find((response) => {
    const query = new URL(response.requestUrl).searchParams
    return (
      query.get('status') === 'pending' &&
      query.get('sourceType') === 'operation' &&
      query.get('sourceService') === 'mes' &&
      query.get('keyword') === 'WO-9001' &&
      query.get('overdue') === 'true'
    )
  })
  expect(fullyFilteredResponse).toMatchObject({ ids: [TASK_ID], total: 1 })

  listRequests.length = 0
  listResponses.length = 0
  await page.goto('/')
  await page.goto('/quality/tasks')
  await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()
  await expect(page.getByText('筛选：WO-9001')).toBeVisible()
  await expect(page.getByRole('button', { name: '待领取', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: '仅看超期', exact: true })).toBeVisible()
  await expect(page.getByRole('button', { name: 'MES', exact: true })).toBeVisible()
  await expect(page.getByTestId('chip-operation')).toHaveClass(/bg-brand/)
  await expect
    .poll(() =>
      listResponses.some((response) => {
        const query = new URL(response.requestUrl).searchParams
        return (
          query.get('scopeKind') === 'self' &&
          query.get('scopeId') === principal.principalId &&
          query.get('status') === 'pending' &&
          query.get('sourceType') === 'operation' &&
          query.get('sourceService') === 'mes' &&
          query.get('keyword') === 'WO-9001' &&
          query.get('overdue') === 'true' &&
          response.ids.join(',') === TASK_ID &&
          response.total === 1
        )
      }),
    )
    .toBe(true)
  await expect(page.getByText('来源单 RCV-OLD-640')).toHaveCount(0)
  await expect(page.getByText('来源单 WO-9001')).toBeVisible()
  await page.getByTestId('task-row').click()

  await expect(page.getByText('第 2/3 步')).toBeVisible()
  await page.getByTestId('measured-value').click()
  const keyboard = page.locator('[data-slot="number-keyboard"]')
  await keyboard.getByRole('button', { name: '1', exact: true }).click()
  await keyboard.getByRole('button', { name: '0', exact: true }).click()
  await keyboard.getByRole('button', { name: '完成' }).first().click()
  await expect(page.getByTestId('submit')).toBeEnabled()
  await page.getByTestId('submit').click()

  await expect(page.getByText('检验合格')).toBeVisible()
  await expect(page.getByText('检验结果已记录。')).toBeVisible()
  expect(submittedBody).toMatchObject({
    idempotencyKey: expect.stringMatching(/^quality-submit-/),
    resultLines: [expect.objectContaining({ characteristicCode: 'OD', measuredValue: 10 })],
  })
  expect(claimRequest?.query.get('scopeKind')).toBe('self')
  expect(claimRequest?.query.get('scopeId')).toBe(principal.principalId)
  expect(claimRequest?.body).toMatchObject({
    idempotencyKey: expect.stringMatching(/^quality-claim-/),
    expectedVersion: 1,
  })
  expect(readbacks.filter((path) => path.endsWith(`/inspection-tasks/${TASK_ID}`)).length).toBe(3)
  expect(readbacks.at(-1)).toBe(`${QUALITY_BASE}/inspection-records/${RECORD_ID}`)
})

for (const scenario of [
  {
    name: '403 越权',
    status: 403,
    code: 'task-outside-selected-work-scope',
    message: '任务不在当前工作范围内，无法领取。',
  },
  {
    name: '409 生命周期冲突',
    status: 409,
    code: 'lifecycle-conflict',
    message: '状态已被其他操作更新',
  },
  {
    name: '422 已被领取',
    status: 422,
    code: 'task-already-claimed',
    message: '任务已由其他检验员领取。',
  },
] as const) {
  test(`375x812：${scenario.name}显示稳定提示并留在列表`, async ({ page }) => {
    const state = { claimed: false, completed: false }
    await page.route('**/api/business-console/v1/quality/**', async (route) => {
      const request = route.request()
      const { pathname } = new URL(request.url())
      const fulfill = (body: unknown, status = 200) =>
        route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })

      if (request.method() === 'GET' && pathname === `${QUALITY_BASE}/inspection-tasks`) {
        return fulfill(envelope({ items: [taskFacts(state)], total: 1 }))
      }
      if (request.method() === 'GET' && pathname === `${QUALITY_BASE}/reason-codes`) {
        return fulfill(envelope({ items: [], total: 0 }))
      }
      if (
        request.method() === 'POST' &&
        pathname === `${QUALITY_BASE}/inspection-tasks/${TASK_ID}/claim`
      ) {
        return fulfill({ success: false, message: scenario.code, data: null }, scenario.status)
      }
      return route.fallback()
    })

    await page.goto('/quality/tasks')
    await page.getByTestId('task-row').click()

    await expect(page.getByText(scenario.message)).toBeVisible()
    await expect(page.getByTestId('task-row')).toBeVisible()
    await expect(page.getByText('第 1/3 步')).toBeVisible()
  })
}
