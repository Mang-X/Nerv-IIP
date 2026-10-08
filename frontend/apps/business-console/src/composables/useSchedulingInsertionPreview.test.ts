import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent, ref } from 'vue'
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSchedulingInsertionPreview } from './useSchedulingInsertionPreview'

const backend = vi.hoisted(() => ({ accept: vi.fn(), status: vi.fn() }))
vi.mock('@nerv-iip/api-client', () => ({
  acceptBusinessConsoleSchedulingInsertionPreviewJobMutationOptions: () => ({
    mutation: backend.accept,
  }),
  getBusinessConsoleSchedulingInsertionPreviewJobQueryOptions: ({
    path,
    query,
  }: {
    path: { jobId: string }
    query: unknown
  }) => ({
    key: ['insertion-preview', path.jobId, query],
    query: backend.status,
  }),
}))
const input = {
  organizationId: 'org-001',
  environmentId: 'env-dev',
  planId: 'PLAN-11',
  workOrderId: 'WO-12',
}
const preview = {
  status: 'preview',
  assignments: [{ orderId: 'WO-12', operationId: 'OP-10' }],
  unscheduledOperations: [{ orderId: 'WO-11', operationId: 'OP-20' }],
}
const job = (status: string) => ({
  success: true,
  data: {
    jobId: 'job-1',
    status,
    input: { ...input, workOrderIds: Array.from({ length: 12 }, (_, i) => `WO-${i + 1}`) },
    preview: status === 'completed' ? preview : null,
    failureReason: status === 'failed' ? '方案缺少问题快照，请重新生成方案后重试。' : null,
  },
})
function setup() {
  let task!: ReturnType<typeof useSchedulingInsertionPreview>
  const enabled = ref(true)
  const wrapper = mount(
    defineComponent({
      setup() {
        task = useSchedulingInsertionPreview(enabled)
        return () => null
      },
    }),
    {
      global: {
        plugins: [
          createPinia(),
          [PiniaColada, { plugins: [PiniaColadaAutoRefetch({ autoRefetch: false })] }],
        ],
      },
    },
  )
  return { task, enabled, wrapper }
}
beforeEach(() => {
  vi.useFakeTimers()
  backend.accept.mockReset().mockResolvedValue(job('created'))
  backend.status.mockReset().mockResolvedValue(job('running'))
})
afterEach(() => vi.useRealTimers())
describe('#4163 插单预览 DomainInvariant / PublicContract（HTTP 桩）', () => {
  it('只提交方案与固定工单，完成显示完整权威 Preview 并停止轮询', async () => {
    const { task, wrapper } = setup()
    await task.start(input)
    expect(backend.accept.mock.calls[0]?.[0]).toEqual({ body: input })
    expect(task.job.value?.status).toBe('created')
    await flushPromises()
    expect(task.job.value?.status).toBe('running')
    backend.status.mockResolvedValue(job('completed'))
    await vi.advanceTimersByTimeAsync(1000)
    await flushPromises()
    expect(task.job.value?.input?.workOrderIds).toHaveLength(12)
    expect(task.preview.value).toEqual(preview)
    expect(task.pending.value).toBe(false)
    const calls = backend.status.mock.calls.length
    await vi.advanceTimersByTimeAsync(5000)
    expect(backend.status).toHaveBeenCalledTimes(calls)
    wrapper.unmount()
  })
  it('失败保留中文原因并停止轮询，不伪造预览', async () => {
    const { task, wrapper } = setup()
    await task.start(input)
    await flushPromises()
    backend.status.mockResolvedValue(job('failed'))
    await vi.advanceTimersByTimeAsync(1000)
    await flushPromises()
    expect((task.error.value as Error).message).toContain('缺少问题快照')
    expect(task.preview.value).toBeUndefined()
    const calls = backend.status.mock.calls.length
    await vi.advanceTimersByTimeAsync(5000)
    expect(backend.status).toHaveBeenCalledTimes(calls)
    wrapper.unmount()
  })
  it('关闭弹窗后清理任务状态并停止查询，重开不会恢复旧轮询', async () => {
    const { task, enabled, wrapper } = setup()
    await task.start(input)
    await flushPromises()
    enabled.value = false
    await flushPromises()
    const calls = backend.status.mock.calls.length
    enabled.value = true
    await vi.advanceTimersByTimeAsync(5000)
    expect(backend.status).toHaveBeenCalledTimes(calls)
    expect(task.job.value).toBeUndefined()
    wrapper.unmount()
  })
  it('HTTP 拒绝信封不上报成功', async () => {
    backend.accept.mockResolvedValue({ success: false, message: '合并去重后超过 500 单。' })
    const { task, wrapper } = setup()
    await expect(task.start(input)).rejects.toThrow('超过 500')
    expect(task.job.value).toBeUndefined()
    wrapper.unmount()
  })
  it('后发任务已受理时，旧任务状态晚到不能覆盖当前候选', async () => {
    let resolveOld!: (value: unknown) => void
    backend.status.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          resolveOld = resolve
        }),
    )
    const { task, wrapper } = setup()
    await task.start(input)
    await flushPromises()
    backend.accept.mockResolvedValue({
      ...job('created'),
      data: { ...job('created').data, jobId: 'job-2', input: { ...input, workOrderId: 'WO-13' } },
    })
    backend.status.mockResolvedValue({
      ...job('running'),
      data: { ...job('running').data, jobId: 'job-2' },
    })
    await task.start({ ...input, workOrderId: 'WO-13' })
    await flushPromises()
    resolveOld(job('completed'))
    await flushPromises()
    expect(task.job.value?.jobId).toBe('job-2')
    expect(task.preview.value).toBeUndefined()
    wrapper.unmount()
  })

  it('完成读回同一任务的 CTP、基线和候选结果，不在前端重新计算', async () => {
    const result = {
      baselinePlanId: input.planId,
      candidatePlanId: 'insertion-job-1',
      promiseUtc: '2026-10-09T10:00:00Z',
      orders: [{ orderId: 'WO-11', status: 'delayed', delayDays: 0.42 }],
    }
    backend.accept.mockResolvedValue({
      ...job('completed'),
      data: { ...job('completed').data, result },
    })
    const { task, wrapper } = setup()
    await task.start(input)
    expect(task.result.value).toEqual(result)
    wrapper.unmount()
  })
})
