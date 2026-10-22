import { describe, expect, it } from 'vitest'
import { finishedGoodsReceiptFlow, productionReportFlow, shiftHandoverFlow } from './mesFlows'

describe('productionReportFlow', () => {
  it('starts at selectWorkOrder and advances as context fills', () => {
    expect(productionReportFlow.currentStep({}).id).toBe('selectWorkOrder')
    expect(productionReportFlow.currentStep({ workOrderId: 'WO-1' }).id).toBe('selectOperation')
    expect(
      productionReportFlow.currentStep({ workOrderId: 'WO-1', operationTaskId: 'OP-1' }).id,
    ).toBe('enterQuantity')
  })

  it('is complete only after every step is done', () => {
    expect(
      productionReportFlow.isComplete({
        workOrderId: 'WO-1',
        operationTaskId: 'OP-1',
        quantityEntered: true,
      }),
    ).toBe(false)
    expect(
      productionReportFlow.isComplete({
        workOrderId: 'WO-1',
        operationTaskId: 'OP-1',
        quantityEntered: true,
        recorded: true,
      }),
    ).toBe(true)
  })

  it('reports ordered progress for the UI step indicator', () => {
    expect(productionReportFlow.progress({ workOrderId: 'WO-1', operationTaskId: 'OP-1' })).toEqual(
      {
        completed: 2,
        total: 4,
      },
    )
  })
})

describe('finishedGoodsReceiptFlow', () => {
  it('starts at selectWorkOrder and requires sku and quantity before create', () => {
    expect(finishedGoodsReceiptFlow.currentStep({}).id).toBe('selectWorkOrder')
    expect(finishedGoodsReceiptFlow.currentStep({ workOrderId: 'WO-1' }).id).toBe(
      'enterSkuQuantity',
    )
    // sku without quantity stays on enterSkuQuantity
    expect(finishedGoodsReceiptFlow.currentStep({ workOrderId: 'WO-1', skuId: 'SKU-1' }).id).toBe(
      'enterSkuQuantity',
    )
    expect(
      finishedGoodsReceiptFlow.currentStep({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
      }).id,
    ).toBe('create')
    expect(
      finishedGoodsReceiptFlow.currentStep({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
      }).id,
    ).toBe('create')
  })

  it('is complete only after create is done', () => {
    expect(
      finishedGoodsReceiptFlow.isComplete({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
      }),
    ).toBe(false)
    expect(
      finishedGoodsReceiptFlow.isComplete({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
        created: true,
      }),
    ).toBe(true)
  })

  it('reports ordered progress for the UI step indicator', () => {
    expect(finishedGoodsReceiptFlow.progress({ workOrderId: 'WO-1' })).toEqual({
      completed: 1,
      total: 3,
    })
  })
})

describe('shiftHandoverFlow', () => {
  it('starts at 选班次班组 and advances only when BOTH shift and team are bound', () => {
    expect(shiftHandoverFlow.currentStep({}).id).toBe('selectShiftTeam')
    expect(shiftHandoverFlow.currentStep({ shiftId: 'EARLY' }).id).toBe('selectShiftTeam')
    expect(shiftHandoverFlow.currentStep({ teamId: 'TEAM-A' }).id).toBe('selectShiftTeam')
    expect(shiftHandoverFlow.currentStep({ shiftId: 'EARLY', teamId: 'TEAM-A' }).id).toBe(
      'recordDetails',
    )
  })

  it('treats an EMPTY handover as a legitimately reviewable one (明细三段可空)', () => {
    // 这一步的完成判据是「操作工确认过」，不是「有没有明细」：写面三个明细数组都可空，
    // 拿数量当判据会让空交班卡在第 2 步永远提交不了。
    expect(
      shiftHandoverFlow.currentStep({ shiftId: 'EARLY', teamId: 'TEAM-A', reviewed: true }).id,
    ).toBe('submit')
  })

  it('is complete only after submit', () => {
    expect(
      shiftHandoverFlow.isComplete({ shiftId: 'EARLY', teamId: 'TEAM-A', reviewed: true }),
    ).toBe(false)
    expect(
      shiftHandoverFlow.isComplete({
        shiftId: 'EARLY',
        teamId: 'TEAM-A',
        reviewed: true,
        submitted: true,
      }),
    ).toBe(true)
  })

  it('reports ordered progress for the UI step indicator', () => {
    expect(shiftHandoverFlow.progress({ shiftId: 'EARLY', teamId: 'TEAM-A' })).toEqual({
      completed: 1,
      total: 3,
    })
  })
})
