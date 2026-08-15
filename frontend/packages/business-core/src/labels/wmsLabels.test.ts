import { describe, expect, it } from 'vitest'
import {
  countExecutionStatusLabel,
  inboundOrderStatusLabel,
  outboundOrderStatusLabel,
  warehouseTaskStatusLabel,
} from './wmsLabels'

describe('WMS status labels (Chinese, no engineering codes leak to UI)', () => {
  it('maps warehouse task statuses', () => {
    expect(warehouseTaskStatusLabel('Open')).toBe('待执行')
    expect(warehouseTaskStatusLabel('InProgress')).toBe('执行中')
    expect(warehouseTaskStatusLabel('Completed')).toBe('已完成')
    expect(warehouseTaskStatusLabel('CompletedWithDifference')).toBe('差异完成')
    expect(warehouseTaskStatusLabel('Exception')).toBe('异常待处理')
    expect(warehouseTaskStatusLabel('Cancelled')).toBe('已取消')
  })

  it('maps count execution statuses', () => {
    expect(countExecutionStatusLabel('Open')).toBe('待盘点')
    expect(countExecutionStatusLabel('Completed')).toBe('已完成')
  })

  it('maps inbound order statuses', () => {
    expect(inboundOrderStatusLabel('Open')).toBe('待收货')
    expect(inboundOrderStatusLabel('Completed')).toBe('已完成')
    expect(inboundOrderStatusLabel('InventoryPostingFailed')).toBe('库存过账失败')
    expect(inboundOrderStatusLabel('PendingQualityCheck')).toBe('待质检')
    expect(inboundOrderStatusLabel('Cancelled')).toBe('已取消')
  })

  it('maps outbound order statuses', () => {
    expect(outboundOrderStatusLabel('Open')).toBe('待复核发货')
    expect(outboundOrderStatusLabel('Completed')).toBe('已完成')
    expect(outboundOrderStatusLabel('InventoryPostingFailed')).toBe('库存过账失败')
    expect(outboundOrderStatusLabel('Cancelled')).toBe('已取消')
    expect(outboundOrderStatusLabel('InventoryPostingPending')).toBe('库存过账中')
  })

  it('falls back to 未知状态 for unknown / empty codes (case-insensitive)', () => {
    expect(warehouseTaskStatusLabel('COMPLETED')).toBe('已完成')
    expect(warehouseTaskStatusLabel('nope')).toBe('未知状态')
    expect(countExecutionStatusLabel(undefined)).toBe('未知状态')
    expect(inboundOrderStatusLabel('')).toBe('未知状态')
    expect(outboundOrderStatusLabel(null)).toBe('未知状态')
  })
})
