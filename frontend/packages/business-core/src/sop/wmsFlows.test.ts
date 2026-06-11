import { describe, expect, it } from 'vitest'
import { countExecutionFlow, inboundReceiveFlow, outboundReviewFlow } from './wmsFlows'

describe('wms PDA step flows', () => {
  it('inbound: order selected → complete', () => {
    expect(inboundReceiveFlow.currentStep({}).id).toBe('selectOrder')
    expect(inboundReceiveFlow.isComplete({ orderId: 'IB1', completed: true })).toBe(true)
    expect(inboundReceiveFlow.progress({ orderId: 'IB1' })).toEqual({ completed: 1, total: 2 })
  })
  it('outbound: order → packReviewNo → complete', () => {
    expect(outboundReviewFlow.currentStep({ orderId: 'OB1' }).id).toBe('enterReviewNo')
    expect(outboundReviewFlow.isComplete({ orderId: 'OB1', packReviewNo: 'PR1', completed: true })).toBe(true)
  })
  it('count: execution selected → count entered → complete', () => {
    expect(countExecutionFlow.currentStep({}).id).toBe('selectExecution')
    expect(countExecutionFlow.currentStep({ countExecutionId: 'CE1' }).id).toBe('enterCount')
    expect(countExecutionFlow.currentStep({ countExecutionId: 'CE1', countEntered: true }).id).toBe('complete')
    expect(countExecutionFlow.isComplete({ countExecutionId: 'CE1', countEntered: true, completed: true })).toBe(true)
    expect(countExecutionFlow.progress({ countExecutionId: 'CE1' })).toEqual({ completed: 1, total: 3 })
  })
})
