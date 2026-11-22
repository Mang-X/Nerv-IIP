import { test, expect } from '@playwright/test'
import { principal, workerProfile } from '../fixtures'
import { createContextWithPrincipal } from './fixture'

export function registerSelfReadback() {
  test('维修工单：HTTP 200 错位 skip/take 响应失败关闭且不渲染空态', async ({ page }) => {
    await page.route('**/api/business-console/v1/maintenance/work-orders**', async (route) => {
      const url = new URL(route.request().url())
      if (
        route.request().method() !== 'GET' ||
        url.pathname !== '/api/business-console/v1/maintenance/work-orders'
      ) {
        return route.fallback()
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            items: [],
            total: 0,
            skip: 20,
            take: 20,
          },
        }),
      })
    })

    await page.goto('/equipment/work-orders')

    await expect(page.getByTestId('maintenance-self-work-orders-error')).toContainText(
      '维修工单读取失败，请重试',
    )
    await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(0)
    await expect(page.getByText('当前维修人员暂无符合筛选条件的维修工单')).toHaveCount(0)
  })

  test('维修工单：畸形设备资料失败关闭，重试公开 ID 后不泄露机器来源引用', async ({ page }) => {
    const workOrderId = '019f1000-0000-7000-8000-000000000099'
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'
    const deviceReference = deviceAssetId.toUpperCase()
    const deviceDetailRequests: URL[] = []
    let rejectMalformedDevice = true
    page.on('request', (request) => {
      const url = new URL(request.url())
      if (url.pathname.includes('/master-data/resources/device-asset/')) {
        deviceDetailRequests.push(url)
      }
    })
    await page.route(
      `**/api/business-console/v1/maintenance/work-orders/${workOrderId}**`,
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              workOrderId: workOrderId.toUpperCase(),
              sourceReferenceId: '019f2000-0000-7000-8000-000000000099',
              deviceAssetId: deviceReference,
              priority: 'high',
              status: 'accepted',
              openedAtUtc: '2026-08-02T01:00:00.000Z',
              version: 3,
              allowedActions: [],
              blockReasons: [],
              lifecycle: [],
              assignedTechnicianUserId: principal.principalId,
              assignedTeamId: null,
            },
          }),
        })
      },
    )
    await page.route(
      `**/api/business-console/v1/master-data/resources/device-asset/${deviceReference}**`,
      async (route) => {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              resourceType: 'device-asset',
              deviceAssetId,
              code: 'DEV-CNC-01',
              displayName: '一号数控机床',
              active: true,
              snapshotVersion: 'device-v1',
              organizationId: principal.organizationId,
              environmentId: principal.environmentId,
              workshopCode: rejectMalformedDevice ? { code: 'WS-INVALID' } : 'WS-01',
            },
          }),
        })
      },
    )

    await page.goto(`/equipment/work-orders/${workOrderId.toUpperCase()}`)

    await expect(page.getByTestId('maintenance-work-order-detail-error')).toContainText(
      '工单详情读取失败，请重试',
    )
    await expect(page.getByTestId('maintenance-work-order-detail')).toHaveCount(0)
    rejectMalformedDevice = false
    await page.getByTestId('maintenance-work-order-detail-error').getByRole('button').click()

    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('一号数控机床')
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('DEV-CNC-01')
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('维修工单')
    await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText(
      '019f2000-0000-7000-8000-000000000099',
    )
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
      `维修人员 ${workerProfile.displayName}`,
    )
    expect(deviceDetailRequests).toHaveLength(2)
    expect(
      deviceDetailRequests.every(
        (url) =>
          url.pathname ===
          `/api/business-console/v1/master-data/resources/device-asset/${deviceReference}`,
      ),
    ).toBe(true)
  })

  test('维修工单：缺少设备位置读取权限时不发请求且不声称个人队列', async ({ browser }) => {
    const { context, page } = await createContextWithPrincipal(browser, {
      permissionCodes: principal.permissionCodes.filter(
        (code) => code !== 'business.masterdata.resources.read',
      ),
    })
    try {
      const requests: string[] = []
      page.on('request', (request) => {
        const pathname = new URL(request.url()).pathname
        if (
          pathname === '/api/business-console/v1/maintenance/work-orders' ||
          pathname === '/api/business-console/v1/master-data/device-assets'
        ) {
          requests.push(pathname)
        }
      })

      await page.goto('/equipment/work-orders')

      await expect(page.getByText('当前账号暂无法查看，请重新登录或联系管理员')).toBeVisible()
      await expect(page.getByText('我的维修工单')).toHaveCount(0)
      expect(requests).toEqual([])
    } finally {
      await context.close()
    }
  })

  test('维修工单：缺少主体 ID 时列表与详情均不发业务请求', async ({ browser }) => {
    const { context, page } = await createContextWithPrincipal(browser, { principalId: '' })
    try {
      const requests: string[] = []
      page.on('request', (request) => {
        const pathname = new URL(request.url()).pathname
        if (
          pathname === '/api/business-console/v1/maintenance/work-orders' ||
          pathname.startsWith('/api/business-console/v1/maintenance/work-orders/') ||
          pathname === '/api/business-console/v1/master-data/device-assets'
        ) {
          requests.push(pathname)
        }
      })

      await page.goto('/equipment/work-orders')

      await expect(page.getByText('当前账号暂无法查看，请重新登录或联系管理员')).toBeVisible()
      await expect(page.getByText('我的工单')).toHaveCount(0)
      expect(requests).toEqual([])

      await page.goto('/equipment/work-orders/019f0000-0000-7000-8000-000000000203')

      await expect(page.getByRole('heading', { name: '工单不可查看' })).toBeVisible()
      await expect(page.getByText('当前账号暂无法查看，请重新登录或联系管理员。')).toBeVisible()
      expect(requests).toEqual([])
    } finally {
      await context.close()
    }
  })

  test('维修工单：终态矛盾事实失败关闭，修正后强 ID 回读且仅可查看', async ({ page }) => {
    const detailRequests: URL[] = []
    let rejectContradictoryDetail = true
    await page.route(
      '**/api/business-console/v1/maintenance/work-orders/019f0000-0000-7000-8000-000000000202**',
      async (route) => {
        const url = new URL(route.request().url())
        detailRequests.push(url)
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              workOrderId: '019f0000-0000-7000-8000-000000000202',
              sourceReferenceId: 'MWO-2026-CLOSED',
              deviceAssetId: 'DEV-CNC-01',
              priority: 'medium',
              status: 'closed',
              openedAtUtc: '2026-08-02T01:00:00.000Z',
              version: 12,
              allowedActions: rejectContradictoryDetail ? ['start'] : [],
              blockReasons: ['terminal-status'],
              lifecycle: [],
              assignedTechnicianUserId: principal.principalId,
              assignedTeamId: 'team-a',
            },
          }),
        })
      },
    )

    await page.goto(
      '/equipment/work-orders/019f0000-0000-7000-8000-000000000202?sourceAlarmId=ALM-9',
    )

    await expect(page.getByTestId('maintenance-work-order-detail-error')).toContainText(
      '工单详情读取失败，请重试',
    )
    await expect(page.getByTestId('maintenance-work-order-detail')).toHaveCount(0)
    rejectContradictoryDetail = false
    await page.getByTestId('maintenance-work-order-detail-error').getByRole('button').click()
    await expect(page.getByTestId('maintenance-work-order-detail')).toBeVisible()
    expect(detailRequests).toHaveLength(2)
    expect(detailRequests.every((url) => url.searchParams.get('scopeKind') === 'self')).toBe(true)
    expect(
      detailRequests.every((url) => url.searchParams.get('scopeId') === principal.principalId),
    ).toBe(true)
    await expect(page.getByRole('button', { name: '开工', exact: true })).toHaveCount(0)
  })
}
