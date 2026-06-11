import { describe, expect, it } from 'vitest'
import {
  countExecutionStatusLabel,
  inboundOrderStatusLabel,
  outboundOrderStatusLabel,
  warehouseTaskStatusLabel,
} from './wmsLabels'

describe('WMS status labels (Chinese, no engineering codes leak to UI)', () => {
  it('maps warehouse task statuses', () => {
    expect(warehouseTaskStatusLabel('pending')).toBe('待执行')
    expect(warehouseTaskStatusLabel('inProgress')).toBe('执行中')
    expect(warehouseTaskStatusLabel('completed')).toBe('已完成')
    expect(warehouseTaskStatusLabel('cancelled')).toBe('已取消')
  })

  it('maps count execution statuses', () => {
    expect(countExecutionStatusLabel('pending')).toBe('待盘点')
    expect(countExecutionStatusLabel('inProgress')).toBe('盘点中')
    expect(countExecutionStatusLabel('completed')).toBe('已完成')
    expect(countExecutionStatusLabel('cancelled')).toBe('已取消')
  })

  it('maps inbound order statuses', () => {
    expect(inboundOrderStatusLabel('open')).toBe('待入库')
    expect(inboundOrderStatusLabel('inProgress')).toBe('入库中')
    expect(inboundOrderStatusLabel('completed')).toBe('已入库')
    expect(inboundOrderStatusLabel('closed')).toBe('已关闭')
    expect(inboundOrderStatusLabel('cancelled')).toBe('已取消')
  })

  it('maps outbound order statuses', () => {
    expect(outboundOrderStatusLabel('open')).toBe('待发货')
    expect(outboundOrderStatusLabel('inProgress')).toBe('发货中')
    expect(outboundOrderStatusLabel('completed')).toBe('已发货')
    expect(outboundOrderStatusLabel('closed')).toBe('已关闭')
    expect(outboundOrderStatusLabel('cancelled')).toBe('已取消')
  })

  it('falls back to 未知状态 for unknown / empty codes (case-insensitive)', () => {
    expect(warehouseTaskStatusLabel('COMPLETED')).toBe('已完成')
    expect(warehouseTaskStatusLabel('nope')).toBe('未知状态')
    expect(countExecutionStatusLabel(undefined)).toBe('未知状态')
    expect(inboundOrderStatusLabel('')).toBe('未知状态')
    expect(outboundOrderStatusLabel(null)).toBe('未知状态')
  })
})
