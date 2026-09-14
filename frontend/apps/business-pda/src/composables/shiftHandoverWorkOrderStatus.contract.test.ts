import {
  SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS,
  shiftHandoverUnfinishedWorkOrderStatusLabel,
} from '@nerv-iip/business-core'
import type { ListBusinessConsoleMesWorkOrdersData } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'

/**
 * 未完工单状态写面值域的**跨界契约**。
 *
 * 这个文件住在 PDA 而不是 business-core，因为断言的另一端在 PDA 的依赖里：
 * 契约类型来自 `@nerv-iip/api-client`（business-core 不依赖它）。
 *
 * 先前那格 round-trip 只跨了 `mesLabels.ts` 里 PDA 自己的两张表——**两端都在自己家里**，
 * 所以放跑了「`Planned` / `OnHold` 在系统里根本不存在」这个缺陷。这里把另一端挪到真正的
 * 生产/消费面上。
 */

/** 契约里工单状态的完整值域（api-client 生成物，与 MES 域常量同源）。 */
type ContractWorkOrderStatus = NonNullable<ListBusinessConsoleMesWorkOrdersData['query']['status']>

/**
 * **类型层断言（由 typecheck 门禁兑现，不是运行时）**：
 * 写面每个码都必须是契约里真实存在的工单状态。写了 `Planned` / `OnHold` 这种不存在的码，
 * `vue-tsc` 直接报错——运行时用例看不见这一类错误，因为「不存在的码」在 PDA 侧照样能显示。
 */
const _writeCodesMustExistInTheContract: readonly ContractWorkOrderStatus[] =
  SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map((option) => option.code)
void _writeCodesMustExistInTheContract

/**
 * PC 读面（`business-console/src/composables/mes/useMesReferenceLabels.ts` 的 `statusLabel`）
 * 对这四个码的说法。
 *
 * ## 这是**单向**的 pin，覆盖边界如下（实测，不是推断）
 *
 * | 谁漂移 | 会不会红 |
 * | --- | --- |
 * | 本仓写面 label（`SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS`） | 四个码都红，红在本文件 |
 * | console 的 `started` 文案 | 红，但红在 console 自己的 `handoversPage.test.ts` |
 * | console 的 `created` / `released` / `hold` 文案 | **两侧都绿——抓不到** |
 *
 * 实测读数：把 console 的 `started: 已开工→生产中` ⇒ console 1 failed / 2471 passed；
 * 把 `released: 已释放→已下达` ⇒ console 2472 全绿 **且** PDA 1343 全绿。
 *
 * 所以别把下面这张表读成「四个码都双面钉住了」——它只挡得住**我方**漂移；console 侧四个码里
 * 只有 `started` 有独立防线。console 在另一个 app 里，从这里 import 它的词表只会变成第四份副本，
 * 这条缝不在本票内补。r4 真栈在 PC 屏上逐码实看过一次，那是一次性证据、不是持续防线。
 */
const CONSOLE_READ_FACE_LABELS: Readonly<Record<string, string>> = {
  created: '已创建',
  released: '已释放',
  started: '已开工',
  hold: '挂起',
}

/** MES 域里工单的终态（`WorkOrder.cs`）——未完工单按定义不可能处于其中任何一个。 */
const TERMINAL_WORK_ORDER_STATUSES = [
  'completed',
  'closed',
  'cancelled',
  'scrapped',
  'split',
  'merged',
] as const

describe('未完工单状态写面值域 × 契约', () => {
  it('uses the contract/domain spelling — lowercase, not the C# constant NAME', () => {
    const codes = SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map((o) => o.code)
    expect(codes).toEqual(['created', 'released', 'started', 'hold'])
    // `WorkOrder.StartedStatus` 的**值**是 "started"；把常量名 `Started` 当值写进去，
    // PC 读面解不出就会把英文码显示给用户。
    for (const code of codes) expect(code).toBe(code.toLowerCase())
  })

  it('excludes every terminal status — 已完成/已关闭的工单不是未完工单', () => {
    const codes: readonly string[] = SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map(
      (o) => o.code,
    )
    for (const terminal of TERMINAL_WORK_ORDER_STATUSES) {
      expect(codes).not.toContain(terminal)
    }
  })

  it('never offers two options that read the same on screen', () => {
    const labels = SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map((o) => o.label)
    expect(new Set(labels).size).toBe(labels.length)
  })

  it('reads back identically on BOTH faces — PDA 与 PC 同一张单不能两种说法', () => {
    for (const option of SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS) {
      // PDA 侧
      expect(shiftHandoverUnfinishedWorkOrderStatusLabel(option.code)).toBe(option.label)
      // PC 侧（钉住的期望；不一致说明 console 词表改了，去核 useMesReferenceLabels.ts）
      expect(CONSOLE_READ_FACE_LABELS[option.code]).toBe(option.label)
    }
  })

  it('never lets a raw English code reach the screen', () => {
    for (const option of SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS) {
      const shown = shiftHandoverUnfinishedWorkOrderStatusLabel(option.code)
      expect(shown).not.toBe(option.code)
      expect(shown).toMatch(/[一-龥]/)
    }
    // 不在值域里的码不回吐原值，回落到中文。
    expect(shiftHandoverUnfinishedWorkOrderStatusLabel('Planned')).toBe('未知状态')
    expect(shiftHandoverUnfinishedWorkOrderStatusLabel('OnHold')).toBe('未知状态')
  })
})
