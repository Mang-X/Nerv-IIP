import { describe, expect, it } from 'vitest'
import { finishedGoodsReceiptFlow, productionReportFlow } from './mesFlows'

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
    expect(productionReportFlow.progress({ workOrderId: 'WO-1', operationTaskId: 'OP-1' })).toEqual({
      completed: 2,
      total: 4,
    })
  })
})

describe('finishedGoodsReceiptFlow', () => {
  it('starts at selectWorkOrder and requires sku, quantity, and unit cost before create', () => {
    expect(finishedGoodsReceiptFlow.currentStep({}).id).toBe('selectWorkOrder')
    expect(finishedGoodsReceiptFlow.currentStep({ workOrderId: 'WO-1' }).id).toBe('enterSkuQuantity')
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
    ).toBe('enterSkuQuantity')
    expect(
      finishedGoodsReceiptFlow.currentStep({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
        unitCostEntered: true,
      }).id,
    ).toBe('create')
  })

  it('is complete only after create is done', () => {
    expect(
      finishedGoodsReceiptFlow.isComplete({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
        unitCostEntered: true,
      }),
    ).toBe(false)
    expect(
      finishedGoodsReceiptFlow.isComplete({
        workOrderId: 'WO-1',
        skuId: 'SKU-1',
        quantityEntered: true,
        unitCostEntered: true,
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
