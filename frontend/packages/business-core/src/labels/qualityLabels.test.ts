import { describe, expect, it } from 'vitest'
import {
  INSPECTION_PLAN_CATEGORIES,
  INSPECTION_TASK_SOURCE_TYPES,
  inspectionPlanCategoryLabel,
  inspectionTaskSourceTypeLabel,
  inspectionTaskStatusLabel,
  qualitySourceTypeLabel,
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

describe('qualitySourceTypeLabel', () => {
  it('maps the inspection-record/NCR source types to Chinese', () => {
    expect(qualitySourceTypeLabel('operation')).toBe('工序')
    expect(qualitySourceTypeLabel('first-article')).toBe('首件检验')
    expect(qualitySourceTypeLabel('in-process')).toBe('过程检验')
    expect(qualitySourceTypeLabel('receiving')).toBe('收货')
    expect(qualitySourceTypeLabel('final')).toBe('终检')
    expect(qualitySourceTypeLabel('maintenance')).toBe('维修')
    expect(qualitySourceTypeLabel('customer-return')).toBe('客户退货')
  })

  it('is case-insensitive and keeps unknown codes verbatim', () => {
    expect(qualitySourceTypeLabel('OPERATION')).toBe('工序')
    expect(qualitySourceTypeLabel('未填')).toBe('未填')
    expect(qualitySourceTypeLabel('')).toBe('')
    expect(qualitySourceTypeLabel(undefined)).toBe('')
  })
})

describe('INSPECTION_TASK_SOURCE_TYPES', () => {
  it('lists the backend source types in display order', () => {
    expect(INSPECTION_TASK_SOURCE_TYPES).toEqual(['receiving', 'operation', 'final'])
  })
})

describe('INSPECTION_PLAN_CATEGORIES', () => {
  it('lists only the categories accepted by the Quality domain', () => {
    expect(INSPECTION_PLAN_CATEGORIES).toEqual([
      'receiving',
      'operation',
      'final',
      'first-article',
      'maintenance',
      'customer-return',
    ])
  })

  it('shares one business-label vocabulary for every accepted category', () => {
    expect(INSPECTION_PLAN_CATEGORIES.map(inspectionPlanCategoryLabel)).toEqual([
      '来料检',
      '工序检',
      '终检',
      '首件检验',
      '维修检',
      '客户退货检',
    ])
    expect(inspectionPlanCategoryLabel('future-category')).toBe('future-category')
  })
})
