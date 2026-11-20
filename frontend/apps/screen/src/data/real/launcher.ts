import {
  getBusinessConsoleEquipmentOverview,
  listBusinessConsoleDeviceAssets,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityNcrs,
  listBusinessConsoleEquipmentAlarms,
  listBusinessConsoleWmsPickingTasks,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsWcsTasks,
} from '@nerv-iip/api-client'
import type { ScreenSession } from '../session'
import { isRunningDevice } from './equipment'

export class HallMetricUnavailable extends Error {}

type Result<T> = { response?: Response; data?: { success?: boolean; data?: T | null } }
function read<T>(result: Result<T>): T {
  if (!result.response) throw new Error('失联')
  if ([401, 403].includes(result.response.status)) throw new HallMetricUnavailable('无权查看')
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
  | 'picking'
  | 'wcs'
  | 'ncr'
  | 'inspection'
  | 'outbound'

interface DeviceReading {
  value: string
  exceptions: string[]
}
export async function fetchHallMetric(
  metric: HallMetric,
  session: ScreenSession,
): Promise<string | DeviceReading> {
  if (!session.organizationId || !session.environmentId)
    throw new HallMetricUnavailable('缺少组织环境')
  const query = { ...session, skip: 0, take: 1 }
  if (metric === 'outbound') {
    const page = read(await listBusinessConsoleWmsOutboundOrders({ query: { ...query, take: 50 } }))
    const total = totalOf(page)
    const start = new Date()
    start.setHours(0, 0, 0, 0)
    const items = page.items
    if (
      !items ||
      items.length > total ||
      items.some((item) => !item.status || !Number.isFinite(Date.parse(item.createdAtUtc ?? '')))
    )
      throw new Error('出库单数据不完整')
    const today = items.filter((item) => Date.parse(item.createdAtUtc!) >= start.getTime())
    // Gateway 按创建时间倒序；首页触及昨日或已覆盖全部时，当日分母才完整。
    if (total > items.length && today.length === items.length)
      throw new HallMetricUnavailable('当日单据覆盖不全')
    if (!today.length) return '今日暂无单据'
    return `${today.filter((item) => item.status === 'Completed').length}/${today.length}`
  }
  if (metric !== 'running') {
    const endpoints = {
      alarms: () => listBusinessConsoleEquipmentAlarms({ query: { ...query, status: 'active' } }),
      ncr: () => listBusinessConsoleQualityNcrs({ query: { ...query, status: 'open' } }),
      inspection: () =>
        listBusinessConsoleQualityInspectionTasks({ query: { ...query, status: 'pending' } }),
      picking: () => listBusinessConsoleWmsPickingTasks({ query: { ...query, status: 'Open' } }),
      wcs: () => listBusinessConsoleWmsWcsTasks({ query: { ...query, status: 'Failed' } }),
    }
    return String(totalOf(read<{ total?: number }>(await endpoints[metric]())))
  }
  const roster = read(
    await listBusinessConsoleDeviceAssets({
      query: { ...query, includeDisabled: false, take: 50 },
    }),
  )
  const total = totalOf(roster)
  if (!roster.resources || total > 50 || total !== roster.resources.length || roster.truncated)
    throw new HallMetricUnavailable('完整台账不可用')
  if (!total) return '暂无设备'
  const ids = new Set(roster.resources.map((r) => r.deviceAssetId?.trim()))
  if (ids.has(undefined) || ids.has('') || ids.size !== total)
    throw new HallMetricUnavailable('设备映射不全')
  const overview = read(
    await getBusinessConsoleEquipmentOverview({
      query: { ...session, deviceAssetIds: [...ids].join(',') },
    }),
  )
  const devices = overview.devices
  if (
    !devices ||
    devices.length !== total ||
    devices.some(
      (d) =>
        !ids.delete(d.deviceAssetId) ||
        typeof d.isSourceFresh !== 'boolean' ||
        d.activeAlarmCount === undefined ||
        (d.isSourceFresh && !d.currentState && !d.activeAlarmCount),
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
    const state = !device.isSourceFresh ? '断线' : device.activeAlarmCount ? '报警' : undefined
    return state ? [`${names.get(device.deviceAssetId)} · ${state}`] : []
  })
  return { value: `${running}/${total}`, exceptions: exceptions.slice(0, 3) }
}
