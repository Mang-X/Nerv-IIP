import { describe, expect, it } from 'vitest'
import { qualityInspectionTaskFlow } from './qualityFlows'

describe('qualityInspectionTaskFlow', () => {
  it('advances selectTask → recordResults → submit as the context fills', () => {
    expect(qualityInspectionTaskFlow.currentStep({}).id).toBe('selectTask')
    expect(qualityInspectionTaskFlow.currentStep({ taskId: 'T1' }).id).toBe('recordResults')
    expect(qualityInspectionTaskFlow.currentStep({ taskId: 'T1', hasResults: true }).id).toBe('submit')
  })

  it('reports completeness and progress', () => {
    expect(qualityInspectionTaskFlow.isComplete({ taskId: 'T1', hasResults: true, submitted: true })).toBe(true)
    expect(qualityInspectionTaskFlow.progress({ taskId: 'T1' })).toEqual({ completed: 1, total: 3 })
    expect(qualityInspectionTaskFlow.progress({})).toEqual({ completed: 0, total: 3 })
  })
})
