import { expect, test } from '@playwright/test'

const quality = '/api/business-console/v1/quality'
const planId = '979fb4e3-6b07-4558-956b-a7b1159f2e0b'
const taskId = '299e5981-b438-496f-ad2d-6ca8383c7f87'
const otherTaskId = '241cc1b7-812d-4a66-be13-7095fe0fd77c'
const recordId = 'db14cd7a-23e0-4563-b395-72850c7eed37'
const principal = {
  principalId: 'inspector-self',
  principalType: 'User',
  loginName: 'inspector',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.quality.inspection-records.read',
    'business.quality.inspection-records.create',
  ],
}
const task = {
  inspectionTaskId: taskId,
  inspectionPlanId: planId,
  sourceDocumentId: 'GR-RESUME',
  sourceType: 'receiving',
  sourceService: 'wms',
  skuCode: 'SKU-QA',
  quantity: 12,
  version: 1,
}

test('质检员重新进入工作台后续办本人检验并读取提交结果', async ({ page }) => {
  const session = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    sessionId: 'session-1',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
    principal,
  }
  await page.addInitScript(
    (storedSession) => {
      localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(storedSession))
    },
    { principal, refreshToken: session.refreshToken, sessionId: session.sessionId },
  )

  let status = 'pending'
  let claimCount = 0
  let submitCount = 0
  await page.route('**/api/console/v1/**', async (route) => {
    const { pathname } = new URL(route.request().url())
    if (pathname.endsWith('/auth/refresh'))
      return route.fulfill({ json: { success: true, data: session } })
    if (pathname.endsWith('/auth/me'))
      return route.fulfill({ json: { success: true, data: principal } })
    return route.fallback()
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const { pathname, searchParams } = new URL(route.request().url())
    const respond = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (pathname.endsWith('/me/work-context')) {
      const scope = { kind: 'organization', id: principal.organizationId, displayName: '一号工厂' }
      return respond({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        availableScopeKinds: ['organization'],
        authorizedScopes: [scope],
        selectedScope: scope,
        issues: [],
      })
    }
    if (pathname === `${quality}/inspection-tasks`) {
      const items = [
        {
          ...task,
          status,
          assignedInspectorUserId: status === 'pending' ? null : principal.principalId,
          allowedActions:
            status === 'pending'
              ? ['claim']
              : status === 'in-progress'
                ? ['submit-inspection']
                : [],
          inspectionRecordId: status === 'completed' ? recordId : null,
        },
        {
          ...task,
          inspectionTaskId: otherTaskId,
          sourceDocumentId: 'GR-OTHER',
          status: 'in-progress',
          assignedInspectorUserId: 'inspector-other',
          allowedActions: [],
          blockReasons: ['task-already-claimed'],
        },
      ].filter(
        (item) =>
          (!searchParams.has('status') || item.status === searchParams.get('status')) &&
          (!searchParams.has('inspectionTaskId') ||
            item.inspectionTaskId === searchParams.get('inspectionTaskId')),
      )
      return respond({ items, total: items.length })
    }
    if (pathname === `${quality}/inspection-tasks/${taskId}/claim`) {
      claimCount += 1
      expect(status).toBe('pending')
      status = 'in-progress'
      return respond({
        ...task,
        status,
        version: 2,
        assignedInspectorUserId: principal.principalId,
      })
    }
    if (pathname === `${quality}/inspection-plans/${planId}/characteristics`) {
      return respond({
        items: [{ characteristicCode: 'APP', name: '外观', characteristicType: 'attribute' }],
      })
    }
    if (pathname === `${quality}/inspection-tasks/${taskId}/inspection-record`) {
      submitCount += 1
      expect(status).toBe('in-progress')
      const body = route.request().postDataJSON()
      expect(body.resultLines).toEqual([
        expect.objectContaining({
          characteristicCode: 'APP',
          observedValue: '无划痕',
        }),
      ])
      status = 'completed'
      return respond({
        inspectionRecordId: recordId,
        result: 'passed',
        operationReceipt: {
          operationType: 'quality.inspection-task.submit',
          authority: 'quality',
          resourceType: 'inspection-record',
          resourceId: recordId,
          idempotencyKey: body.idempotencyKey,
          outcome: 'confirmed',
          stateConfirmed: true,
          readbackRequired: false,
          changedAtUtc: '2026-11-26T09:00:00Z',
          resourceStatus: 'passed',
        },
      })
    }
    if (pathname === `${quality}/inspection-records/${recordId}`) {
      expect(status).toBe('completed')
      return respond({
        id: recordId,
        sourceDocumentId: task.sourceDocumentId,
        skuCode: task.skuCode,
        inspectedQuantity: task.quantity,
        result: 'passed',
        resultLines: [{ characteristicCode: 'APP', observedValue: '无划痕' }],
      })
    }
    return respond({ items: [], total: 0 })
  })

  await page.goto('/quality/inspection-tasks')
  const ownRow = page.getByRole('row').filter({ hasText: 'GR-RESUME' })
  await ownRow.getByRole('button', { name: '认领并开始检验' }).click()
  const form = page.getByRole('dialog', { name: '创建检验记录' })
  await expect(form.getByText('GR-RESUME', { exact: true })).toBeVisible()
  await form.getByRole('button', { name: '取消' }).click()
  await expect(form).toBeHidden()

  await page.goto('/quality/inspection-tasks')
  const inProgress = page.getByRole('radio', { name: '检验中' })
  await expect(inProgress).toBeVisible()
  await inProgress.click()
  const otherRow = page.getByRole('row').filter({ hasText: 'GR-OTHER' })
  await expect(otherRow).toBeVisible()
  await expect(otherRow.getByRole('button', { name: /开始检验|继续检验/ })).toHaveCount(0)
  await ownRow.getByRole('button', { name: '继续检验' }).click()
  await expect(form.getByText('GR-RESUME', { exact: true })).toBeVisible()
  const resumedUrl = new URL(page.url())
  await form.getByLabel('实测值').fill('无划痕')
  await form.getByRole('button', { name: '提交检验记录' }).click()

  await expect(page).toHaveURL(/\/quality\/inspection-tasks/)
  const detail = page.getByRole('dialog', { name: /检验记录/ })
  await expect(detail.getByText('判定结论')).toBeVisible()
  await expect(detail.getByText('通过', { exact: true })).toBeVisible()
  await expect(detail.getByText('无划痕', { exact: true })).toBeVisible()
  resumedUrl.searchParams.set('inspectionTaskId', otherTaskId)
  resumedUrl.searchParams.set('sourceDocumentId', 'GR-OTHER')
  await page.goto(resumedUrl.href)
  await expect(form.getByText('GR-OTHER', { exact: true })).toBeVisible()
  await form.getByLabel('实测值').fill('无划痕')
  await expect(form.getByText(/该待检任务当前不允许提交检验/)).toBeVisible()
  await expect(form.getByRole('button', { name: '提交检验记录' })).toBeDisabled()

  expect(claimCount).toBe(1)
  expect(submitCount).toBe(1)
})
