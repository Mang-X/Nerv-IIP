import { authState, stub, layoutStub, openPlanTable } from './scheduling.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { describe, expect, it } from 'vitest'
import SchedulingPage from './scheduling.vue'

// DomainInvariant: #3634 与产品文档 §4；页面裁剪不替代 Gateway 的最终授权。
describe('排产三级权限', () => {
  it.each([
    ['只读', false, false],
    ['管理', true, false],
    ['发布', true, true],
  ] as const)('%s角色按权限开放动作并说明禁用原因', async (_role, manage, release) => {
    authState.permissionCodes = [
      'business.scheduling.plans.read',
      ...(manage ? ['business.scheduling.plans.manage'] : []),
      ...(release ? ['business.scheduling.plans.release'] : []),
    ]
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    if (manage) {
      wrapper
        .findComponent({ name: 'SchedulingOrderPool' })
        .vm.$emit('include', ['WO-20260701-001'], true)
      await flushPromises()
    }
    const generate = wrapper.findAll('button').find((button) => button.text().includes('生成首版'))!
    expect(generate.attributes('disabled') !== undefined).toBe(!manage)
    if (!manage) expect(generate.attributes('title')).toContain('没有排产管理权限')
    expect(wrapper.findComponent({ name: 'SchedulingDraftBoard' }).props('readOnly')).toBe(!manage)

    await openPlanTable(wrapper)
    const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-001'))!
    const publish = row.findAll('button').find((button) => button.text().trim() === '发布')!
    expect(publish.attributes('disabled') !== undefined).toBe(!release)
    expect(publish.attributes('title')).toContain(release ? '发布该排程方案' : '没有排程发布权限')
    const released = wrapper
      .findAll('tbody tr')
      .find((item) => item.text().includes('plan-released'))!
    expect(released.findAll('button').some((button) => button.text().includes('撤销发布'))).toBe(
      release,
    )
    wrapper.unmount()
  })

  it('Gateway 拒绝发布时显示中文权限原因', async () => {
    stub.releasePlan.mockRejectedValueOnce({
      success: false,
      code: 403,
      message: 'Forbidden.',
      data: [],
    })
    const wrapper = mount(SchedulingPage, {
      global: { plugins: [createPinia()], stubs: layoutStub },
    })
    await flushPromises()
    await openPlanTable(wrapper)
    const row = wrapper.findAll('tbody tr').find((item) => item.text().includes('plan-001'))!
    await row
      .findAll('button')
      .find((button) => button.text().trim() === '发布')!
      .trigger('click')
    await flushPromises()
    expect(stub.toastError).toHaveBeenCalledWith('发布失败：没有权限执行此操作。')
    wrapper.unmount()
  })
})
