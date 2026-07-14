import { describe, expect, it } from 'vitest'
import {
  INSPECTION_TASK_SOURCE_TYPES,
  inspectionTaskSourceTypeLabel,
  inspectionTaskStatusLabel,
} from './qualityLabels'

describe('inspectionTaskStatusLabel', () => {
  it('maps the Quality lifecycle codes to Chinese', () => {
    expect(inspectionTaskStatusLabel('pending')).toBe('待检')
    expect(inspectionTaskStatusLabel('in-progress')).toBe('检验中')
    expect(inspectionTaskStatusLabel('completed')).toBe('已完成')
  })

  it('is case-insensitive and falls back for unknown/empty', () => {
    expect(inspectionTaskStatusLabel('PENDING')).toBe('待检')
    expect(inspectionTaskStatusLabel('')).toBe('未知状态')
    expect(inspectionTaskStatusLabel(undefined)).toBe('未知状态')
  })
})

describe('inspectionTaskSourceTypeLabel', () => {
  it('maps the three source types to Chinese', () => {
    expect(inspectionTaskSourceTypeLabel('receiving')).toBe('来料检')
    expect(inspectionTaskSourceTypeLabel('operation')).toBe('过程检')
    expect(inspectionTaskSourceTypeLabel('final')).toBe('终检')
  })

  it('falls back for unknown source', () => {
    expect(inspectionTaskSourceTypeLabel('mystery')).toBe('其他来源')
  })
})

describe('INSPECTION_TASK_SOURCE_TYPES', () => {
  it('lists the backend source types in display order', () => {
    expect(INSPECTION_TASK_SOURCE_TYPES).toEqual(['receiving', 'operation', 'final'])
  })
})
