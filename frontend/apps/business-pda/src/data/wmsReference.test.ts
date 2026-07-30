import { describe, expect, it } from 'vitest'
import {
  PDA_COUNT_EXECUTION_STATUS_OPTIONS,
  PDA_INBOUND_ORDER_STATUS_OPTIONS,
  PDA_OUTBOUND_ORDER_STATUS_OPTIONS,
  PDA_WAREHOUSE_TASK_STATUS_OPTIONS,
} from './wmsReference'

describe('WMS PDA 服务端状态契约', () => {
  it('按资源暴露完整且互不复用的状态集合', () => {
    expect(PDA_WAREHOUSE_TASK_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      '',
      'Open',
      'InProgress',
      'Completed',
      'CompletedWithDifference',
      'Exception',
      'Cancelled',
    ])
    expect(PDA_COUNT_EXECUTION_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      '',
      'Open',
      'Completed',
    ])
    expect(PDA_INBOUND_ORDER_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      '',
      'Open',
      'PendingQualityCheck',
      'Completed',
      'InventoryPostingFailed',
      'Cancelled',
    ])
    expect(PDA_OUTBOUND_ORDER_STATUS_OPTIONS.map(({ value }) => value)).toEqual([
      '',
      'Open',
      'InventoryPostingPending',
      'Completed',
      'InventoryPostingFailed',
      'Cancelled',
    ])
  })
})
