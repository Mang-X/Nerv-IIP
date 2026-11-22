import { type Route } from '@playwright/test'
import { fulfillJson, envelope, confirmedOperation } from './http'
import { principal, workerProfile } from './auth'

export const CREATED_MAINTENANCE_WORK_ORDER_ID = '33333333-3333-3333-3333-333333333333'

/**
 * 停机原因目录（`downtime-reason`）的模拟条目。码保留混合大小写：Maintenance 只对
 * `ReasonCode` 做 trim（`MaintenanceText.Required`），不改大小写，所以这是后端造得出的
 * 真实形状，可以承担"原样提交"的断言。
 */
export const DOWNTIME_REASON_DIRECTORY = [
  {
    id: 'reason-1',
    displayName: '液压泄漏',
    code: 'Hyd-Leak_01',
    sourceService: 'maintenance',
    context: { reasonCategory: 'equipment', lossCategory: 'availability' },
  },
  {
    id: 'reason-2',
    displayName: '主轴异响',
    code: 'Spindle-Noise',
    sourceService: 'maintenance',
    context: { reasonCategory: 'equipment', lossCategory: 'availability' },
  },
]

const deviceAssets = [
  {
    deviceAssetId: '019f0000-0000-7000-8000-000000000001',
    code: 'DEV-CNC-01',
    displayName: '一号数控机床',
    active: true,
    workshopCode: 'WS-1',
    lineCode: 'LINE-A',
    stationCode: 'ST-9',
  },
  {
    deviceAssetId: '019f0000-0000-7000-8000-000000000002',
    code: 'DEV-LATHE-02',
    displayName: '二号车床',
    active: true,
    workshopCode: 'WS-2',
    lineCode: 'LINE-B',
  },
  {
    deviceAssetId: '019f0000-0000-7000-8000-000000000003',
    code: 'DEV-A',
    displayName: '装配线冲压机',
    active: true,
    workshopCode: 'WS-1',
    lineCode: 'LINE-A',
    stationCode: 'ST-1',
  },
]
export function routeEquipment(route: Route, requestUrl: URL, pathname: string, method: string) {
  // 报修设备选择器：principal scope + 服务端 keyword/skip/take，有界返回。
  if (pathname === '/api/business-console/v1/master-data/device-assets') {
    const keyword = (requestUrl.searchParams.get('keyword') ?? '').trim().toLowerCase()
    const skip = Math.max(0, Number(requestUrl.searchParams.get('skip') ?? 0))
    const take = Math.max(1, Number(requestUrl.searchParams.get('take') ?? 20))
    const matched = keyword
      ? deviceAssets.filter(
          (item) =>
            item.deviceAssetId.toLowerCase().includes(keyword) ||
            item.displayName.toLowerCase().includes(keyword) ||
            item.code.toLowerCase().includes(keyword),
        )
      : deviceAssets
    return fulfillJson(
      route,
      envelope({
        resources: matched.slice(skip, skip + take),
        total: matched.length,
        truncated: skip + take < matched.length,
        limit: take,
      }),
    )
  }

  const deviceDetailMatch = pathname.match(
    /^\/api\/business-console\/v1\/master-data\/resources\/device-asset\/([^/]+)$/,
  )
  if (deviceDetailMatch) {
    const requestedCode = decodeURIComponent(deviceDetailMatch[1])
    const device = deviceAssets.find(
      (item) => item.code === requestedCode || item.deviceAssetId === requestedCode,
    )
    return fulfillJson(
      route,
      envelope(
        device
          ? {
              resourceType: 'device-asset',
              deviceAssetId: device.deviceAssetId,
              code: device.code,
              displayName: device.displayName,
              active: device.active,
              workshopCode: device.workshopCode,
              lineCode: device.lineCode,
              stationCode: device.stationCode,
              organizationId: principal.organizationId,
              environmentId: principal.environmentId,
              snapshotVersion: 'v1',
            }
          : null,
      ),
      device ? 200 : 404,
    )
  }

  const teamDetailMatch = pathname.match(
    /^\/api\/business-console\/v1\/master-data\/resources\/team\/([^/]+)$/,
  )
  if (teamDetailMatch) {
    const requestedCode = decodeURIComponent(teamDetailMatch[1])
    const teamName =
      requestedCode === 'team-a'
        ? '甲班'
        : workerProfile.teams.find((team) => team.teamCode === requestedCode)?.teamName
    return fulfillJson(
      route,
      envelope(
        teamName
          ? {
              resourceType: 'team',
              code: requestedCode,
              displayName: teamName,
              active: true,
              organizationId: principal.organizationId,
              environmentId: principal.environmentId,
              snapshotVersion: 'v1',
            }
          : null,
      ),
      teamName ? 200 : 404,
    )
  }

  if (
    method === 'GET' &&
    pathname ===
      `/api/business-console/v1/maintenance/work-orders/${CREATED_MAINTENANCE_WORK_ORDER_ID}`
  ) {
    return fulfillJson(
      route,
      { success: false, message: '新建工单尚未指派给当前维修人员', data: null },
      403,
    )
  }

  // 报修：停机原因目录（Maintenance 权威 downtime-reason 词表，经网关 searchable directory）。
  // 只回本 principal organization/environment 的码——跨租户码由网关过滤掉，模拟层同样不给。
  if (pathname === '/api/business-console/v1/directories/downtime-reason') {
    const keyword = requestUrl.searchParams.get('keyword')?.trim() ?? ''
    const items = DOWNTIME_REASON_DIRECTORY.filter(
      (item) => !keyword || item.displayName.includes(keyword) || item.code.includes(keyword),
    )
    return fulfillJson(
      route,
      envelope({
        directoryType: 'downtime-reason',
        status: 'available',
        items,
        total: items.length,
        sourceService: 'maintenance',
        authorityDirectoryType: 'downtime-reason',
        rankingMode: 'default',
        rankingStatus: 'applied',
        ordering: 'default',
        orderingExplanation: '按目录默认顺序',
      }),
    )
  }

  // 报修：v2 建单（#2964 迁移后 PDA 生产路径唯一的创建入口）
  if (pathname === '/api/business-console/v2/maintenance/work-orders' && method === 'POST') {
    const body = route.request().postDataJSON() as { idempotencyKey: string }
    return fulfillJson(
      route,
      envelope({
        workOrderId: CREATED_MAINTENANCE_WORK_ORDER_ID,
        operationReceipt: confirmedOperation(
          'maintenance.work-order.create',
          CREATED_MAINTENANCE_WORK_ORDER_ID,
          body.idempotencyKey,
          'open',
        ),
      }),
    )
  }

  // 报修：维修工单 list（v1 读面不变）
  if (pathname === '/api/business-console/v1/maintenance/work-orders') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-M1',
            deviceAssetId: 'DEV-A',
            priority: 'high',
            status: 'open',
            openedBy: principal.loginName,
            openedAtUtc: '2026-06-10T01:00:00.000Z',
            assignedTechnicianUserId: principal.principalId,
          },
        ],
        total: 1,
        skip: Number(requestUrl.searchParams.get('skip') ?? 0),
        take: Number(requestUrl.searchParams.get('take') ?? 20),
      }),
    )
  }

  // 点检：保养计划 list（点检页先选计划）
  if (pathname === '/api/business-console/v1/maintenance/plans') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            planId: 'PLAN-1',
            planCode: 'PM-001',
            deviceAssetId: 'DEV-A',
            interval: 'P7D',
          },
        ],
        total: 1,
      }),
    )
  }

  // 点检：记录 list（空）/ record
  if (pathname === '/api/business-console/v1/maintenance/inspections') {
    if (method === 'POST') {
      return fulfillJson(route, envelope({ inspectionId: 'INS-new' }))
    }
    return fulfillJson(route, envelope({ items: [], total: 0 }))
  }

  // 报警查看（只读）
  if (pathname === '/api/business-console/v1/equipment/alarms') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            alarmEventId: 'ALM-1',
            deviceAssetId: 'DEV-A',
            alarmCode: 'E-101',
            severity: 'critical',
            raisedAtUtc: '2026-06-10T02:30:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }
}
