import {
  andonSdk,
  startTask,
  operationScopeMessageRef,
  operationScopeReadyRef,
  defaultTasks,
  operationTasksRef,
  resetFixture,
} from './operation.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import OperationPage from './operation.vue'

describe('PDA MES operation execution page', () => {
  beforeEach(resetFixture)

  it.each(['MATERIAL_SHORTAGE', 'EQUIPMENT_UNAVAILABLE', 'QUALITY_HOLD'])(
    'allows assistance during %s without opening a production action',
    async (reason) => {
      operationTasksRef.value = [
        { ...defaultTasks[0], status: 'Blocked', allowedActions: [], blockReasons: [reason] },
      ]
      const wrapper = mount(OperationPage, { attachTo: document.body })
      await flushPromises()
      await wrapper.get('[data-row]').trigger('click')
      await flushPromises()
      const quality = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => button.textContent?.trim() === '质量呼叫',
      )!
      quality.click()
      await flushPromises()
      const submit = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
        (button) => button.textContent?.trim() === '发起呼叫',
      )!
      submit.click()
      await flushPromises()
      expect(document.body.textContent).toContain('呼叫已确认')
      expect(document.body.textContent).not.toContain('andon-created-1')
      expect(andonSdk.raise.mock.calls[0][0].body).toMatchObject({
        workOrderId: 'WO-2026-0001',
        operationTaskId: 'OP-1',
        workCenterId: 'WC-A',
        scopeId: 'WC-A',
      })
      expect(startTask).not.toHaveBeenCalled()
    },
  )

  it('does not offer a call when the manage scope is not verified', async () => {
    operationScopeReadyRef.value = false
    operationScopeMessageRef.value = '当前账号没有呼叫权限或作业范围尚未就绪'
    const wrapper = mount(OperationPage, { attachTo: document.body })
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()
    const quality = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '质量呼叫',
    )!
    expect(quality.disabled).toBe(true)
    expect(document.body.textContent).toContain('当前账号没有呼叫权限或作业范围尚未就绪')
    expect(andonSdk.raise).not.toHaveBeenCalled()
  })
})
