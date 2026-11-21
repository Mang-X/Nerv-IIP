import { test, expect } from '@playwright/test'
import { principal, workerProfile } from '../fixtures'
import { selfWorkOrderId, expectMinimumTouchHeight } from './fixture'

export function registerSelfLookup() {
  test('维修工单：服务端 Self 筛选与分页 → 强 ID 详情重新校验', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 })
    const listRequests: URL[] = []
    const detailRequests: URL[] = []
    const deviceDetailRequests: URL[] = []
    let rejectFirstListResponse = true
    page.on('request', (request) => {
      const url = new URL(request.url())
      if (url.pathname.startsWith('/api/business-console/v1/master-data/resources/device-asset/')) {
        deviceDetailRequests.push(url)
      }
    })
    const commaDevice = {
      deviceAssetId: '019f0000-0000-7000-8000-000000000001',
      code: 'DEV,CNC-01',
      displayName: '一号数控机床',
      active: true,
      workshopCode: 'WS-1',
      lineCode: 'LINE-A',
      stationCode: 'ST-9',
    }
    await page.route('**/api/business-console/v1/master-data/device-assets**', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { resources: [commaDevice], total: 1, truncated: false, limit: 20 },
        }),
      }),
    )
    await page.route('**/api/business-console/v1/master-data/resources/device-asset/**', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            ...commaDevice,
            resourceType: 'device-asset',
            organizationId: principal.organizationId,
            environmentId: principal.environmentId,
            retiredOn: null,
            snapshotVersion: 'device-v1',
          },
        }),
      }),
    )
    const matchingWorkOrders = Array.from({ length: 25 }, (_, index) => ({
      workOrderId: selfWorkOrderId(index + 1),
      sourceReferenceId: `MWO-2026-${String(index + 1).padStart(4, '0')}`,
      deviceAssetId: index === 24 ? commaDevice.deviceAssetId : commaDevice.code,
      priority: 'high',
      status: 'accepted',
      openedAtUtc: '2026-08-02T01:00:00.000Z',
      assignedTechnicianUserId: principal.principalId,
      assignedTeamId: 'team-a',
      version: 7,
      allowedActions: ['start', 'cancel'],
      blockReasons: ['manage-permission-required'],
      lifecycle: [
        {
          action: 'accept',
          fromStatus: 'open',
          toStatus: 'accepted',
          actorPrincipalId: principal.principalId,
          technicianUserId: principal.principalId,
          teamId: 'team-a',
          reason: '现场接单',
          resultingVersion: 7,
          occurredAtUtc: '2026-08-02T01:02:03.000Z',
        },
      ],
    }))
    const workOrders = [
      ...matchingWorkOrders,
      {
        ...matchingWorkOrders[0],
        workOrderId: selfWorkOrderId(26),
        sourceReferenceId: 'MWO-OPEN-DISTRACTOR',
        status: 'open',
      },
      {
        ...matchingWorkOrders[0],
        workOrderId: selfWorkOrderId(27),
        sourceReferenceId: 'MWO-OTHER-DEVICE',
        deviceAssetId: 'DEV-LATHE-02',
      },
    ]

    await page.route('**/api/business-console/v1/maintenance/work-orders**', async (route) => {
      const url = new URL(route.request().url())
      const base = '/api/business-console/v1/maintenance/work-orders'
      if (route.request().method() !== 'GET') return route.fallback()
      if (url.pathname === base) {
        listRequests.push(url)
        if (rejectFirstListResponse) {
          rejectFirstListResponse = false
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({ success: false, message: '列表暂不可用', data: null }),
          })
        }
        const skip = Number(url.searchParams.get('skip') ?? 0)
        const take = Number(url.searchParams.get('take') ?? 20)
        const status = url.searchParams.get('status')?.trim().toLowerCase()
        const deviceReferences = [
          ...url.searchParams.getAll('deviceAssetReferences'),
          ...(url.searchParams.get('deviceAssetIds')?.split(',') ?? []),
          url.searchParams.get('deviceAssetId') ?? '',
        ]
          .map((value) => value.trim())
          .filter(Boolean)
        const authorizedAliases = deviceReferences.includes(commaDevice.deviceAssetId)
          ? [commaDevice.deviceAssetId, commaDevice.code]
          : deviceReferences
        const keyword = url.searchParams.get('keyword')?.trim().toLowerCase()
        const filtered = workOrders.filter((item) => {
          if (status && item.status.toLowerCase() !== status) return false
          if (authorizedAliases.length > 0 && !authorizedAliases.includes(item.deviceAssetId)) {
            return false
          }
          if (!keyword) return true
          return [
            item.deviceAssetId,
            item.sourceReferenceId,
            item.assignedTechnicianUserId,
            item.assignedTeamId,
          ].some((value) => value?.toLowerCase().includes(keyword))
        })
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              items: filtered.slice(skip, skip + take),
              total: filtered.length,
              skip,
              take,
            },
          }),
        })
      }
      if (url.pathname === `${base}/${selfWorkOrderId(1)}`) {
        detailRequests.push(url)
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ success: true, data: matchingWorkOrders[0] }),
        })
      }
      return route.fallback()
    })

    await page.goto('/equipment/work-orders')
    await expect(page.getByTestId('maintenance-self-work-orders-error')).toContainText(
      '维修工单读取失败，请重试',
    )
    await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(0)
    await expect(page.getByText('当前维修人员暂无符合筛选条件的维修工单')).toHaveCount(0)
    await page.getByTestId('retry-list').click()
    await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(20)
    expect(listRequests[0].searchParams.get('scopeKind')).toBe('self')
    expect(listRequests[0].searchParams.get('scopeId')).toBe(principal.principalId)
    expect(listRequests[0].searchParams.get('skip')).toBe('0')
    expect(listRequests[0].searchParams.get('take')).toBe('20')
    await expect(page.getByTestId('maintenance-work-order-row').first()).toContainText('已接单')
    await expect(page.getByTestId('maintenance-work-order-row').first()).not.toContainText(
      'DEV,CNC-01',
    )
    await expect(page.getByTestId('maintenance-work-order-row').first()).toContainText(
      workerProfile.displayName,
    )
    await expect(page.getByTestId('maintenance-work-order-row').first()).not.toContainText(
      principal.principalId,
    )

    await page.getByRole('searchbox', { name: '维修工单关键字' }).fill('MWO-2026-')
    await page.getByRole('button', { name: '全部状态' }).click()
    await page.getByRole('button', { name: '已接单', exact: true }).click()
    await page.getByTestId('maintenance-device-filter').click()
    await page.getByTestId('device-option-019f0000-0000-7000-8000-000000000001').click()
    await expectMinimumTouchHeight(page.getByTestId('maintenance-device-clear'), 48)
    await expect
      .poll(() => {
        const last = listRequests.at(-1)
        return last
          ? {
              scopeKind: last.searchParams.get('scopeKind'),
              scopeId: last.searchParams.get('scopeId'),
              status: last.searchParams.get('status'),
              deviceAssetReferences: last.searchParams.getAll('deviceAssetReferences'),
              keyword: last.searchParams.get('keyword'),
            }
          : undefined
      })
      .toEqual({
        scopeKind: 'self',
        scopeId: principal.principalId,
        status: 'accepted',
        deviceAssetReferences: ['019f0000-0000-7000-8000-000000000001'],
        keyword: 'MWO-2026-',
      })

    const scroller = page.locator('[data-slot="pull-refresh"] .nv-m-pr-scroll')
    await scroller.evaluate((element) => element.scrollTo({ top: element.scrollHeight }))
    await expect
      .poll(() => listRequests.some((url) => url.searchParams.get('skip') === '20'))
      .toBe(true)
    await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(25)
    await expect(page.getByText('MWO-2026-0025', { exact: false })).toBeVisible()
    await expect(page.getByText('MWO-OPEN-DISTRACTOR', { exact: false })).toHaveCount(0)

    await page.getByTestId('maintenance-work-order-row').first().press('Enter')
    await expect(page).toHaveURL(`/equipment/work-orders/${selfWorkOrderId(1)}`)
    await expectMinimumTouchHeight(page.getByRole('button', { name: '返回维修工单列表' }))
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('一号数控机床')
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
      'WS-1 · LINE-A · ST-9',
    )
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
      `维修人员 ${workerProfile.displayName}`,
    )
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('班组 甲班')
    await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText(
      principal.principalId,
    )
    await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText('team-a')
    await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText(
      selfWorkOrderId(1),
    )
    expect(detailRequests).toHaveLength(1)
    expect(detailRequests[0].searchParams.get('scopeKind')).toBe('self')
    expect(detailRequests[0].searchParams.get('scopeId')).toBe(principal.principalId)
    expect(deviceDetailRequests).toHaveLength(1)
    expect(deviceDetailRequests[0].pathname).toBe(
      '/api/business-console/v1/master-data/resources/device-asset/DEV%2CCNC-01',
    )
    expect(deviceDetailRequests[0].searchParams.get('organizationId')).toBe('org-001')
    expect(deviceDetailRequests[0].searchParams.get('environmentId')).toBe('env-dev')
    await expect(page.getByRole('button', { name: '开工', exact: true })).toHaveCount(0)
  })

  test('维修工单：设备编码换绑后只以强 ID 筛选且不扩入另一设备', async ({ page }) => {
    const deviceAId = '019f0000-0000-7000-8000-000000000011'
    const deviceBId = '019f0000-0000-7000-8000-000000000012'
    const reboundCode = 'DEV-REBIND'
    const currentDeviceACode = 'DEV-A-CURRENT'
    const listRequests: URL[] = []
    const listItem = (workOrderId: string, sourceReferenceId: string, deviceAssetId: string) => ({
      workOrderId,
      sourceReferenceId,
      deviceAssetId,
      priority: 'high',
      status: 'accepted',
      openedAtUtc: '2026-08-02T01:00:00.000Z',
      assignedTechnicianUserId: principal.principalId,
      assignedTeamId: null,
      version: 1,
    })
    const deviceAWorkOrders = [
      listItem(selfWorkOrderId(91), 'MWO-A-GUID', deviceAId),
      listItem(selfWorkOrderId(92), 'MWO-A-CODE', currentDeviceACode),
    ]
    const deviceBWorkOrder = listItem(selfWorkOrderId(93), 'MWO-B-REBOUND', deviceBId)

    await page.route('**/api/business-console/v1/master-data/device-assets**', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            resources: [
              {
                deviceAssetId: deviceAId,
                code: reboundCode,
                displayName: '设备 A',
                active: true,
              },
            ],
            total: 1,
            truncated: false,
            limit: 20,
          },
        }),
      }),
    )
    await page.route('**/api/business-console/v1/maintenance/work-orders**', (route) => {
      const url = new URL(route.request().url())
      if (
        route.request().method() !== 'GET' ||
        url.pathname !== '/api/business-console/v1/maintenance/work-orders'
      ) {
        return route.fallback()
      }
      listRequests.push(url)
      const requestedReferences = url.searchParams.getAll('deviceAssetReferences')
      const items =
        requestedReferences.length === 0
          ? [...deviceAWorkOrders, deviceBWorkOrder]
          : deviceAWorkOrders
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { items, total: items.length, skip: 0, take: 20 },
        }),
      })
    })

    await page.goto('/equipment/work-orders')
    await page.getByTestId('maintenance-device-filter').click()
    await page.getByTestId(`device-option-${deviceAId}`).click()

    await expect
      .poll(() => listRequests.at(-1)?.searchParams.getAll('deviceAssetReferences'))
      .toEqual([deviceAId])
    await expect(page.getByTestId('maintenance-device-filter')).toContainText('设备 A')
    await expect(page.getByText('MWO-A-GUID', { exact: false })).toBeVisible()
    await expect(page.getByText('MWO-A-CODE', { exact: false })).toBeVisible()
    await expect(page.getByText('MWO-B-REBOUND', { exact: false })).toHaveCount(0)
    expect(listRequests.at(-1)?.searchParams.getAll('deviceAssetReferences')).not.toContain(
      reboundCode,
    )
  })

  test('维修工单：GUID 形设备编码在筛选、详情与报修中保持 Ordinal', async ({ page }) => {
    const deviceCode = '019F0000-0000-7000-8000-0000000000AA'
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'
    const workOrderId = selfWorkOrderId(88)
    const listRequests: URL[] = []
    const deviceDetailRequests: URL[] = []
    const postBodies: Record<string, unknown>[] = []
    const device = {
      deviceAssetId,
      code: deviceCode,
      displayName: 'GUID 编码设备',
      active: true,
      workshopCode: 'WS-GUID',
    }
    const workOrder = {
      workOrderId,
      sourceReferenceId: 'MWO-GUID-CODE',
      deviceAssetId: deviceCode,
      priority: 'high',
      status: 'accepted',
      openedAtUtc: '2026-08-02T01:00:00.000Z',
      assignedTechnicianUserId: principal.principalId,
      assignedTeamId: null,
      version: 2,
      allowedActions: [],
      blockReasons: [],
      lifecycle: [
        {
          action: 'accept',
          fromStatus: 'open',
          toStatus: 'accepted',
          actorPrincipalId: principal.principalId,
          technicianUserId: principal.principalId,
          teamId: null,
          reason: '现场接单',
          resultingVersion: 2,
          occurredAtUtc: '2026-08-02T01:01:00.000Z',
        },
      ],
    }

    await page.route('**/api/business-console/v1/master-data/device-assets**', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { resources: [device], total: 1, truncated: false, limit: 20 },
        }),
      }),
    )
    await page.route(
      '**/api/business-console/v1/master-data/resources/device-asset/**',
      (route) => {
        deviceDetailRequests.push(new URL(route.request().url()))
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              ...device,
              resourceType: 'device-asset',
              organizationId: principal.organizationId,
              environmentId: principal.environmentId,
              retiredOn: null,
              snapshotVersion: 'device-guid-code-v1',
            },
          }),
        })
      },
    )
    await page.route('**/api/business-console/v2/maintenance/work-orders', async (route) => {
      const request = route.request()
      if (request.method() !== 'POST') return route.fallback()
      postBodies.push(request.postDataJSON() as Record<string, unknown>)
      return route.fallback()
    })
    await page.route('**/api/business-console/v1/maintenance/work-orders**', async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const base = '/api/business-console/v1/maintenance/work-orders'
      if (request.method() !== 'GET') return route.fallback()
      if (url.pathname === `${base}/${workOrderId}`) {
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ success: true, data: workOrder }),
        })
      }
      if (url.pathname !== base) return route.fallback()
      listRequests.push(url)
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      const requestedReferences = url.searchParams.getAll('deviceAssetReferences')
      const items =
        requestedReferences.length === 0 || requestedReferences.includes(deviceAssetId)
          ? [workOrder]
          : []
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: { items, total: items.length, skip, take },
        }),
      })
    })

    await page.goto('/equipment/work-orders')
    await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(1)
    await page.getByTestId('maintenance-device-filter').click()
    await page.getByTestId(`device-option-${deviceAssetId}`).click()
    await expect
      .poll(() => listRequests.at(-1)?.searchParams.getAll('deviceAssetReferences'))
      .toEqual([deviceAssetId])

    await page.getByTestId('maintenance-work-order-row').press('Enter')
    await expect(page).toHaveURL(`/equipment/work-orders/${workOrderId}`)
    await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('GUID 编码设备')
    expect(deviceDetailRequests.at(-1)?.pathname).toBe(
      `/api/business-console/v1/master-data/resources/device-asset/${deviceCode}`,
    )

    const sourceAlarmId = 'AAAAAAAA-AAAA-4AAA-8AAA-AAAAAAAAAAAA'
    await page.goto(`/equipment/repair?deviceAssetId=${deviceCode}&sourceAlarmId=${sourceAlarmId}`)
    await expect(page.getByTestId('device-trigger')).toContainText(deviceCode)
    await page.getByTestId('priority-trigger').click()
    await page
      .locator('[data-slot="mobile-sheet-content"]')
      .getByRole('button', { name: '高', exact: true })
      .click()
    await page.getByTestId('submit').click()
    await expect.poll(() => postBodies.length).toBe(1)
    expect(postBodies[0]).toMatchObject({
      deviceAssetId: deviceCode,
      sourceAlarmId,
      priority: 'high',
    })
  })
}
