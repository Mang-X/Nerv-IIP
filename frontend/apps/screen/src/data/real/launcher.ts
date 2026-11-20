import {
  getBusinessConsoleEquipmentOverview,
  listBusinessConsoleDeviceAssets,
  listBusinessConsoleQualityInspectionTasks,
  listBusinessConsoleQualityNcrs,
  listBusinessConsoleEquipmentAlarms,
  listBusinessConsoleWmsPickingTasks,
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

export type HallMetric = 'running' | 'alarms' | 'picking' | 'wcs' | 'ncr' | 'inspection'

export async function fetchHallMetric(metric: HallMetric, session: ScreenSession): Promise<string> {
  if (!session.organizationId || !session.environmentId)
    throw new HallMetricUnavailable('缺少组织环境')
  const query = { ...session, skip: 0, take: 1 }
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
  return `${devices.filter((d) => isRunningDevice(d.currentState, d.isSourceFresh === true, d.activeAlarmCount ?? 0)).length}/${total}`
}
