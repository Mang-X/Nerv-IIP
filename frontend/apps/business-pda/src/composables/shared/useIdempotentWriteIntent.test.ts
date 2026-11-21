import { describe, expect, it } from 'vitest'
import { useIdempotentWriteIntent } from './useIdempotentWriteIntent'

describe('useIdempotentWriteIntent', () => {
  it('locks a dispatched write when a structured HTTP 5xx leaves the result unknown', () => {
    const intent = useIdempotentWriteIntent<{ quantity: number }>(() => 'intent-1')
    intent.start()
    intent.payload(() => ({ quantity: 8 }))
    intent.markCommandAttempt()

    intent.recordFailure({ statusCode: 503, message: '服务暂不可用' }, '提交失败')

    expect(intent.locked.value).toBe(true)
    expect(intent.key.value).toBe('intent-1')
  })

  it('keeps a dispatched write editable after a determinate HTTP 422', () => {
    const intent = useIdempotentWriteIntent<{ quantity: number }>(() => 'intent-1')
    intent.start()
    intent.markCommandAttempt()

    intent.recordFailure({ response: { status: 422 }, message: '数量无效' }, '提交失败')

    expect(intent.locked.value).toBe(false)
  })
})
