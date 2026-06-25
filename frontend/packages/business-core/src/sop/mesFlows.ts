import { defineStepFlow } from './defineStepFlow'

export interface ReportCtx {
  workOrderId?: string
  operationTaskId?: string
  quantityEntered?: boolean
  recorded?: boolean
}

export interface ReceiptCtx {
  workOrderId?: string
  skuId?: string
  quantityEntered?: boolean
  unitCostEntered?: boolean
  created?: boolean
}

export const productionReportFlow = defineStepFlow<ReportCtx>({
  id: 'mes.report',
  steps: [
    { id: 'selectWorkOrder', done: (c) => Boolean(c.workOrderId) },
    { id: 'selectOperation', done: (c) => Boolean(c.operationTaskId) },
    { id: 'enterQuantity', done: (c) => Boolean(c.quantityEntered) },
    { id: 'record', done: (c) => Boolean(c.recorded) },
  ],
})

export const finishedGoodsReceiptFlow = defineStepFlow<ReceiptCtx>({
  id: 'mes.receipt',
  steps: [
    { id: 'selectWorkOrder', done: (c) => Boolean(c.workOrderId) },
    { id: 'enterSkuQuantity', done: (c) => Boolean(c.skuId && c.quantityEntered && c.unitCostEntered) },
    { id: 'create', done: (c) => Boolean(c.created) },
  ],
})
