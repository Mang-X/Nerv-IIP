import { expect, test, type Response } from '@playwright/test'
import { loginViaUi } from './support/login'
import { assertLiveStackReachable } from './support/preflight'
import { simulateScanGun } from './support/scan-gun'

const QUALITY_TASK_LIST_PATH = '/api/business-console/v1/quality/inspection-tasks'

interface QualityTaskListItem {
  inspectionTaskId?: string
  sourceDocumentId?: string | null
  skuCode?: string | null
  batchNo?: string | null
}

interface QualityTaskListEnvelope {
  success?: boolean
  data?: {
    items?: QualityTaskListItem[]
    total?: number
  } | null
}

function isQualityTaskListResponse(response: Response) {
  return (
    response.request().method() === 'GET' &&
    new URL(response.url()).pathname === QUALITY_TASK_LIST_PATH
  )
}

// L2 真实栈仿真走查——quality 链路只读 smoke（方案文档 §8 M1b）。
// 无任何 page.route mock：真实 IAM 登录 → 真实 BusinessGateway 待检任务列表 → S1 常驻直扫。
// 只读约束：本 spec 不做任何写操作，扫码只更新服务端 keyword 筛选。写路径归 M1c。

test('live 只读链路：真实登录 → /quality/tasks 渲染 → S1 常驻直扫触发服务端筛选', async ({
  page,
}) => {
  await assertLiveStackReachable()
  await loginViaUi(page)
  const authenticatedPrincipalId = await page.evaluate(() => {
    try {
      const stored = JSON.parse(localStorage.getItem('nerv-iip.business-pda.auth') ?? '{}') as {
        principal?: { principalId?: unknown }
      }
      return typeof stored.principal?.principalId === 'string'
        ? stored.principal.principalId.trim()
        : ''
    } catch {
      return ''
    }
  })
  expect(authenticatedPrincipalId.length).toBeGreaterThan(0)

  const initialListResponsePromise = page.waitForResponse(
    (response) =>
      isQualityTaskListResponse(response) && !new URL(response.url()).searchParams.has('keyword'),
  )
  await page.goto('/quality/tasks')
  await expect(page.getByRole('heading', { name: '检验任务' })).toBeVisible()

  const initialListResponse = await initialListResponsePromise
  const initialListUrl = new URL(initialListResponse.url())
  expect(initialListResponse.status()).toBe(200)
  expect(initialListUrl.searchParams.get('scopeKind')).toBe('self')
  const selfScopeId = initialListUrl.searchParams.get('scopeId')?.trim() ?? ''
  expect(selfScopeId).toBe(authenticatedPrincipalId)
  const initialEnvelope = (await initialListResponse.json()) as QualityTaskListEnvelope
  expect(initialEnvelope).toMatchObject({
    success: true,
    data: { items: expect.any(Array), total: expect.any(Number) },
  })

  // 等待真实列表请求落定（loading 消失），并断言非错误态（RetryableListError 不出现）。
  await expect(page.getByText('加载中…')).toHaveCount(0)
  await expect(page.getByTestId('tasks-error')).toHaveCount(0)

  // S1 前提断言：ScanBar 挂载后焦点常驻（不做任何点击/聚焦操作）。
  const scanInput = page.getByPlaceholder('扫描或输入来源单据 / SKU 以筛选')
  await expect(scanInput).toBeVisible()
  await expect(scanInput).toBeFocused()

  // 扫码值：优先 env 注入；缺省时从第一行任务读取来源单号（真实数据驱动，不伪造）。
  let code = process.env.NERV_IIP_LIVE_SCAN_CODE
  if (!code) {
    const rows = page.getByTestId('task-row')
    if ((await rows.count()) === 0) {
      throw new Error(
        '环境阻塞：待检任务列表为空且未提供 NERV_IIP_LIVE_SCAN_CODE——' +
          '请先 seed 待检任务（QualitySeedService）或用 NERV_IIP_LIVE_SCAN_CODE 指定条码。' +
          'live 走查不伪造数据、不静默跳过。',
      )
    }
    const firstRowText = await rows.first().innerText()
    const match = /来源单\s+([^\s·]+)/.exec(firstRowText)
    if (!match) {
      throw new Error(
        `无法从首行任务提取来源单号（行文本：${firstRowText.replaceAll('\n', ' | ')}）。` +
          '请用 NERV_IIP_LIVE_SCAN_CODE 显式指定扫码值。',
      )
    }
    code = match[1]
  }

  // 必须在扫码前注册真实公开 Gateway 响应等待器；若 composable 未发 GET、漏传 keyword/Self
  // scope，或响应失败，本用例会超时/失败，不能只凭本地 banner 提前通过。
  const filteredListResponsePromise = page.waitForResponse((response) => {
    if (!isQualityTaskListResponse(response)) return false
    const url = new URL(response.url())
    return url.searchParams.get('keyword') === code
  })

  // S1 常驻直扫：不 focus、不 fill，DOM 层键盘楔入近似（突发字符流 + Enter 后缀）。
  await simulateScanGun(page, code)

  const filteredListResponse = await filteredListResponsePromise
  const filteredListUrl = new URL(filteredListResponse.url())
  expect(filteredListUrl.searchParams.get('scopeKind')).toBe('self')
  expect(filteredListUrl.searchParams.get('scopeId')).toBe(selfScopeId)
  expect(filteredListUrl.searchParams.get('keyword')).toBe(code)
  expect(filteredListResponse.status()).toBe(200)
  const filteredEnvelope = (await filteredListResponse.json()) as QualityTaskListEnvelope
  expect(filteredEnvelope).toMatchObject({
    success: true,
    data: { items: expect.any(Array), total: expect.any(Number) },
  })

  const filteredItems = filteredEnvelope.data?.items ?? []
  const filteredTotal = filteredEnvelope.data?.total ?? -1
  expect(filteredTotal).toBeGreaterThanOrEqual(filteredItems.length)

  // 扫码值只作为服务端 keyword 条件；操作员仍需等待权威列表并显式选行。
  await expect(page.getByText(`筛选：${code}`)).toBeVisible()
  await expect(page.getByText('第 2/3 步')).toHaveCount(0)
  await expect(page.getByTestId('tasks-error')).toHaveCount(0)
  if (filteredItems.length > 0) {
    const expectedIds = filteredItems.map((item) => item.inspectionTaskId?.trim() ?? '')
    expect(expectedIds.every(Boolean)).toBe(true)
    expect(new Set(expectedIds).size).toBe(expectedIds.length)

    const taskRows = page.getByTestId('task-row')
    const expectedIdsSorted = [...expectedIds].sort()
    await expect
      .poll(() =>
        taskRows.evaluateAll((rows) =>
          rows.map((row) => row.getAttribute('data-task-id') ?? '').sort(),
        ),
      )
      .toEqual(expectedIdsSorted)

    const renderedRows = await taskRows.evaluateAll((rows) =>
      rows.map((row) => ({
        inspectionTaskId: row.getAttribute('data-task-id') ?? '',
        text: row.textContent ?? '',
      })),
    )
    for (const item of filteredItems) {
      const row = renderedRows.find(
        (candidate) => candidate.inspectionTaskId === item.inspectionTaskId,
      )
      expect(row, `missing rendered task ${item.inspectionTaskId}`).toBeDefined()
      expect(row?.text).toContain(item.skuCode?.trim() || '未知物料')
      if (item.sourceDocumentId?.trim()) {
        expect(row?.text).toContain(`来源单 ${item.sourceDocumentId.trim()}`)
      }
      if (item.batchNo?.trim()) {
        expect(row?.text).toContain(`批次 ${item.batchNo.trim()}`)
      }
    }
  } else {
    expect(filteredTotal).toBe(0)
    await expect(
      page
        .getByText('当前账号没有符合筛选条件的质检任务；缺少登录主体或组织环境时不会发起查询。')
        .last(),
    ).toBeVisible()
  }
  await expect(page.getByTestId('list-scope-meta')).toContainText(
    `已加载 ${filteredItems.length} / 共 ${filteredTotal}`,
  )
})
