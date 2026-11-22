import {
  inventoryState,
  routeState,
  mountInventoryPage,
  openCreateCountTask,
  openCountTaskRow,
  recountRequiredCountTaskRow,
  resetFixture,
} from './inventory-workflow.fixture'
import { flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { beforeEach, describe, expect, it } from 'vitest'
import AvailabilityPage from './availability.vue'
import CountsPage from './counts.vue'

describe('inventory workflow pages', () => {
  beforeEach(resetFixture)

  it('uses design-system table components for the stock count read face', () => {
    const wrapper = mountInventoryPage(CountsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  it('shows count-only operation reasons and the honest missing-reason fallback', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    const row = inventoryState.availabilityRows!.value[0]!
    row.movementAllowed = true
    row.countAllowed = false
    row.countBlockReason = '该定位存在多个效期台账，请缩小盘点范围。'
    await nextTick()

    expect(wrapper.get('[data-operation-block-reason]').text()).toBe(
      '该定位存在多个效期台账，请缩小盘点范围。',
    )

    row.countBlockReason = undefined
    await nextTick()
    expect(wrapper.get('[data-operation-block-reason]').text()).toBe(
      '该库存行暂不能创建盘点，请稍后重试或联系管理员。',
    )
  })

  it('generates a fresh idempotency key each time the same count task is adjusted', async () => {
    // 盘点任务行来自服务端读面，不再是「本次提交后塞进本地队列」的那一行。
    inventoryState.countTaskRows = [openCountTaskRow('COUNT-TASK-1')]
    inventoryState.confirmAdjustment.mockResolvedValue({ data: { movementId: 'MOVE-1' } })

    const wrapper = mountInventoryPage(CountsPage)

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认差异'))!
      .trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('5')
    await wrapper.findAll('form')[1]!.trigger('submit')

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('确认差异'))!
      .trigger('click')
    await wrapper.find('#count-adjust-quantity').setValue('6')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).toHaveBeenCalledTimes(2)
    const firstKey = inventoryState.confirmAdjustment.mock.calls[0][1].idempotencyKey
    const secondKey = inventoryState.confirmAdjustment.mock.calls[1][1].idempotencyKey
    expect(firstKey).toMatch(/^count-COUNT-TASK-1-\d+-\d+$/)
    expect(secondKey).toMatch(/^count-COUNT-TASK-1-\d+-\d+$/)
    expect(secondKey).not.toBe(firstKey)
  })

  it('offers recount as the way out of a recount-required count task', async () => {
    inventoryState.countTaskRows = [recountRequiredCountTaskRow('COUNT-TASK-STUCK')]
    inventoryState.restartCountTask.mockResolvedValue({ data: { status: 'open' } })

    const wrapper = mountInventoryPage(CountsPage)
    const recount = wrapper.findAll('button').find((button) => button.text().includes('重盘'))!

    expect(recount.attributes('disabled')).toBeUndefined()
    await recount.trigger('click')
    await flushPromises()

    expect(inventoryState.restartCountTask).toHaveBeenCalledWith('COUNT-TASK-STUCK')
    expect(inventoryState.notifySuccess).toHaveBeenCalled()
  })

  it('also offers recount to an open task whose snapshot drifted', async () => {
    // 台账在快照之后被改动时确认必被拒，而拒绝会回滚事务，任务留在「待实盘」——同样是死单。
    inventoryState.countTaskRows = [openCountTaskRow('COUNT-TASK-DRIFTED')]
    inventoryState.restartCountTask.mockResolvedValue({ data: { status: 'open' } })

    const wrapper = mountInventoryPage(CountsPage)
    const recount = wrapper.findAll('button').find((button) => button.text().includes('重盘'))!

    expect(recount.attributes('disabled')).toBeUndefined()
    await recount.trigger('click')
    await flushPromises()

    expect(inventoryState.restartCountTask).toHaveBeenCalledWith('COUNT-TASK-DRIFTED')
  })

  it.each(['confirmed', 'cancelled', 'pending-approval'])(
    'keeps recount disabled for %s tasks',
    async (status) => {
      inventoryState.countTaskRows = [{ ...openCountTaskRow(`COUNT-TASK-${status}`), status }]

      const wrapper = mountInventoryPage(CountsPage)
      const recount = wrapper.findAll('button').find((button) => button.text().includes('重盘'))!

      expect(recount.attributes('disabled')).toBeDefined()
      await recount.trigger('click')
      await flushPromises()

      expect(inventoryState.restartCountTask).not.toHaveBeenCalled()
    },
  )

  it('keeps close disabled for a pending-approval task so the approval write-back cannot poison', async () => {
    // 作废待审批任务会让随后的审批回写在服务端抛异常、从消费者逃逸成毒消息。
    inventoryState.countTaskRows = [
      { ...openCountTaskRow('COUNT-TASK-PENDING'), status: 'pending-approval' },
    ]

    const wrapper = mountInventoryPage(CountsPage)
    const close = wrapper.findAll('button').find((button) => button.text().includes('关闭任务'))!

    expect(close.attributes('disabled')).toBeDefined()
    await close.trigger('click')
    await flushPromises()

    // 弹框外壳在本文件是桩（slot 直出），所以只断言动作没被触发，不看弹框在不在。
    expect(inventoryState.cancelCountTask).not.toHaveBeenCalled()
  })

  it('closes a count task with a required reason', async () => {
    inventoryState.countTaskRows = [recountRequiredCountTaskRow('COUNT-TASK-CLOSE')]
    inventoryState.cancelCountTask.mockResolvedValue({ data: { status: 'cancelled' } })

    const wrapper = mountInventoryPage(CountsPage)
    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('关闭任务'))!
      .trigger('click')
    await flushPromises()

    // 原因是必填：没填就不发请求。
    const closeForm = wrapper.findAll('form').at(-1)!
    await closeForm.trigger('submit')
    await flushPromises()
    expect(inventoryState.cancelCountTask).not.toHaveBeenCalled()

    await wrapper.find('#count-close-reason').setValue('盘点范围调整，任务不再执行')
    await closeForm.trigger('submit')
    await flushPromises()

    expect(inventoryState.cancelCountTask).toHaveBeenCalledWith(
      'COUNT-TASK-CLOSE',
      '盘点范围调整，任务不再执行',
    )
  })

  it('盘点创建内容冲突交给现有反馈链，保留填写内容供用户处理', async () => {
    const refusal = {
      success: false,
      message: '本次提交与此前的创建内容不一致，请重新发起创建。',
    }
    routeState.query = { skuCode: 'SKU-001', locationCode: 'A-01' }
    inventoryState.createCountTask.mockRejectedValueOnce(refusal)
    const wrapper = mountInventoryPage(CountsPage)
    await openCreateCountTask(wrapper)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    expect(inventoryState.notifyOperationFailure).toHaveBeenCalledWith(
      '创建盘点任务失败',
      refusal,
      '创建盘点任务失败，请稍后重试。',
    )
    expect(wrapper.find('#count-task-location').exists()).toBe(true)
    expect(inventoryState.notifySuccess).not.toHaveBeenCalled()
  })

  it('新建盘点任务不填任务号：提交带幂等键，同一次填写重试沿用同一个键，重新打开才换键', async () => {
    routeState.query = { skuCode: 'SKU-001', locationCode: 'A-01' }
    inventoryState.createCountTask
      .mockRejectedValueOnce(new Error('网络中断'))
      .mockResolvedValue({ data: { countTaskId: 'task-1', countTaskCode: 'SCT-20260928-000001' } })
    const wrapper = mountInventoryPage(CountsPage)

    expect(wrapper.find('#count-task-code').exists()).toBe(false)
    await openCreateCountTask(wrapper)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()

    const [firstBody] = inventoryState.createCountTask.mock.calls[0]!
    const [retryBody] = inventoryState.createCountTask.mock.calls[1]!
    expect(firstBody).not.toHaveProperty('countTaskCode')
    expect(firstBody.idempotencyKey).toBeTruthy()
    expect(retryBody.idempotencyKey).toBe(firstBody.idempotencyKey)

    await openCreateCountTask(wrapper)
    await wrapper.findAll('form')[0]!.trigger('submit')
    await flushPromises()
    const [nextBody] = inventoryState.createCountTask.mock.calls[2]!
    expect(nextBody.idempotencyKey).not.toBe(firstBody.idempotencyKey)
  })

  it('requires the adjustment action to be opened from a count task row before submitting', async () => {
    const wrapper = mountInventoryPage(CountsPage)

    // 盘点任务不再是可输入字段：只能由所选任务行带出，没有带出就不该发请求。
    expect(wrapper.find('#count-adjust-task-id').exists()).toBe(false)
    await wrapper.find('#count-adjust-quantity').setValue('7')
    await wrapper.findAll('form')[1]!.trigger('submit')

    expect(inventoryState.confirmAdjustment).not.toHaveBeenCalled()
  })
})
