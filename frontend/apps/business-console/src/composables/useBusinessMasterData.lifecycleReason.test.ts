import { expect, it } from 'vitest'

import type {
  MasterDataLifecyclePatch,
  useMasterDataResourceActions,
  useWorkerRegistry,
} from './useBusinessMasterData'

/**
 * 类型级契约（#878）：停用 / 重新启用的**手写动作边界**必须要求 `reason`。
 *
 * 为什么要有它：生成契约把 `reason` 收紧为必填之后，只要动作签名写成
 * `patch: Partial<...> = {}`，`actions.disable(code)` 就又能编译通过——正是本票要消除的
 * 原始漏传路径（编译期放行、运行期被后端稳定拒绝）。运行时单测覆盖不到这一层：
 * 它们断言的是「填了原因会带上」，不是「不填根本写不出来」。
 *
 * 下面的 `@ts-expect-error` 是**反向断言**：一旦有人把 `reason` 改回可选，这些行就不再报错，
 * `vue-tsc --noEmit`（`pnpm -C apps/business-console typecheck`）会因「未使用的 ts-expect-error」
 * 直接失败。这条门禁在 typecheck 兑现，不在 vitest 运行时兑现。
 */
type ResourceActions = ReturnType<typeof useMasterDataResourceActions>
type WorkerRegistry = ReturnType<typeof useWorkerRegistry>

declare const resourceActions: ResourceActions
declare const workerRegistry: WorkerRegistry

  // eslint-disable-next-line @typescript-eslint/no-unused-expressions
;() => {
  // 合法：带上业务原因。
  void resourceActions.disable('SKU-001', { reason: '该规格已被新版图纸替代' })
  void resourceActions.enable('SKU-001', { reason: '图纸变更暂缓' })
  void workerRegistry.disable('EMP-901', { reason: '已办理离职手续' })
  void workerRegistry.enable('EMP-901', { reason: '返岗复工' })

  // @ts-expect-error 漏传补丁 = 漏传原因，必须编译失败。
  void resourceActions.disable('SKU-001')
  // @ts-expect-error 空补丁同样缺原因。
  void resourceActions.disable('SKU-001', {})
  // @ts-expect-error 漏传补丁 = 漏传原因，必须编译失败。
  void resourceActions.enable('SKU-001')
  // @ts-expect-error 空补丁同样缺原因。
  void resourceActions.enable('SKU-001', {})
  // @ts-expect-error 员工目录包装同样不得把必填放松掉。
  void workerRegistry.disable('EMP-901')
  // @ts-expect-error 员工目录包装同样不得把必填放松掉。
  void workerRegistry.enable('EMP-901')

  // 补丁类型自身：只给可选字段而不给 reason 也必须编译失败。
  // @ts-expect-error 缺 reason。
  const patchWithoutReason: MasterDataLifecyclePatch = { codeSet: 'product-category' }
  void patchWithoutReason
}

it('生命周期动作的原因必填由 typecheck 兑现（本用例只标记契约存在）', () => {
  // 断言在编译期已经发生；运行时留一条可见记录，避免这份契约被当成"没人跑的文件"删掉。
  expect(true).toBe(true)
})
