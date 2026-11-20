import {
  getBusinessConsoleEquipmentOverview,
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsPutawayTasks,
  listBusinessConsoleDeviceAssets,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityNcrs,
  listBusinessConsoleEquipmentAlarms,
  listBusinessConsoleMaintenanceWorkOrders,
  listBusinessConsoleWmsPickingTasks,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsWcsTasks,
} from '@nerv-iip/api-client'
import { inspectionScope, warehouseScope } from './workScope'
import { readRows } from './response'
import { accessError } from './readError'
import type { ScreenSession } from '../session'
import { deviceIdBatches } from './deviceBatches'
import { deviceWorkCenter } from './deviceScope'
import { isRunningDevice, screenDeviceLabel } from './equipment'

export class HallMetricUnavailable extends Error {}

type Result<T> = { response?: Response; data?: { success?: boolean; data?: T | null } }
function read<T>(result: Result<T>): T {
  if (!result.response) throw new Error('失联')
  if (result.response.status === 401) throw new HallMetricUnavailable('登录已失效')
  if (result.response.status === 403) throw new HallMetricUnavailable('无权查看')
  if (!result.response.ok || !result.data?.success || !result.data.data) throw new Error('读取失败')
  return result.data.data
}
function totalOf(page: { total?: number }): number {
  if (page.total === undefined || !Number.isSafeInteger(page.total) || page.total < 0)
    throw new Error('总数不可用')
  return page.total
}

export type HallMetric =
  | 'running'
  | 'alarms'
  | 'repairs'
  | 'picking'
  | 'putaway'
  | 'count'
  | 'wcs'
  | 'ncr'
  | 'inspection'
  | 'inbound'
  | 'outbound'

