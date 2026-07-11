import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { describe, expect, it, vi } from 'vitest'
import { useNonIdempotentWriteResult } from './useNonIdempotentWriteResult'

function make(onVerify: () => void = vi.fn()) {
  return useNonIdempotentWriteResult({
    failureTitle: '报修提交失败',
    verifyListLabel: '近期维修工单',
    verifyVerb: '创建',
    onVerify,
  })
}

describe('useNonIdempotentWriteResult', () => {
  it('run success → success phase', async () => {
    const write = make()
    const ok = await write.run(async () => ({}))
    expect(ok).toBe(true)
    expect(write.phase.value).toBe('success')
  })

  it('DISPATCHED timeout → indeterminate: no retry, "提交结果未知" + 勿重复提交/核实', async () => {
    const write = make()
    const ok = await write.run(async () => {
      throw new RequestTimeoutError()
    })
    expect(ok).toBe(false)
    expect(write.phase.value).toBe('error')
    expect(write.canRetry.value).toBe(false)
    expect(write.errorTitle.value).toBe('提交结果未知')
    expect(write.errorDescription.value).toContain('请勿重复提交')
    expect(write.errorDescription.value).toContain('核实是否已创建')
  })

  it('OFFLINE pre-check (never dispatched) → safe to retry', async () => {
    const write = make()
    await write.run(async () => {
      throw new OfflineError()
    })
    expect(write.canRetry.value).toBe(true)
    expect(write.errorTitle.value).toBe('报修提交失败')
    expect(write.errorDescription.value).toContain('当前离线')
  })

  it('determinate business error → safe to retry + surfaces the server message', async () => {
    const write = make()
    await write.run(async () => {
      throw { message: '设备不存在' }
    })
    expect(write.canRetry.value).toBe(true)
    expect(write.errorDescription.value).toBe('设备不存在')
  })

  it('retry returns to the form', async () => {
    const write = make()
    await write.run(async () => {
      throw { message: 'x' }
    })
    write.retry()
    expect(write.phase.value).toBe('form')
    expect(write.lastError.value).toBe(null)
  })

  it('verify refreshes the list and returns to the form WITHOUT resubmitting', async () => {
    const onVerify = vi.fn()
    const write = make(onVerify)
    await write.run(async () => {
      throw new RequestTimeoutError()
    })
    write.verify()
    expect(onVerify).toHaveBeenCalledTimes(1)
    expect(write.phase.value).toBe('form')
    expect(write.lastError.value).toBe(null)
  })
})
