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
    {
      id: 'enterSkuQuantity',
      done: (c) => Boolean(c.skuId && c.quantityEntered),
    },
    { id: 'create', done: (c) => Boolean(c.created) },
  ],
})

export interface ShiftHandoverCtx {
  shiftId?: string
  teamId?: string
  /**
   * 操作工显式确认过三类明细与附件。
   *
   * 不能拿「有没有明细」当完成判据：空在制清点 / 空未完工单 / 空遗留问题都是合法交班
   * （写面三个数组都可空），那样第 2 步会变成永不可完成的死步。
   */
  reviewed?: boolean
  submitted?: boolean
}

export const shiftHandoverFlow = defineStepFlow<ShiftHandoverCtx>({
  id: 'mes.handover',
  steps: [
    { id: 'selectShiftTeam', done: (c) => Boolean(c.shiftId && c.teamId) },
    { id: 'recordDetails', done: (c) => Boolean(c.reviewed) },
    { id: 'submit', done: (c) => Boolean(c.submitted) },
  ],
})
