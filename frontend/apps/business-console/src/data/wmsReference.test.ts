import { describe, expect, it } from 'vitest'
import {
  WMS_COUNT_EXECUTION_STATUS_OPTIONS,
  WMS_INBOUND_ORDER_STATUS_OPTIONS,
  WMS_OUTBOUND_ORDER_STATUS_OPTIONS,
  WMS_WAREHOUSE_TASK_STATUS_OPTIONS,
  wmsCountExecutionStatusLabel,
  wmsInboundOrderStatusLabel,
  wmsOutboundOrderStatusLabel,
  wmsWarehouseTaskStatusLabel,
} from './wmsReference'

describe('WMS PC 服务端状态契约', () => {
  it('为四类资源保留各自真实枚举，不复用相似状态集合', () => {
    expect(WMS_WAREHOUSE_TASK_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      'Open',
      'InProgress',
      'Completed',
      'CompletedWithDifference',
      'Exception',
      'Cancelled',
    ])
    expect(WMS_COUNT_EXECUTION_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      'Open',
      'Completed',
    ])
    expect(WMS_INBOUND_ORDER_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      'Open',
      'PendingQualityCheck',
      'Completed',
      'InventoryPostingFailed',
      'Cancelled',
    ])
    expect(WMS_OUTBOUND_ORDER_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      'Open',
      'InventoryPostingPending',
      'Completed',
      'InventoryPostingFailed',
      'Cancelled',
    ])
  })

  it('所有真实枚举都有中文标签，不回落英文状态码', () => {
    const labels = [
      ...WMS_WAREHOUSE_TASK_STATUS_OPTIONS.map(({ value }) => wmsWarehouseTaskStatusLabel(value)),
      ...WMS_COUNT_EXECUTION_STATUS_OPTIONS.map(({ value }) => wmsCountExecutionStatusLabel(value)),
      ...WMS_INBOUND_ORDER_STATUS_OPTIONS.map(({ value }) => wmsInboundOrderStatusLabel(value)),
      ...WMS_OUTBOUND_ORDER_STATUS_OPTIONS.map(({ value }) => wmsOutboundOrderStatusLabel(value)),
    ]

    expect(labels).not.toContain('InventoryPostingPending')
    expect(labels).not.toContain('InventoryPostingFailed')
    expect(labels.every((label) => /[\u4e00-\u9fff]/u.test(label))).toBe(true)
  })
})
