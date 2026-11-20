// 大屏真实会话上下文与设备台账解析（MAN-466 样板，S3b/S3c 复用）。
//
// - 会话上下文（organizationId/environmentId）由 @nerv-iip/auth 的 principal 派生，
//   main.ts 在登录/会话恢复后调用 setScreenSession 注入；真实 fetcher 取数时读取。
// - 设备台账（deviceAssetId → 档案）由 business-console 设备主数据（device-asset）解析并短缓存：
//   真实 overview/可用率端点要求 deviceAssetIds 非空（≤50/批），台账变化很慢，缓存降低轮询请求量（#734 限流友好）。
import { readRows, readScreenPayload } from './real/response'
import {
  listBusinessConsoleDeviceAssets,
  type BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'

export interface ScreenSession {
  organizationId: string
  environmentId: string
}

let session: ScreenSession = { organizationId: '', environmentId: '' }

/** 注入/更新会话上下文（会话切换作废台账缓存）。 */
export function setScreenSession(next: Partial<ScreenSession>): void {
  const organizationId = (next.organizationId ?? session.organizationId).trim()
  const environmentId = (next.environmentId ?? session.environmentId).trim()
  if (organizationId !== session.organizationId || environmentId !== session.environmentId) {
    rosterCache = null
  }
  session = { organizationId, environmentId }
}

export function getScreenSession(): ScreenSession {
  return session
}

/** 组织/环境齐备才允许发起真实取数（空 scope 不打后端，见 AGENTS「空业务上下文」约束）。 */
export function hasScreenSession(): boolean {
  return session.organizationId.length > 0 && session.environmentId.length > 0
}

// —— 设备台账（deviceAssetId → 档案）——
export interface DeviceRosterEntry {
  /** 与 equipment/alarms/maintenance facade 联动的 telemetry 设备号（join 键）。 */
  deviceAssetId: string
  /** master-data 资产编码（人读，展示用；可与 deviceAssetId 不等）。 */
  code: string
  name: string
  lineCode: string
  lineName: string
  workCenterCode: string
  workshopCode: string
  workshopName: string
}
export interface DeviceRoster {
  /** 参与 overview/可用率查询的设备编号（已截断到 ROSTER_MAX）。 */
  ids: string[]
  byId: Map<string, DeviceRosterEntry>
  /** 台账实际总数（用于提示是否被 ≤50 截断）。 */
  total: number
}

const ROSTER_TTL_MS = 5 * 60_000
// overview/可用率端点 deviceAssetIds ≤ 50/批；小厂样板足够，超出等 #570 批量端点。
const ROSTER_MAX = 50
let rosterCache: { workCenterCode?: string; at: number; roster: DeviceRoster } | null = null

function rosterEntryOf(item: BusinessConsoleResourceItem): DeviceRosterEntry | null {
  // ⚠️ equipment/alarms/maintenance facade 消费 telemetry deviceAssetId；master-data code 仅作展示，
  // 二者可不相等（网关测试覆盖 code=DEV-001 / deviceAssetId=018f…）。join 键必须用 deviceAssetId，
  // 缺少关联键的台账条目不能用于状态查询。
  const deviceAssetId = item.deviceAssetId?.trim()
  if (!deviceAssetId) return null
  const code = item.code?.trim() || deviceAssetId
  const lineCode = item.lineCode?.trim() ?? ''
  const workshopCode = item.workshopCode?.trim() ?? ''
  return {
    deviceAssetId,
    code,
    name: item.displayName?.trim() || code,
    lineCode,
    // 真实平台无 workshop/line 名称维度，展示编码即可（分组视图据此归并）。
    lineName: lineCode || '—',
    workCenterCode: item.workCenterCode?.trim() ?? '',
    workshopCode,
    workshopName: workshopCode || '—',
  }
}

/** 解析设备台账（短缓存）；nowMs 便于测试注入，缺省取 Date.now()。 */
export async function resolveDeviceRoster(
  nowMs: number = Date.now(),
  force = false,
  workCenterCode?: string,
): Promise<DeviceRoster> {
  if (
    !force &&
    rosterCache &&
    rosterCache.workCenterCode === workCenterCode &&
    nowMs - rosterCache.at < ROSTER_TTL_MS
  ) {
    return rosterCache.roster
  }
  const requestedSession = session
  const { organizationId, environmentId } = requestedSession
  const res = await listBusinessConsoleDeviceAssets({
    throwOnError: true,
    signal: AbortSignal.timeout(12000),
    query: {
      organizationId,
      environmentId,
      workCenterCode,
      includeDisabled: false,
      skip: 0,
      take: 200,
    },
  })
  const page = readScreenPayload(res)
  const items = readRows(page.resources)
  const byId = new Map<string, DeviceRosterEntry>()
  for (const item of items) {
    const entry = rosterEntryOf(item)
    if (entry && (!workCenterCode || entry.workCenterCode === workCenterCode))
      byId.set(entry.deviceAssetId, entry)
  }
  const allIds = [...byId.keys()]
  if (allIds.length > ROSTER_MAX) {
    // 不静默截断：明确告知看板只覆盖前 50 台（等 #570 批量端点）。
    console.warn(
      `[screen] 设备台账 ${allIds.length} 台超过 overview 单批上限 ${ROSTER_MAX}，仅覆盖前 ${ROSTER_MAX} 台（待 #570 批量端点）。`,
    )
  }
  const roster: DeviceRoster = {
    ids: allIds.slice(0, ROSTER_MAX),
    byId,
    total: page.total ?? items.length,
  }
  if (session === requestedSession) rosterCache = { workCenterCode, at: nowMs, roster }
  return roster
}

/** 登录会话或权限变更后重新解析台账。 */
export function resetDeviceRosterCache(): void {
  rosterCache = null
}
