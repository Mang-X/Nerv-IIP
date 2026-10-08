import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent, shallowRef } from 'vue'
import { PiniaColada } from '@pinia/colada'
import { beforeEach, expect, it, vi } from 'vitest'
import { useSchedulingRushInsertion } from './useSchedulingRushInsertion'

const backend = vi.hoisted(() => ({ accept: vi.fn(), status: vi.fn(), save: vi.fn() }))
vi.mock('@nerv-iip/api-client', () => ({
  acceptBusinessConsoleSchedulingInsertionPreviewJobMutationOptions: () => ({
    mutation: backend.accept,
  }),
  getBusinessConsoleSchedulingInsertionPreviewJobQueryOptions: () => ({
    key: ['insertion'],
    query: backend.status,
  }),
}))
const context = { organizationId: 'org-001', environmentId: 'env-dev' }
function setup(planId?: string) {
  const baseline = shallowRef(planId ? { planId } : undefined)
  let flow!: ReturnType<typeof useSchedulingRushInsertion>
  const wrapper = mount(
    defineComponent({
      setup() {
        flow = useSchedulingRushInsertion({
          baseline,
          context: () => context,
          enabled: shallowRef(true),
          saveOrder: backend.save,
        })
        return () => null
      },
    }),
    { global: { plugins: [createPinia(), PiniaColada] } },
  )
  return { flow, baseline, wrapper }
}
beforeEach(() => {
  backend.save.mockReset().mockResolvedValue(undefined)
  backend.accept.mockReset().mockImplementation(({ body }) => ({
    success: true,
    data: { jobId: 'job-1', status: 'completed', input: body },
  }))
  backend.status.mockReset()
})
it('急单保存成功后自动受理明确持久化基线，计算完成不发布', async () => {
  const { flow, wrapper } = setup('plan-001')
  await flow.saveOrder('WO-12', { isRush: true, priority: 200 })
  expect(backend.accept.mock.calls[0]?.[0]).toEqual({
    body: { ...context, planId: 'plan-001', workOrderId: 'WO-12' },
  })
  expect(flow.task.job.value?.status).toBe('completed')
  wrapper.unmount()
})
it('保存失败不受理', async () => {
  backend.save.mockRejectedValue(new Error('保存失败'))
  const { flow, wrapper } = setup('plan-001')
  await expect(flow.saveOrder('WO-12', { isRush: true, priority: 200 })).rejects.toThrow('保存失败')
  expect(backend.accept).not.toHaveBeenCalled()
  wrapper.unmount()
})
it('无基线保留已保存急单并页内提示先生成方案，非急单不受理', async () => {
  const { flow, wrapper } = setup()
  await flow.saveOrder('WO-12', { isRush: true, priority: 200 })
  expect(flow.message.value).toContain('先生成方案')
  expect(backend.accept).not.toHaveBeenCalled()
  await flow.saveOrder('WO-12', { isRush: false, priority: 100 })
  expect(backend.accept).not.toHaveBeenCalled()
  wrapper.unmount()
})
it.each([new Error('服务不可用'), { detail: '服务不可用', status: 503 }])(
  '受理失败保留保存结果与后端原因，只有手动重试才再受理同一基线：%s',
  async (failure) => {
    backend.accept.mockRejectedValueOnce(failure)
    const { flow, baseline, wrapper } = setup('plan-001')
    await flow.saveOrder('WO-12', { isRush: true, priority: 200 })
    expect(backend.save).toHaveBeenCalledTimes(1)
    expect(flow.message.value).toContain('服务不可用')
    baseline.value = { planId: 'plan-002' }
    await flow.retry()
    expect(backend.accept.mock.calls[1]?.[0].body.planId).toBe('plan-001')
    wrapper.unmount()
  },
)
it('保存期间切换查阅不会改变受理前已绑定基线', async () => {
  let finish!: () => void
  backend.save.mockImplementationOnce(
    () =>
      new Promise<void>((resolve) => {
        finish = resolve
      }),
  )
  const { flow, baseline, wrapper } = setup('plan-001')
  const saving = flow.saveOrder('WO-12', { isRush: true, priority: 200 })
  baseline.value = { planId: 'plan-002' }
  finish()
  await saving
  expect(backend.accept.mock.calls[0]?.[0].body.planId).toBe('plan-001')
  wrapper.unmount()
})
it('旧保存晚到不能启动任务覆盖后发急单计算', async () => {
  let finishOld!: () => void
  backend.save.mockImplementationOnce(
    () =>
      new Promise<void>((resolve) => {
        finishOld = resolve
      }),
  )
  const { flow, wrapper } = setup('plan-001')
  const old = flow.saveOrder('WO-12', { isRush: true, priority: 200 })
  await flow.saveOrder('WO-13', { isRush: true, priority: 300 })
  finishOld()
  await old
  expect(backend.accept).toHaveBeenCalledTimes(1)
  expect(flow.task.job.value?.input?.workOrderId).toBe('WO-13')
  wrapper.unmount()
})
