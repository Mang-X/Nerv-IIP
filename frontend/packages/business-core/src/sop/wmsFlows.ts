import { defineStepFlow } from './defineStepFlow'

export interface InboundReceiveCtx {
  orderId?: string
  completed?: boolean
}

export const inboundReceiveFlow = defineStepFlow<InboundReceiveCtx>({
  id: 'wms.inbound.receive',
  steps: [
    { id: 'selectOrder', done: (c) => Boolean(c.orderId) },
    { id: 'complete', done: (c) => Boolean(c.completed) },
  ],
})

export interface CountExecCtx {
  countExecutionId?: string
  countEntered?: boolean
  completed?: boolean
}

export const countExecutionFlow = defineStepFlow<CountExecCtx>({
  id: 'wms.count',
  steps: [
    { id: 'selectExecution', done: (c) => Boolean(c.countExecutionId) },
    { id: 'enterCount', done: (c) => Boolean(c.countEntered) },
    { id: 'complete', done: (c) => Boolean(c.completed) },
  ],
})
