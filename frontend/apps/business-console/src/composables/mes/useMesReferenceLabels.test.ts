import { describe, expect, it } from 'vitest'

import {
  isFailedReceiptStatus,
  mesOperationTaskStatusOptions,
  mesWorkOrderStatusOptions,
  receiptStatusLabel,
  statusLabelForTest,
} from './useMesReferenceLabels'

/**
 * 查表不做大小写归一化，这里逐条钉住这个不变量（#3912 / #3898）。
 *
 * 旧实现把每个 key 展开成「原形 / 首字母大写 / 全小写」三重，运行时值命中哪一重都能查到标签。
 * 那种写法会掩盖后端哪天改发另一种拼写 —— 前端静默继续接受，没有任何类型或测试会红。
 * 真实值是各聚合域常量的字面量：工单小写，其余（工序 / 领料单 / 完工入库单 / 不良记录 /
 * 交接班 / 停机产能）都是 PascalCase。所以小写码必须落到未知，而不是被吸收。
 */
describe('MES 状态词表不吸收契约漂移', () => {
  it('完工入库按运行时 PascalCase 查表', () => {
    expect(receiptStatusLabel('Requested')).toBe('待入库')
    expect(receiptStatusLabel('PartiallyPosted')).toBe('部分入库')
    expect(receiptStatusLabel('Posted')).toBe('已入库')
    expect(receiptStatusLabel('InventoryPostingFailed')).toBe('入库失败')
    expect(receiptStatusLabel('Cancelled')).toBe('已取消')
  })

  it('旧契约里的小写完工入库码必须落「未知状态」', () => {
    // 这四个 camelCase 码是展示层处理器把 10 个聚合重填成同一份 29 值并集的产物，运行时不可达。
    // 刻意钉住它们落空：后端若哪天真的开始发小写，这条会红，从而不会把契约漂移藏进适配层。
    expect(receiptStatusLabel('posted')).toBe('未知状态')
    expect(receiptStatusLabel('partiallyPosted')).toBe('未知状态')
    expect(receiptStatusLabel('inventoryPostingFailed')).toBe('未知状态')
    expect(receiptStatusLabel('requested')).toBe('未知状态')
    expect(receiptStatusLabel('cancelled')).toBe('未知状态')
  })

  it('失败判定同样只认运行时 PascalCase', () => {
    expect(isFailedReceiptStatus('InventoryPostingFailed')).toBe(true)
    expect(isFailedReceiptStatus('Posted')).toBe(false)
    // 小写不得命中，否则漂移又被静默吸收。
    expect(isFailedReceiptStatus('inventoryPostingFailed')).toBe(false)
  })

  it('工单筛选项保持小写真实值域，不含 PascalCase 变体', () => {
    const values = mesWorkOrderStatusOptions.map((option) => option.value)
    expect(values).toEqual([
      'all',
      'created',
      'released',
      'started',
      'hold',
      'completed',
      'closed',
      'cancelled',
      'scrapped',
    ])
    expect(values).not.toContain('Created')
  })

  it('共享词表对两种拼写同时返回标签即为归一化层，必须为否', () => {
    // 这条比上面任何一条都强：只要有人把归一化加回 statusLabel，两种拼写就都会命中，
    // 断言立刻红。它直接钉住「不得有大小写兼容层」这个不变量本身，而不是钉住某几个码。
    expect(statusLabelForTest('Queued')).toBe('待开工')
    expect(statusLabelForTest('queued')).toBeUndefined()
    expect(statusLabelForTest('InProgress')).toBe('执行中')
    expect(statusLabelForTest('inProgress')).toBeUndefined()
    expect(statusLabelForTest('created')).toBe('已创建')
    // 工单是真小写，PascalCase 变体同样必须查不到。
    expect(statusLabelForTest('Created')).toBeUndefined()
  })

  it('工序筛选项是 PascalCase 的真实值域，不含小写变体', () => {
    const values = mesOperationTaskStatusOptions.map((option) => option.value)
    expect(values).toEqual([
      'all',
      'Queued',
      'InProgress',
      'Paused',
      'ScheduleInvalidated',
      'Completed',
      'Cancelled',
    ])
    expect(values).not.toContain('queued')
    expect(values).not.toContain('inProgress')
  })
})
