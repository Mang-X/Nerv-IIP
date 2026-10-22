import { expect, test, type Response } from '@playwright/test'

import { loginViaUi } from './support/login'
import { assertLiveStackReachable } from './support/preflight'
import { simulateScanGun } from './support/scan-gun'

function isPostResponse(response: Response, path: string) {
  return response.request().method() === 'POST' && new URL(response.url()).pathname === path
}

// Labels must come from the current stack's public print-batch readback, not mock fixtures.
test('live 报工扫码：未找到与对象不支持分离，支持工序经过真实上下文预校验', async ({
  page,
}, testInfo) => {
  const unknownLabel = process.env.NERV_IIP_LIVE_MES_UNKNOWN_LABEL
  const unsupportedLabel = process.env.NERV_IIP_LIVE_MES_UNSUPPORTED_LABEL
  const operationLabel = process.env.NERV_IIP_LIVE_MES_OPERATION_LABEL
  if (!unknownLabel || !unsupportedLabel || !operationLabel) {
    throw new Error('缺少真实扫码样本：需要未生成的管理范围条码、已识别不支持标签与有效工序标签。')
  }
  await assertLiveStackReachable()
  await loginViaUi(page)
  let reportWrites = 0
  let contextPrevalidations = 0
  page.on('request', (request) => {
    if (request.method() !== 'POST') return
    const path = new URL(request.url()).pathname
    if (path === '/api/business-console/v1/mes/production-reports') reportWrites += 1
    if (path === '/api/business-console/v1/mes/context-scan-prevalidation')
      contextPrevalidations += 1
  })
  await page.goto('/mes/report')
  const input = page.getByPlaceholder('扫描工单或工序', { exact: true })
  const feedback = page.getByTestId('mes-scan-status')
  const observations: Array<Record<string, unknown>> = []
  for (const sample of [
    {
      name: 'unknown',
      value: unknownLabel,
      status: 'unknown',
      reasonCode: 'managed-label-not-found',
      message: '未找到',
    },
    {
      name: 'unrecognized-format',
      value: 'WO-NOT-EXIST-3869',
      status: 'unsupported',
      reasonCode: 'barcode-format-unsupported',
      message: '未找到',
    },
    {
      name: 'recognized-unsupported-type',
      value: unsupportedLabel,
      status: 'resolved',
      reasonCode: null,
      message: '不支持',
    },
  ]) {
    await expect(input).toBeFocused()
    const resolved = page.waitForResponse((response) =>
      isPostResponse(response, '/api/business-console/v1/barcode/resolve'),
    )
    await simulateScanGun(page, sample.value)
    const response = await resolved
    expect(response.status()).toBe(200)
    const envelope = await response.json()
    expect(envelope).toMatchObject({
      success: true,
      data: { status: sample.status, reasonCode: sample.reasonCode },
    })
    if (sample.status === 'resolved') {
      expect(envelope.data.candidates).toHaveLength(1)
    } else {
      expect(envelope.data.candidates).toEqual([])
    }
    await expect(feedback).toContainText(sample.message)
    await expect(feedback).not.toContainText(sample.message === '未找到' ? '已识别' : '未找到')
    await expect(page).toHaveURL(/\/mes\/report$/)
    expect(reportWrites).toBe(0)
    expect(contextPrevalidations).toBe(0)
    observations.push({
      sample: sample.name,
      value: sample.value,
      response: envelope,
      feedback: await feedback.innerText(),
    })
    await page.screenshot({ path: testInfo.outputPath(`${sample.name}.png`), fullPage: true })
  }

  await expect(input).toBeFocused()
  const resolved = page.waitForResponse((response) =>
    isPostResponse(response, '/api/business-console/v1/barcode/resolve'),
  )
  const prevalidated = page.waitForResponse((response) =>
    isPostResponse(response, '/api/business-console/v1/mes/context-scan-prevalidation'),
  )
  await simulateScanGun(page, operationLabel)
  const resolveResponse = await resolved
  expect(resolveResponse.status()).toBe(200)
  const envelope = await resolveResponse.json()
  expect(envelope).toMatchObject({ success: true, data: { status: 'resolved' } })
  expect(envelope.data.candidates).toHaveLength(1)
  const candidate = envelope.data.candidates[0]
  expect(candidate.objectType).toBe('mes-operation')
  const contextResponse = await prevalidated
  expect(contextResponse.status()).toBe(200)
  expect(contextResponse.request().postDataJSON()).toMatchObject({
    workOrderId: candidate.strongIds.workOrderId,
    operationTaskId: candidate.strongIds.operationTaskId,
    objectType: 'operationTask',
    scannedObjectId: candidate.strongIds.operationTaskId,
  })
  const contextEnvelope = await contextResponse.json()
  expect(contextEnvelope).toMatchObject({
    success: true,
    data: {
      decision: 'accepted',
      reasonCode: 'operation-task-scan-accepted',
      workOrderId: candidate.strongIds.workOrderId,
      operationTaskId: candidate.strongIds.operationTaskId,
    },
  })
  await expect
    .poll(() => new URL(page.url()).searchParams.get('operationTaskId'))
    .toBe(candidate.strongIds.operationTaskId)
  expect(new URL(page.url()).searchParams.get('workOrderId')).toBe(candidate.strongIds.workOrderId)
  expect(contextPrevalidations).toBe(1)
  expect(reportWrites).toBe(0)
  observations.push({
    sample: 'supported-operation',
    value: operationLabel,
    response: envelope,
    prevalidation: contextEnvelope,
    reportWrites,
    contextPrevalidations,
  })
  await page.screenshot({ path: testInfo.outputPath('supported-operation.png'), fullPage: true })
  await testInfo.attach('public-scan-observations', {
    body: JSON.stringify(observations, null, 2),
    contentType: 'application/json',
  })
})