interface DeviceReading {
  value: string
  exceptions: string[]
}
export async function fetchHallMetric(
  metric: HallMetric,
  session: ScreenSession,
  detail = false,
): Promise<string | DeviceReading> {
  if (!session.organizationId || !session.environmentId)
    throw new HallMetricUnavailable('缺少组织环境')
  const query = { ...session, skip: 0, take: 1 }
  const domain =
    metric === 'putaway' || metric === 'inbound'
      ? 'receipts'
      : metric === 'count'
        ? 'counts'
        : 'shipments'
  const selection = await (async () => {
    try {
      if (['putaway', 'count', 'inbound', 'outbound', 'picking'].includes(metric))
        return await warehouseScope(session, domain)
      if (metric === 'inspection') return await inspectionScope(session)
    } catch (error) {
      const message = accessError(error)
      if (message) throw new HallMetricUnavailable(message)
      throw error
    }
  })()
  const scopedQuery = { ...query, ...selection?.query }
  const scopedReading = (value: string) =>
    selection ? { value, exceptions: [selection.label] } : value
  if (metric === 'outbound' || metric === 'inbound') {
    const options = { query: { ...scopedQuery, take: 50 }, signal: AbortSignal.timeout(12000) }
    const page =
      metric === 'outbound'
        ? read(await listBusinessConsoleWmsOutboundOrders(options))
        : read(await listBusinessConsoleWmsInboundOrders(options))
    const total = totalOf(page)
    const start = new Date()
    start.setHours(0, 0, 0, 0)
    const items = page.items
    if (
      !items ||
      items.length > total ||
      items.some((item) => !item.status || !Number.isFinite(Date.parse(item.createdAtUtc ?? '')))
    )
      throw new Error('单据数据不完整')
    const today = items.filter((item) => Date.parse(item.createdAtUtc!) >= start.getTime())
    // Gateway 按创建时间倒序；首页触及昨日或已覆盖全部时，当日分母才完整。
    if (total > items.length && today.length === items.length)
      throw new HallMetricUnavailable('当日单据覆盖不全')
    if (!today.length) return scopedReading('今日暂无单据')
    return scopedReading(
      `${today.filter((item) => item.status === 'Completed').length}/${today.length}`,
    )
  }
  if (metric === 'inspection' || metric === 'ncr') {
    const statuses =
      metric === 'inspection' ? ['pending', 'in-progress'] : ['open', 'disposition-in-progress']
    const samples: string[] = selection ? [selection.label] : []
    const totals: number[] = []
    for (const status of statuses) {
      const query = { ...scopedQuery, status, take: detail ? 5 : 1 }
      if (metric === 'inspection') {
        const page = read(
          await listBusinessConsoleQualityInspectionTasks({
            query,
            signal: AbortSignal.timeout(12000),
          }),
        )
        totals.push(totalOf(page))
        if (detail)
          for (const row of readRows(page.items))
            samples.push(
              `${status === 'pending' ? '待认领' : '进行中'} · ${row.inspectionTaskId} · ${row.skuCode ?? '—'}`,
            )
      } else {
        const page = read(
          await listBusinessConsoleQualityNcrs({ query, signal: AbortSignal.timeout(12000) }),
        )
        totals.push(totalOf(page))
        if (detail)
          for (const row of readRows(page.items))
            samples.push(
              `${status === 'open' ? '未处置' : '处置中'} · ${row.code ?? row.id} · ${row.skuCode ?? '—'}`,
            )
      }
    }
    return { value: totals.join(' / '), exceptions: samples }
  }
  if (metric === 'repairs') {
    const page = read(
      await listBusinessConsoleMaintenanceWorkOrders({
        query: { ...query, take: 5 },
        signal: AbortSignal.timeout(12000),
      }),
    )
    return {
      value: String(totalOf(page)),
      exceptions: readRows(page.items).map(
        (row) => `${row.workOrderId ?? '—'} · ${row.status ?? '—'}`,
      ),
    }
  }
  if (metric !== 'running') {
    const endpoints = {
      alarms: () =>
        listBusinessConsoleEquipmentAlarms({
          signal: AbortSignal.timeout(12000),
          query: { ...query, status: 'active' },
        }),
      putaway: () =>
        listBusinessConsoleWmsPutawayTasks({
          signal: AbortSignal.timeout(12000),
          query: { ...scopedQuery, status: 'Open' },
        }),
      count: () =>
        listBusinessConsoleWmsCountExecutions({
          signal: AbortSignal.timeout(12000),
          query: { ...scopedQuery, status: 'Open' },
        }),
      picking: () =>
        listBusinessConsoleWmsPickingTasks({
          signal: AbortSignal.timeout(12000),
          query: { ...scopedQuery, status: 'Open' },
        }),
      wcs: () =>
        listBusinessConsoleWmsWcsTasks({
          signal: AbortSignal.timeout(12000),
          query: { ...query, status: 'Failed' },
        }),
    }
    return scopedReading(String(totalOf(read<{ total?: number }>(await endpoints[metric]()))))
  }
  let workCenterCode: string | undefined
  try {
    workCenterCode = await deviceWorkCenter(session)
  } catch (error) {
    const message = accessError(error)
    if (message) throw new HallMetricUnavailable(message)
    throw error
  }
  const roster = read(
    await listBusinessConsoleDeviceAssets({
      signal: AbortSignal.timeout(12000),
      query: { ...query, workCenterCode, includeDisabled: false, take: 50 },
    }),
  )
  const total = totalOf(roster)
  if (!roster.resources || total > 50 || total !== roster.resources.length || roster.truncated)
    throw new HallMetricUnavailable('完整台账不可用')
  if (workCenterCode && roster.resources.some((row) => row.workCenterCode !== workCenterCode))
    throw new HallMetricUnavailable('设备台账范围不符')
  if (!total)
    return workCenterCode ? { value: '暂无设备', exceptions: [workCenterCode] } : '暂无设备'
  const ids = new Set(roster.resources.map((r) => r.deviceAssetId?.trim()))
  if (ids.has(undefined) || ids.has('') || ids.size !== total)
    throw new HallMetricUnavailable('设备映射不全')
  const devices = (
    await Promise.all(
      deviceIdBatches([...ids].filter((id): id is string => !!id)).map(async (deviceAssetIds) => {
        const overview = read(
          await getBusinessConsoleEquipmentOverview({
            signal: AbortSignal.timeout(12000),
            query: { ...session, deviceAssetIds },
          }),
        )
        if (!overview.devices) throw new Error('设备响应不完整')
        return overview.devices
      }),
    )
  ).flat()
  if (
    !devices ||
    devices.length !== total ||
    devices.some(
      (d) =>
        !ids.delete(d.deviceAssetId) ||
        typeof d.isSourceFresh !== 'boolean' ||
        d.activeAlarmCount === undefined,
    )
  )
    throw new HallMetricUnavailable('设备状态不全')
  const running = devices.filter((device) =>
    isRunningDevice(
      device.currentState,
      device.isSourceFresh === true,
      device.activeAlarmCount ?? 0,
    ),
  ).length
  const names = new Map(
    roster.resources.map((asset) => [
      asset.deviceAssetId?.trim(),
      asset.displayName || asset.code || asset.deviceAssetId,
    ]),
  )
  const exceptions = devices.flatMap((device) => {
    const state = screenDeviceLabel(
      device.currentState,
      device.isSourceFresh === true,
      device.activeAlarmCount ?? 0,
    )
    return ['报警', '停机', '状态未知', '断线', '换型'].includes(state)
      ? [`${names.get(device.deviceAssetId)} · ${state}`]
      : []
  })
  return {
    value: `${running}/${total}`,
    exceptions: [
      ...(workCenterCode ? ['工作中心 · ' + workCenterCode] : []),
      ...exceptions.slice(0, 3),
    ],
  }
}
