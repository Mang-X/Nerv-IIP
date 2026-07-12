import { describe, expect, it } from 'vitest'
import {
  alarmLifecycleSortWeight,
  alarmLifecycleStatusLabel,
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

describe('alarmLifecycleStatusLabel', () => {
  it('maps known lifecycle statuses to Chinese (mirrors AlarmEvent.Status)', () => {
    expect(alarmLifecycleStatusLabel('raised')).toBe('未确认')
    expect(alarmLifecycleStatusLabel('acknowledged')).toBe('已确认')
    expect(alarmLifecycleStatusLabel('shelved')).toBe('已搁置')
    expect(alarmLifecycleStatusLabel('cleared')).toBe('已清除')
  })

  it('is case-insensitive and falls back for unknown / empty values', () => {
    expect(alarmLifecycleStatusLabel('RAISED')).toBe('未确认')
    expect(alarmLifecycleStatusLabel('weird')).toBe('未知状态')
    expect(alarmLifecycleStatusLabel(null)).toBe('未知状态')
    expect(alarmLifecycleStatusLabel(undefined)).toBe('未知状态')
  })
})

describe('alarmLifecycleSortWeight', () => {
  it('orders 未确认 > 已搁置 > 已确认 > 已清除', () => {
    expect(alarmLifecycleSortWeight('raised')).toBeLessThan(alarmLifecycleSortWeight('shelved'))
    expect(alarmLifecycleSortWeight('shelved')).toBeLessThan(
      alarmLifecycleSortWeight('acknowledged'),
    )
    expect(alarmLifecycleSortWeight('acknowledged')).toBeLessThan(
      alarmLifecycleSortWeight('cleared'),
    )
  })

  it('sorts unknown statuses after known active/handled tiers but before cleared', () => {
    expect(alarmLifecycleSortWeight('weird')).toBeGreaterThan(
      alarmLifecycleSortWeight('acknowledged'),
    )
    expect(alarmLifecycleSortWeight('weird')).toBeLessThan(alarmLifecycleSortWeight('cleared'))
    expect(alarmLifecycleSortWeight('RAISED')).toBe(alarmLifecycleSortWeight('raised'))
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
