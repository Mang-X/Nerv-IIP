import { describe, expect, it } from 'vitest'
import {
  materialIssueStatusLabel,
  operationTaskStatusLabel,
  receiptStatusLabel,
  workOrderStatusLabel,
  workOrderSubtitle,
  workOrderTitle,
} from './mesLabels'

describe('workOrderStatusLabel', () => {
  it('maps known work-order statuses to Chinese labels', () => {
    expect(workOrderStatusLabel('Released')).toBe('已下达')
    expect(workOrderStatusLabel('Planned')).toBe('已计划')
    expect(workOrderStatusLabel('InProgress')).toBe('生产中')
    expect(workOrderStatusLabel('Started')).toBe('生产中')
    expect(workOrderStatusLabel('Completed')).toBe('已完成')
    expect(workOrderStatusLabel('Closed')).toBe('已关闭')
    expect(workOrderStatusLabel('OnHold')).toBe('已挂起')
  })

  it('falls back to 未知状态 for unknown / missing status', () => {
    expect(workOrderStatusLabel('Nope')).toBe('未知状态')
    expect(workOrderStatusLabel(undefined)).toBe('未知状态')
    expect(workOrderStatusLabel(null)).toBe('未知状态')
    expect(workOrderStatusLabel('')).toBe('未知状态')
  })
})

describe('operationTaskStatusLabel', () => {
  it('maps known operation-task statuses to Chinese labels', () => {
    expect(operationTaskStatusLabel('Ready')).toBe('可开工')
    expect(operationTaskStatusLabel('Running')).toBe('执行中')
    expect(operationTaskStatusLabel('Started')).toBe('执行中')
    expect(operationTaskStatusLabel('InProgress')).toBe('执行中')
    expect(operationTaskStatusLabel('Paused')).toBe('已暂停')
    expect(operationTaskStatusLabel('Held')).toBe('已暂停')
    expect(operationTaskStatusLabel('ScheduleInvalidated')).toBe('排程已失效')
    expect(operationTaskStatusLabel('Completed')).toBe('已完成')
    expect(operationTaskStatusLabel('Blocked')).toBe('受阻')
  })

  it('falls back to 未知状态 for unknown / missing status', () => {
    expect(operationTaskStatusLabel('Nope')).toBe('未知状态')
    expect(operationTaskStatusLabel(undefined)).toBe('未知状态')
    expect(operationTaskStatusLabel(null)).toBe('未知状态')
  })
})

describe('materialIssueStatusLabel', () => {
  it('maps known material-issue statuses to Chinese labels', () => {
    expect(materialIssueStatusLabel('Requested')).toBe('待领料')
    expect(materialIssueStatusLabel('Pending')).toBe('待领料')
    expect(materialIssueStatusLabel('Issued')).toBe('已发料')
    expect(materialIssueStatusLabel('PartiallyReceived')).toBe('部分接收')
    expect(materialIssueStatusLabel('Received')).toBe('已接收')
    expect(materialIssueStatusLabel('Confirmed')).toBe('已接收')
    expect(materialIssueStatusLabel('Completed')).toBe('已完成')
    expect(materialIssueStatusLabel('Cancelled')).toBe('已取消')
    expect(materialIssueStatusLabel('Rejected')).toBe('已驳回')
  })

  it('falls back to 未知状态 for unknown / missing status', () => {
    expect(materialIssueStatusLabel('Nope')).toBe('未知状态')
    expect(materialIssueStatusLabel(undefined)).toBe('未知状态')
    expect(materialIssueStatusLabel(null)).toBe('未知状态')
  })
})

describe('receiptStatusLabel', () => {
  it('maps known receipt statuses to Chinese labels', () => {
    expect(receiptStatusLabel('Requested')).toBe('待入库')
    expect(receiptStatusLabel('Pending')).toBe('待入库')
    expect(receiptStatusLabel('Created')).toBe('待入库')
    expect(receiptStatusLabel('Submitted')).toBe('待入库')
    expect(receiptStatusLabel('PartiallyReceived')).toBe('部分入库')
    expect(receiptStatusLabel('Received')).toBe('已入库')
    expect(receiptStatusLabel('Completed')).toBe('已入库')
    expect(receiptStatusLabel('Cancelled')).toBe('已取消')
    expect(receiptStatusLabel('Rejected')).toBe('已驳回')
  })

  it('falls back to 未知状态 for unknown / missing status', () => {
    expect(receiptStatusLabel('Nope')).toBe('未知状态')
    expect(receiptStatusLabel(undefined)).toBe('未知状态')
    expect(receiptStatusLabel(null)).toBe('未知状态')
  })
})

describe('workOrderTitle / workOrderSubtitle', () => {
  it('renders the work-order id as title, 无工单 when missing', () => {
    expect(workOrderTitle({ workOrderId: 'WO-001' })).toBe('WO-001')
    expect(workOrderTitle({})).toBe('无工单')
  })

  it('joins status with optional sku and quantity in the subtitle', () => {
    expect(workOrderSubtitle({ status: 'Released', skuId: 'SKU-1', quantity: 10 }))
      .toBe('已下达 · 物料 SKU-1 · 计划 10')
    expect(workOrderSubtitle({ status: 'Planned' })).toBe('已计划')
    expect(workOrderSubtitle({ status: 'Released', quantity: 0 })).toBe('已下达 · 计划 0')
  })
})
