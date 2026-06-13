import { describe, expect, it } from 'vitest'
import {
  alarmSeverityLabel,
  equipmentStateLabel,
  inspectionResultLabel,
  maintenancePriorityLabel,
  maintenanceWorkOrderStatusLabel,
} from './equipmentLabels'

describe('alarmSeverityLabel', () => {
  it('maps known severities to Chinese (mirrors PC useBusinessEquipment)', () => {
    expect(alarmSeverityLabel('critical')).toBe('严重')
    expect(alarmSeverityLabel('blocked')).toBe('阻塞')
    expect(alarmSeverityLabel('warning')).toBe('预警')
    expect(alarmSeverityLabel('info')).toBe('信息')
  })

  it('is case-insensitive', () => {
    expect(alarmSeverityLabel('CRITICAL')).toBe('严重')
  })

  it('falls back for unknown / empty values', () => {
    expect(alarmSeverityLabel('boom')).toBe('未知级别')
    expect(alarmSeverityLabel(null)).toBe('未知级别')
    expect(alarmSeverityLabel(undefined)).toBe('未知级别')
  })
})

describe('equipmentStateLabel', () => {
  it('maps known states to Chinese (mirrors PC equipment pages)', () => {
    expect(equipmentStateLabel('running')).toBe('运行中')
    expect(equipmentStateLabel('idle')).toBe('空闲')
    expect(equipmentStateLabel('down')).toBe('停机')
    expect(equipmentStateLabel('faulted')).toBe('故障')
    expect(equipmentStateLabel('offline')).toBe('离线')
    expect(equipmentStateLabel('ready')).toBe('就绪')
    expect(equipmentStateLabel('stopped')).toBe('停止')
  })

  it('falls back for unknown / empty values', () => {
    expect(equipmentStateLabel('weird')).toBe('未知状态')
    expect(equipmentStateLabel(null)).toBe('未知状态')
  })
})

describe('maintenancePriorityLabel', () => {
  it('maps known priorities to Chinese', () => {
    expect(maintenancePriorityLabel('high')).toBe('高')
    expect(maintenancePriorityLabel('medium')).toBe('中')
    expect(maintenancePriorityLabel('low')).toBe('低')
  })

  it('falls back for unknown / empty values', () => {
    expect(maintenancePriorityLabel('urgent')).toBe('未知优先级')
    expect(maintenancePriorityLabel(undefined)).toBe('未知优先级')
  })
})

describe('maintenanceWorkOrderStatusLabel', () => {
  it('maps known work order statuses to Chinese', () => {
    expect(maintenanceWorkOrderStatusLabel('open')).toBe('待处理')
    expect(maintenanceWorkOrderStatusLabel('inProgress')).toBe('处理中')
    expect(maintenanceWorkOrderStatusLabel('completed')).toBe('已完成')
    expect(maintenanceWorkOrderStatusLabel('closed')).toBe('已关闭')
    expect(maintenanceWorkOrderStatusLabel('cancelled')).toBe('已取消')
  })

  it('falls back for unknown / empty values', () => {
    expect(maintenanceWorkOrderStatusLabel('frozen')).toBe('未知状态')
    expect(maintenanceWorkOrderStatusLabel(null)).toBe('未知状态')
  })
})

describe('inspectionResultLabel', () => {
  it('maps known results to Chinese', () => {
    expect(inspectionResultLabel('pass')).toBe('通过')
    expect(inspectionResultLabel('fail')).toBe('不通过')
  })

  it('falls back for unknown / empty values', () => {
    expect(inspectionResultLabel('maybe')).toBe('未知结果')
    expect(inspectionResultLabel(undefined)).toBe('未知结果')
  })
})
