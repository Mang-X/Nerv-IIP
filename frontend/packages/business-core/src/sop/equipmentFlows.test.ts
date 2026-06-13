import { describe, expect, it } from 'vitest'
import { inspectionFlow, repairOrderFlow, type InspectCtx, type RepairCtx } from './equipmentFlows'

describe('repairOrderFlow', () => {
  it('starts at selectDevice and advances as the context fills', () => {
    expect(repairOrderFlow.id).toBe('equipment.repair')
    expect(repairOrderFlow.currentStep({}).id).toBe('selectDevice')
    expect(repairOrderFlow.currentStep({ deviceAssetId: 'DEV-OIL-01' }).id).toBe('fillDetails')
    expect(
      repairOrderFlow.currentStep({ deviceAssetId: 'DEV-OIL-01', priority: 'high' }).id,
    ).toBe('create')
  })

  it('reports completion only when the work order is created', () => {
    const ready: RepairCtx = { deviceAssetId: 'DEV-OIL-01', priority: 'high' }
    expect(repairOrderFlow.isComplete(ready)).toBe(false)
    expect(repairOrderFlow.isComplete({ ...ready, created: true })).toBe(true)
  })

  it('exposes ordered progress for the step indicator', () => {
    expect(repairOrderFlow.progress({ deviceAssetId: 'DEV-OIL-01' })).toEqual({
      completed: 1,
      total: 3,
    })
  })
})

describe('inspectionFlow', () => {
  it('starts at selectPlan and advances as the context fills', () => {
    expect(inspectionFlow.id).toBe('equipment.inspect')
    expect(inspectionFlow.currentStep({}).id).toBe('selectPlan')
    expect(inspectionFlow.currentStep({ planId: 'PLAN-1' }).id).toBe('enterResult')
    expect(inspectionFlow.currentStep({ planId: 'PLAN-1', result: 'pass' }).id).toBe('record')
  })

  it('reports completion only when the inspection is recorded', () => {
    const ready: InspectCtx = { planId: 'PLAN-1', result: 'pass' }
    expect(inspectionFlow.isComplete(ready)).toBe(false)
    expect(inspectionFlow.isComplete({ ...ready, recorded: true })).toBe(true)
  })

  it('exposes ordered progress for the step indicator', () => {
    expect(inspectionFlow.progress({ planId: 'PLAN-1' })).toEqual({ completed: 1, total: 3 })
  })
})
