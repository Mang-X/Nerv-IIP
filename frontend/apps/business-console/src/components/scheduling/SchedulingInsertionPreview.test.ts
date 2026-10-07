import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import SchedulingInsertionPreview from './SchedulingInsertionPreview.vue'

const backend = vi.hoisted(() => ({
  history: vi.fn(),
  accept: vi.fn(),
  status: vi.fn(),
  notify: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleSchedulingPlanHistoryQueryOptions: ({ query }: { query: unknown }) => ({
    key: ['plans', query],
    query: backend.history,
  }),
  acceptBusinessConsoleSchedulingInsertionPreviewJobMutationOptions: () => ({
    mutation: backend.accept,
  }),
  getBusinessConsoleSchedulingInsertionPreviewJobQueryOptions: ({
    path,
  }: {
    path: { jobId: string }
  }) => ({ key: ['job', path.jobId], query: backend.status }),
}))
vi.mock('@/utils/notify', () => ({ notifyOperationFailure: backend.notify }))
vi.mock('./SchedulingPreviewResult.vue', () => ({
  default: { props: ['plan'], template: '<div>插单预览结果 {{ plan.status }}</div>' },
}))
const context = { organizationId: 'org-001', environmentId: 'env-dev' }
function setup(canManage = true) {
  return mount(SchedulingInsertionPreview, {
    props: { workOrderId: 'WO-12', context, canManage },
    global: {
      plugins: [
        createPinia(),
        [PiniaColada, { plugins: [PiniaColadaAutoRefetch({ autoRefetch: false })] }],
      ],
    },
  })
}
function button(wrapper: ReturnType<typeof setup>, text: string) {
  return wrapper.findAll('button').find((item) => item.text().includes(text))!
}
beforeEach(() => {
  vi.useFakeTimers()
  backend.history.mockReset().mockResolvedValue({
    success: true,
    data: { total: 1, items: [{ planId: 'APS-11', status: 'generated', assignmentCount: 11 }] },
  })
  backend.accept.mockReset().mockResolvedValue({
    success: true,
    data: {
      jobId: 'job-1',
      status: 'created',
      input: {
        ...context,
        planId: 'APS-11',
        workOrderId: 'WO-12',
        workOrderIds: Array.from({ length: 12 }, (_, i) => `WO-${i + 1}`),
        horizonStartUtc: '2026-10-07T00:00:00Z',
        horizonEndUtc: '2026-10-14T00:00:00Z',
      },
    },
  })
  backend.status
    .mockReset()
    .mockResolvedValue({ success: true, data: { jobId: 'job-1', status: 'running' } })
  backend.notify.mockReset()
})
afterEach(() => vi.useRealTimers())
describe('#4163 页内插单操作（DomainInvariant，真实组件 / HTTP 桩）', () => {
  it('选择方案与固定工单发起重预览，显示真实排队和完整预览，不把结果当保存方案', async () => {
    const wrapper = setup()
    await flushPromises()
    expect(button(wrapper, '插入该单并重预览').attributes('disabled')).toBeDefined()
    await button(wrapper, 'APS-11').trigger('click')
    expect(wrapper.text()).toContain('APS-11 ＋ 工单 WO-12')
    expect(wrapper.text()).toContain('合并去重后最多 500 单')
    await button(wrapper, '插入该单并重预览').trigger('click')
    await flushPromises()
    expect(backend.accept.mock.calls[0]?.[0]).toEqual({
      body: { ...context, planId: 'APS-11', workOrderId: 'WO-12' },
    })
    expect(wrapper.text()).toContain('计算中')
    backend.status.mockResolvedValue({
      success: true,
      data: { jobId: 'job-1', status: 'completed', preview: { status: 'preview' } },
    })
    await vi.advanceTimersByTimeAsync(1000)
    await flushPromises()
    expect(wrapper.text()).toContain('预览完成')
    expect(wrapper.text()).toContain('插单预览结果 preview')
    wrapper.unmount()
  })
  it('只读仍可选看方案，管理动作禁用且不发写请求', async () => {
    const wrapper = setup(false)
    await flushPromises()
    await button(wrapper, 'APS-11').trigger('click')
    expect(wrapper.text()).toContain('没有排产管理权限')
    expect(button(wrapper, '插入该单并重预览').attributes('disabled')).toBeDefined()
    await button(wrapper, '插入该单并重预览').trigger('click')
    expect(backend.accept).not.toHaveBeenCalled()
    wrapper.unmount()
  })
  it.each([
    '合并去重后工单总数不能超过500单。',
    '方案缺少问题快照，请重新生成方案后重试。',
    '没有权限执行此操作。',
  ])('受理失败 %s 由中文通知反馈，仍保留所选方案', async (message) => {
    const failure = { message, status: message.includes('权限') ? 403 : 400 }
    backend.accept.mockRejectedValue(failure)
    const wrapper = setup()
    await flushPromises()
    await button(wrapper, 'APS-11').trigger('click')
    await button(wrapper, '插入该单并重预览').trigger('click')
    await flushPromises()
    expect(backend.notify).toHaveBeenCalledWith(
      '插单预览失败',
      failure,
      '插单预览失败，请稍后重试。',
    )
    expect(wrapper.text()).toContain('APS-11 ＋ 工单 WO-12')
    expect(backend.status).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
