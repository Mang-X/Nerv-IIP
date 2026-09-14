import { describe, expect, it } from 'vitest'
import {
  materialIssueStatusLabel,
  operationTaskStatusLabel,
  receiptStatusLabel,
  SHIFT_HANDOVER_ISSUE_CATEGORY_CODES,
  SHIFT_HANDOVER_ISSUE_SEVERITY_CODES,
  SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS,
  shiftHandoverUnfinishedWorkOrderStatusLabel,
  shiftHandoverIssueCategoryLabel,
  shiftHandoverIssueSeverityLabel,
  shiftHandoverStatusLabel,
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
    expect(operationTaskStatusLabel('Queued')).toBe('待开工')
    expect(operationTaskStatusLabel('Ready')).toBe('可开工')
    expect(operationTaskStatusLabel('Running')).toBe('执行中')
    expect(operationTaskStatusLabel('Started')).toBe('执行中')
    expect(operationTaskStatusLabel('InProgress')).toBe('执行中')
    expect(operationTaskStatusLabel('Paused')).toBe('已暂停')
    expect(operationTaskStatusLabel('Held')).toBe('已暂停')
    expect(operationTaskStatusLabel('ScheduleInvalidated')).toBe('排程已失效')
    expect(operationTaskStatusLabel('Completed')).toBe('已完成')
    expect(operationTaskStatusLabel('Cancelled')).toBe('已取消')
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
    expect(workOrderSubtitle({ status: 'Released', skuId: 'SKU-1', quantity: 10 })).toBe(
      '已下达 · 物料 SKU-1 · 计划 10',
    )
    expect(workOrderSubtitle({ status: 'Planned' })).toBe('已计划')
    expect(workOrderSubtitle({ status: 'Released', quantity: 0 })).toBe('已下达 · 计划 0')
  })
})

describe('shiftHandoverStatusLabel', () => {
  it('maps the two MES ShiftHandover statuses to 交接语境 Chinese', () => {
    // 域权威是 ShiftHandover.OpenStatus / AcceptedStatus；open 在交接语境是「待接班」。
    expect(shiftHandoverStatusLabel('Open')).toBe('待接班')
    expect(shiftHandoverStatusLabel('Accepted')).toBe('已接班')
  })

  it('is case-insensitive because the read face echoes the enum name verbatim', () => {
    expect(shiftHandoverStatusLabel('open')).toBe('待接班')
    expect(shiftHandoverStatusLabel('ACCEPTED')).toBe('已接班')
  })

  it('never leaks an unknown raw status code to the shop floor', () => {
    expect(shiftHandoverStatusLabel('Cancelled')).toBe('未知状态')
    expect(shiftHandoverStatusLabel('')).toBe('未知状态')
    expect(shiftHandoverStatusLabel(undefined)).toBe('未知状态')
    expect(shiftHandoverStatusLabel(null)).toBe('未知状态')
  })
})

describe('shiftHandoverIssueCategoryLabel / shiftHandoverIssueSeverityLabel', () => {
  it('maps the closed domain vocabularies to Chinese', () => {
    expect(shiftHandoverIssueCategoryLabel('Equipment')).toBe('设备')
    expect(shiftHandoverIssueCategoryLabel('Quality')).toBe('质量')
    expect(shiftHandoverIssueSeverityLabel('Low')).toBe('低')
    expect(shiftHandoverIssueSeverityLabel('Medium')).toBe('中')
    expect(shiftHandoverIssueSeverityLabel('High')).toBe('高')
  })

  it('falls back without echoing the raw code', () => {
    expect(shiftHandoverIssueCategoryLabel('Safety')).toBe('未分类')
    expect(shiftHandoverIssueCategoryLabel(null)).toBe('未分类')
    // 回落文案与本模块 alarmSeverityLabel 同为「未知级别」；console 的「未定级」与本文件
    // 早先的「未分级」都是离群值，副本收拢（#3470）以「未知级别」为准。
    expect(shiftHandoverIssueSeverityLabel('Critical')).toBe('未知级别')
    expect(shiftHandoverIssueSeverityLabel(undefined)).toBe('未知级别')
  })

  it('keeps the write-face codes spelled exactly as the MES enum names', () => {
    // ShiftHandoverVocabulary.ParseCategory/ParseSeverity 用 Enum.TryParse + IsDefined，
    // 提交小写也能过，但按枚举名原样提交才与域枚举成员一一对应，避免下次改判据时静默失配。
    expect([...SHIFT_HANDOVER_ISSUE_CATEGORY_CODES]).toEqual(['Equipment', 'Quality'])
    expect([...SHIFT_HANDOVER_ISSUE_SEVERITY_CODES]).toEqual(['Low', 'Medium', 'High'])
    for (const code of SHIFT_HANDOVER_ISSUE_CATEGORY_CODES) {
      expect(shiftHandoverIssueCategoryLabel(code)).not.toBe('未分类')
    }
    for (const code of SHIFT_HANDOVER_ISSUE_SEVERITY_CODES) {
      expect(shiftHandoverIssueSeverityLabel(code)).not.toBe('未知级别')
    }
  })
})

describe('SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS', () => {
  it('is a WRITE-face value domain spelled the way the contract spells it', () => {
    // 读面表是历史拼写的并集；写面单独定义，且码取契约/域常量的**值**（小写），不是常量名。
    const codes = SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map((o) => o.code)
    expect(codes).toEqual(['created', 'released', 'started', 'hold'])
  })

  it('excludes the terminal statuses — 已完成/已关闭的工单不是未完工单', () => {
    const codes: readonly string[] = SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map(
      (o) => o.code,
    )
    for (const terminal of ['completed', 'closed', 'cancelled', 'scrapped']) {
      expect(codes).not.toContain(terminal)
    }
  })

  it('never offers two options that read the same on screen', () => {
    const labels = SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map((o) => o.label)
    expect(new Set(labels).size).toBe(labels.length)
  })

  it('resolves every write code through its own label function', () => {
    for (const option of SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS) {
      expect(shiftHandoverUnfinishedWorkOrderStatusLabel(option.code)).toBe(option.label)
    }
  })

  it('falls back to Chinese instead of echoing an unknown code', () => {
    expect(shiftHandoverUnfinishedWorkOrderStatusLabel('Planned')).toBe('未知状态')
    expect(shiftHandoverUnfinishedWorkOrderStatusLabel(undefined)).toBe('未知状态')
  })
})
