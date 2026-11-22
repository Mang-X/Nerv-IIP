import {
  stub,
  associatedOrderIds,
  firstPlanJob,
  generationError,
  capacityCandidates,
  planOne,
  layoutStub,
} from './scheduling.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import SchedulingPage from './scheduling.vue'

describe('APS scheduling workbench page', () => {
  it('按用户指定的窗口生成首版，而不是提交时现算的固定 7 天', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()

    wrapper
      .findComponent({ name: 'SchedulingOrderPool' })
      .vm.$emit('include', ['WO-20260701-001'], true)
    await flushPromises()

    // 把窗口换成 1 天：生成请求必须跟着变。
    const horizon = wrapper.findComponent({ name: 'SchedulingHorizonFields' })
    horizon.vm.$emit('update:modelValue', {
      ...(horizon.props('modelValue') as Record<string, unknown>),
      mode: 'preset',
      days: 1,
    })
    await flushPromises()

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('生成首版'))!
      .trigger('click')
    await flushPromises()

    const body = stub.generatePlan.mock.calls.at(-1)?.[0] as {
      horizonStartUtc: string
      horizonEndUtc: string
    }
    const span =
      (new Date(body.horizonEndUtc).getTime() - new Date(body.horizonStartUtc).getTime()) /
      86_400_000
    expect(span).toBe(1)
  })
})

describe('异步首版页面选择容量（#4137 DomainInvariant）', () => {
  it('单选与全部加入拒绝第 501 单，500 单提交使用完整选单', async () => {
    capacityCandidates.value = Array.from({ length: 501 }, (_, i) => ({
      workOrderId: `WO-${i}`,
      productionVersionId: 'PV-001',
      status: 'released',
      priority: 100,
    }))
    const wrapper = mount(SchedulingPage, {
      // 页面边界测完整选单与提交；真实 500/501 checkbox 由 SchedulingOrderPool.test.ts 覆盖。
      global: {
        plugins: [createPinia()],
        stubs: {
          ...layoutStub,
          SchedulingOrderPool: {
            name: 'SchedulingOrderPool',
            props: ['candidates', 'draftOrders'],
            emits: ['include'],
            template: '<div />',
          },
          SchedulingDraftBoard: {
            name: 'SchedulingDraftBoard',
            props: ['model'],
            template: '<div />',
          },
        },
      },
    })
    try {
      await flushPromises()
      const pool = wrapper.findComponent({ name: 'SchedulingOrderPool' })
      const generate = () => wrapper.findAll('button').find((b) => b.text().includes('生成首版'))!
      pool.vm.$emit(
        'include',
        capacityCandidates.value.map((o) => o.workOrderId!),
        true,
      )
      await flushPromises()
      expect(stub.toastError).toHaveBeenCalledWith('首版排程最多选择 500 单，请先移出超出的工单')
      expect(generate().attributes('disabled')).toBeDefined()
      pool.vm.$emit(
        'include',
        capacityCandidates.value.slice(0, 500).map((o) => o.workOrderId!),
        true,
      )
      await flushPromises()
      pool.vm.$emit('include', ['WO-500'], true)
      await flushPromises()
      expect(
        pool.props('draftOrders').filter((o: { included: boolean }) => o.included),
      ).toHaveLength(500)
      await generate().trigger('click')
      await flushPromises()
      expect(stub.generatePlan.mock.calls.at(-1)?.[0].orders).toHaveLength(500)
      expect(
        wrapper
          .findComponent({ name: 'SchedulingDraftBoard' })
          .props('model')
          .tasks.some((task: { orderId: string }) => task.orderId === 'WO-20260701-001'),
      ).toBe(true)
    } finally {
      wrapper.unmount()
    }
  })
})

describe('500 单首版关联事实复用（#4137 Regression）', () => {
  it('使用待排池已返回的商业来源，历史池外工单仍按 ID 读取', async () => {
    capacityCandidates.value = [
      {
        workOrderId: 'WO-20260701-001',
        productionVersionId: 'PV-001',
        status: 'released',
        commercialSourceFacts: null,
      },
    ]
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    try {
      await flushPromises()
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      await wrapper
        .findAll('button')
        .find((b) => b.text().includes('生成首版'))!
        .trigger('click')
      await flushPromises()
      expect(associatedOrderIds()).not.toContain('WO-20260701-001')
      expect(associatedOrderIds()).toContain('WO-20260701-002')
    } finally {
      wrapper.unmount()
    }
  })
})

describe('异步首版最终状态与草稿一致性（#4137 Regression）', () => {
  it('完成后方案加载失败仍显示已完成，并提示查看已有方案', async () => {
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    try {
      await flushPromises()
      firstPlanJob.value = { status: 'completed', planId: 'plan-001' }
      generationError.value = new Error('HTTP 503')
      await flushPromises()
      expect(wrapper.text()).toContain('首版排程已完成，方案加载失败')
      expect(wrapper.text()).not.toContain('重新生成')
      expect(stub.toastError).toHaveBeenCalledWith(expect.stringContaining('方案加载失败'))
    } finally {
      wrapper.unmount()
    }
  })

  it('等待首版时禁止撤销选单、切换或清空草稿，完成后选单与任务一致', async () => {
    let complete!: (plan: typeof planOne) => void
    stub.generatePlan.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          complete = resolve
        }),
    )
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    try {
      await flushPromises()
      const pool = wrapper.findComponent({ name: 'SchedulingOrderPool' })
      pool.vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
      const button = (text: string) =>
        wrapper.findAll('button').find((b) => b.text().trim() === text)!
      expect(button('撤销').attributes('disabled')).toBeUndefined()
      await button('生成首版').trigger('click')
      await flushPromises()
      expect(button('撤销').attributes('disabled')).toBeDefined()
      expect(button('重做').attributes('disabled')).toBeDefined()
      expect(button('清空草稿').attributes('disabled')).toBeDefined()
      expect(pool.props('readOnly')).toBe(true)
      await button('撤销').trigger('click')
      expect(
        pool.props('draftOrders').filter((o: { included: boolean }) => o.included),
      ).toHaveLength(1)
      complete({
        ...planOne,
        assignments: planOne.assignments.filter((a) => a.orderId === 'WO-20260701-001'),
      })
      await flushPromises()
      expect(pool.props('readOnly')).toBe(false)
      expect(button('生成首版').attributes('disabled')).toBeUndefined()
      expect(
        pool
          .props('draftOrders')
          .find((o: { workOrderId: string }) => o.workOrderId === 'WO-20260701-001').included,
      ).toBe(true)
      expect(
        wrapper
          .findComponent({ name: 'SchedulingDraftBoard' })
          .props('model')
          .tasks.some((t: { orderId: string }) => t.orderId === 'WO-20260701-001'),
      ).toBe(true)
    } finally {
      wrapper.unmount()
    }
  })
})
