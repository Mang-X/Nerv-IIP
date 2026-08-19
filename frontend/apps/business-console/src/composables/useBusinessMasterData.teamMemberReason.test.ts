import { expect, it } from 'vitest'

import type { useTeamMembers } from './useBusinessMasterData'

/**
 * 类型级契约（#1655）：班组成员移除的手写边界必须要求 `reason`。
 *
 * `@ts-expect-error` 的反向断言由 business-console typecheck 兑现：如果未来把第二参数
 * 改回可选，这一行将不再报错，未使用的 `@ts-expect-error` 会让门禁直接失败。
 */
type TeamMembers = ReturnType<typeof useTeamMembers>

declare const removeMember: TeamMembers['removeMember']

  // eslint-disable-next-line @typescript-eslint/no-unused-expressions
;() => {
  void removeMember('usr-1', '调入维修班组')

  // @ts-expect-error 漏传原因必须编译失败。
  void removeMember('usr-1')
}

it('班组成员移除原因必填由 typecheck 兑现', () => {
  expect(true).toBe(true)
})
