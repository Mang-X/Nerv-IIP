/**
 * 班次交接单上「交班人 / 接班人」的显示态（框架无关，纯 TS）。
 *
 * **判据是身份 id 在不在，不是姓名在不在。**
 *
 * 这两件事在交接班里后果完全不同：`未记录` 断言的是「这张单子上没有这个人」——问责链断了；
 * `姓名未知` 断言的是「人记在案，只是员工目录解不出名字」——问责链是完整的。用同一个词
 * 会让一张「已接班」的单子同屏写着「接班人 未记录」，那是与数据相反的读数（网关始终按认证
 * principal 注入 `incomingUserId`，所以「已接班且 id 有值」是常态而不是异常）。
 *
 * 姓名解不出时仍然**不回显用户 id**：那是 IAM 主体标识符，不是工号，上屏对一线没有指认价值。
 *
 * 这段判据先在 PDA 交接班 composable 里落地（PR #3464 第 1 轮阻断），business-console 的
 * 交接班页当时留着「只看 name」的旧写法，同一张单在两屏上给出互相矛盾的说法（#3475）。
 * 搬到共享包是为了让两屏读同一份判据 + 同一组文案，而不是各自再挑一个。
 */

export type ShiftHandoverPartyState = 'named' | 'name-unresolved' | 'absent' | 'pending'

export interface ShiftHandoverOutgoingPartyFields {
  outgoingUserId?: string | null
  outgoingUserName?: string | null
}

export interface ShiftHandoverIncomingPartyFields {
  incomingUserId?: string | null
  incomingUserName?: string | null
  acceptedAtUtc?: string | null
}

export function outgoingPartyState(row: ShiftHandoverOutgoingPartyFields): ShiftHandoverPartyState {
  if (row.outgoingUserName?.trim()) return 'named'
  return row.outgoingUserId?.trim() ? 'name-unresolved' : 'absent'
}

export function incomingPartyState(row: ShiftHandoverIncomingPartyFields): ShiftHandoverPartyState {
  if (row.incomingUserName?.trim()) return 'named'
  if (row.incomingUserId?.trim()) return 'name-unresolved'
  // 还没人接班 ≠ 接了班但身份没落下来；前者是正常中间态，后者才是问责缺口。
  return row.acceptedAtUtc?.trim() ? 'absent' : 'pending'
}

export const SHIFT_HANDOVER_PARTY_STATE_LABELS: Record<ShiftHandoverPartyState, string> = {
  named: '',
  'name-unresolved': '姓名未知',
  absent: '未记录',
  pending: '待接班',
}

export function outgoingUserLabel(row: ShiftHandoverOutgoingPartyFields): string {
  const state = outgoingPartyState(row)
  return state === 'named' ? row.outgoingUserName!.trim() : SHIFT_HANDOVER_PARTY_STATE_LABELS[state]
}

export function incomingUserLabel(row: ShiftHandoverIncomingPartyFields): string {
  const state = incomingPartyState(row)
  return state === 'named' ? row.incomingUserName!.trim() : SHIFT_HANDOVER_PARTY_STATE_LABELS[state]
}
