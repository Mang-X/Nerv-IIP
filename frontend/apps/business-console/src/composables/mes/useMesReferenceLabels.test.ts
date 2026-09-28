import { describe, expect, it } from 'vitest'

import {
  isFailedReceiptStatus,
  mesDowntimeStatusOptions,
  mesHandoverStatusOptions,
  mesOperationTaskStatusOptions,
  mesQualityStatusOptions,
  mesWorkOrderStatusOptions,
  receiptStatusLabel,
  receiptStatusTone,
  useMesReferenceLabels,
} from './useMesReferenceLabels'

/**
 * 查表不做大小写归一化，这里逐条钉住这个不变量（#3912 / #3898）。
 *
 * 旧实现把每个 key 展开成「原形 / 首字母大写 / 全小写」三重，运行时值命中哪一重都能查到标签。
 * 那种写法会掩盖后端哪天改发另一种拼写 —— 前端静默继续接受，没有任何类型或测试会红。
 * 真实值是各聚合域常量的字面量：工单小写，其余（工序 / 领料单 / 完工入库单 / 不良记录 /
 * 交接班 / 停机产能）都是 PascalCase。所以小写码必须落空，而不是被吸收。
 *
 * 断言一律打 `useMesReferenceLabels().statusLabel` 这条**生产渲染路径**，不走任何
 * 只读表的旁路导出：只要有人把归一化加回 `statusLabel`，这里立刻红。
 */
describe('MES 状态词表不吸收契约漂移', () => {
  const { statusLabel } = useMesReferenceLabels()

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

  it('工单筛选项是小写的完整值域，与 work-orders 契约一致', () => {
    const values = mesWorkOrderStatusOptions.map((option) => option.value)
    // 10 个值 = MES 域 WorkOrder.AllStatuses 全集 = 契约里 work-orders 与
    // production-plans 两条 status 查询参数的枚举。`split` / `merged` 曾被漏掉
    // （域里有可达赋值，契约也列了，只有下拉少两项），这里逐个钉住。
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
      'split',
      'merged',
    ])
    expect(values).not.toContain('Created')
  })

  it('生产渲染路径对两种拼写只认真实值域那一侧', () => {
    // 走 statusLabel（页面真正调用的那个），不是任何测试专用的查表入口。
    // 逐个聚合各钉一对：一个真实值命中标签 + 它的另一种拼写落空。这样无论把归一化
    // 加成「先原样、再 lower」（PascalCase 域死、工单域活）还是「先原样、再首字母大写」
    // （工单域活、PascalCase 域死），两条里总有一条会红 —— 早先只钉工单那一对，
    // 归一化恰好只对工单生效时整条断言会一起放过。
    // 查不到时回吐原值（不是空串），所以负例断言的是「原样返回」。
    expect(statusLabel('Queued')).toBe('待开工')
    expect(statusLabel('queued')).toBe('queued')
    expect(statusLabel('InProgress')).toBe('执行中')
    expect(statusLabel('inProgress')).toBe('inProgress')
    expect(statusLabel('ScheduleInvalidated')).toBe('排程已失效')
    expect(statusLabel('scheduleInvalidated')).toBe('scheduleInvalidated')
    // 工单是真小写，PascalCase 变体同样必须查不到。
    expect(statusLabel('created')).toBe('已创建')
    expect(statusLabel('Created')).toBe('Created')
    // 领料单与完工入库是另外两个 PascalCase 域，各钉一对。
    expect(statusLabel('PartiallyReceived')).toBe('部分接收')
    expect(statusLabel('partiallyReceived')).toBe('partiallyReceived')
    expect(statusLabel('InventoryPostingFailed')).toBe('入库失败')
    expect(statusLabel('inventoryPostingFailed')).toBe('inventoryPostingFailed')
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

  it('Open 是跨聚合重码，三个语境各印各的说法', () => {
    // 共享词表只放停机语境的「未恢复」，其余两个语境靠 overrides 覆盖。这三条
    // 是 overrides 唯一的防线：删掉任何一个 override，页面照单全收、测试全绿，
    // 但不良记录会印成「未恢复」、交接班会印成「未恢复」。所以逐语境钉住。
    const labelOf = (options: { value: string; label: string }[], value: string) =>
      options.find((option) => option.value === value)?.label

    expect(labelOf(mesQualityStatusOptions, 'Open')).toBe('待处理')
    expect(labelOf(mesHandoverStatusOptions, 'Open')).toBe('待接班')
    expect(labelOf(mesDowntimeStatusOptions, 'Open')).toBe('未恢复')
  })

  it('完工入库徽章色按运行时 PascalCase 取', () => {
    // 徽章色与标签是两回事：标签错了看得见，色调错了（已入库印成红色告警）同样
    // 是契约漂移的表现，只是更难发现，所以单列一条。
    expect(receiptStatusTone('Posted')).toBe('success')
    expect(receiptStatusTone('InventoryPostingFailed')).toBe('danger')
    expect(receiptStatusTone('PartiallyPosted')).toBe('info')
    expect(receiptStatusTone('Requested')).toBe('neutral')
    // 小写不得命中，否则漂移又被静默吸收。
    expect(receiptStatusTone('posted')).toBe('neutral')
  })
})
