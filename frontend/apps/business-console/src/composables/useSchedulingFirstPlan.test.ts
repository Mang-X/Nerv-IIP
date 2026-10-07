import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent } from 'vue'
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSchedulingFirstPlan } from './useSchedulingFirstPlan'

const backend = vi.hoisted(() => ({ accept: vi.fn(), status: vi.fn(), detail: vi.fn() }))
vi.mock('@nerv-iip/api-client', () => ({
  acceptBusinessConsoleSchedulingFirstPlanJobMutationOptions: () => ({ mutation: backend.accept }),
  getBusinessConsoleSchedulingFirstPlanJobQueryOptions: ({
    path,
  }: {
    path: { jobId: string }
  }) => ({
    key: ['first-plan-job', path.jobId],
    query: backend.status,
  }),
  getBusinessConsoleSchedulingPlan: backend.detail,
}))
const input = {
  organizationId: 'org-4137',
  environmentId: 'env-4137',
  orders: [{ workOrderId: 'WO-001' }],
}
const job = (status: string) => ({
  success: true,
  data: {
    jobId: 'job-001',
    status,
    input,
    planId: status === 'completed' ? 'plan-001' : null,
    failureReason: status === 'failed' ? '工单生产版本已停用' : null,
  },
})

beforeEach(() => {
  vi.useFakeTimers()
  backend.accept.mockReset().mockResolvedValue(job('created'))
  backend.status.mockReset().mockResolvedValue(job('running'))
  backend.detail.mockReset().mockResolvedValue({
    data: {
      success: true,
      data: {
        planId: 'plan-001',
        assignments: [{ orderId: 'WO-001', operationId: 'OP-10' }],
        materialShortageSummary: [],
      },
    },
  })
})
afterEach(() => vi.useRealTimers())
function setup() {
  let first!: ReturnType<typeof useSchedulingFirstPlan>
  const completed = vi.fn()
  const wrapper = mount(
    defineComponent({
      setup() {
        first = useSchedulingFirstPlan(completed)
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
  return { first, wrapper, completed }
}
describe('异步首版进度（#4137 DomainInvariant / PublicContract，HTTP 桩）', () => {
  it('受理后显示排队与生成中，完成加载持久化方案并停止轮询', async () => {
    const { first, wrapper, completed } = setup()
    try {
      await first.generatePlan(input)
      expect(first.job.value?.status).toBe('created')
      await flushPromises()
      expect(first.job.value?.status).toBe('running')
      expect(first.pending.value).toBe(true)
      backend.status.mockResolvedValue(job('completed'))
      await vi.advanceTimersByTimeAsync(1000)
      await flushPromises()
      expect(first.plan.value).toMatchObject({
        planId: 'plan-001',
        assignments: [{ orderId: 'WO-001', operationId: 'OP-10' }],
      })
      expect(backend.detail).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { planId: 'plan-001' },
          query: { organizationId: 'org-4137', environmentId: 'env-4137' },
        }),
      )
      expect(completed).toHaveBeenCalledTimes(1)
      expect(first.pending.value).toBe(false)
      const calls = backend.status.mock.calls.length
      await vi.advanceTimersByTimeAsync(5000)
      expect(backend.status).toHaveBeenCalledTimes(calls)
      expect(backend.detail).toHaveBeenCalledTimes(1)
    } finally {
      wrapper.unmount()
    }
  })
  it('失败保留服务端业务原因，终止轮询，不加载虚构方案', async () => {
    const { first, wrapper } = setup()
    try {
      await first.generatePlan(input)
      await flushPromises()
      backend.status.mockResolvedValue(job('failed'))
      await vi.advanceTimersByTimeAsync(1000)
      await flushPromises()
      expect((first.error.value as Error).message).toBe('工单生产版本已停用')
      expect(first.pending.value).toBe(false)
      expect(backend.detail).not.toHaveBeenCalled()
      const calls = backend.status.mock.calls.length
      await vi.advanceTimersByTimeAsync(5000)
      expect(backend.status).toHaveBeenCalledTimes(calls)
    } finally {
      wrapper.unmount()
    }
  })
  it('完成后详情 GET 503 保留 completed 与 planId，停止轮询并报告加载错误', async () => {
    const { first, wrapper, completed } = setup()
    const failure = new Error('HTTP 503')
    backend.detail.mockRejectedValue(failure)
    try {
      await first.generatePlan(input)
      await flushPromises()
      backend.status.mockResolvedValue(job('completed'))
      await vi.advanceTimersByTimeAsync(1000)
      await flushPromises()
      expect(first.job.value).toMatchObject({ status: 'completed', planId: 'plan-001' })
      expect(first.error.value).toBe(failure)
      expect(first.plan.value).toBeUndefined()
      expect(first.pending.value).toBe(false)
      expect(completed).not.toHaveBeenCalled()
      const calls = backend.status.mock.calls.length
      await vi.advanceTimersByTimeAsync(5000)
      expect(backend.status).toHaveBeenCalledTimes(calls)
      expect(backend.detail).toHaveBeenCalledTimes(1)
    } finally {
      wrapper.unmount()
    }
  })
  it('离开工作台后停止轮询', async () => {
    const { first, wrapper } = setup()
    await first.generatePlan(input)
    await flushPromises()
    wrapper.unmount()
    const calls = backend.status.mock.calls.length
    await vi.advanceTimersByTimeAsync(5000)
    expect(backend.status).toHaveBeenCalledTimes(calls)
  })
})
