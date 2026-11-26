import { wms, routeGuardState, layoutStub, setInput, resetFixture } from './wms-operate.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { BusinessOperationPendingError } from '@nerv-iip/api-client'
import { NvDialog, toast } from '@nerv-iip/ui'
import CountsPage from './counts.vue'

describe('WMS operate actions', () => {
  beforeEach(resetFixture)

  // 合同：#3943 已批准范围，断言请求身份而非页面内部状态。
  it.each([
    ['确定性拒绝', { status: 400 }, false],
    ['网络中断', new TypeError('Failed to fetch'), true],
    ['服务端失败', { status: 503 }, true],
    ['请求超时', { status: 408 }, true],
  ])('盘点创建：%s 后同载荷沿用键，编辑后仅确定失败才换键', async (_label, error, unknown) => {
    wms.createCountExecution.mockRejectedValue(error)
    const wrapper = mount(CountsPage, { global: { stubs: layoutStub } })
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '新建盘点单')!
      .trigger('click')
    await flushPromises()
    setInput('#cnt-sku', 'SKU-001')
    setInput('#cnt-site', 'SITE-001')
    setInput('#cnt-location', 'A-01')
    setInput('#cnt-lot', 'LOT-COUNT-01')
    setInput('#cnt-expected', '5')
    await flushPromises()
    const form = document.querySelector<HTMLFormElement>('#cnt-expected')!.closest('form')!
    const submit = async () => {
      form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
      await flushPromises()
    }
    await submit()
    const original = { ...wms.createCountExecution.mock.calls[0]![0] }
    expect(original.lotNo).toBe('LOT-COUNT-01')
    expect(document.querySelector<HTMLInputElement>('#cnt-lot')!.disabled).toBe(unknown)
    await submit()
    expect(wms.createCountExecution.mock.calls[1]![0]).toEqual(original)
    setInput('#cnt-expected', '5.0')
    await flushPromises()
    await submit()
    expect(wms.createCountExecution.mock.calls[2]![0]).toEqual(original)
    setInput('#cnt-expected', '6')
    await flushPromises()
    await submit()
    const edited = wms.createCountExecution.mock.calls[3]![0]
    if (unknown) {
      expect(edited).toEqual(original)
      const dialog = wrapper.findAllComponents(NvDialog).find((d) => d.props('open'))!
      dialog.vm.$emit('update:open', false)
      await flushPromises()
      expect(dialog.props('open')).toBe(true)
      expect(routeGuardState.guard?.()).toBe(false)
    } else {
      expect(edited.expectedQuantity).toBe(6)
      expect(edited.idempotencyKey).not.toBe(original.idempotencyKey)
    }
    wms.createCountExecution.mockResolvedValue(undefined)
    await submit()
    expect(wms.createCountExecution.mock.calls[4]![0]).toEqual(edited)
    expect(routeGuardState.guard?.()).toBe(true)
    expect(wrapper.findAllComponents(NvDialog).some((d) => d.props('open'))).toBe(false)
    await wrapper
      .findAll('button')
      .find((b) => b.text() === '新建盘点单')!
      .trigger('click')
    await flushPromises()
    expect(document.querySelector<HTMLInputElement>('#cnt-lot')!.disabled).toBe(false)
    wrapper.unmount()
  })

  it('盘点只读用户仍可查看列表，但不显示新建与完成动作', async () => {
    wms.permissionCodes = ['business.wms.counts.read']
    const wrapper = mount(CountsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('CNT-1')
    expect(wrapper.text()).not.toContain('新建盘点单')
    expect(wrapper.find('button[aria-label="盘点操作 CNT-1"]').exists()).toBe(false)
  })

  it('acknowledges pending approval and refreshes the count list', async () => {
    wms.completeCountExecution.mockRejectedValueOnce(
      new BusinessOperationPendingError('wms.count-execution.complete'),
    )
    const wrapper = mount(CountsPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await wrapper.get('button[aria-label="盘点操作 CNT-1"]').trigger('click')
    await flushPromises()
    const completeItem = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (item) => item.textContent?.includes('完成盘点'),
    )!
    completeItem.click()
    await flushPromises()
    wms.refreshCountExecutions.mockClear()
    setInput('#cnt-counted', '8')
    document.body
      .querySelector('#cnt-counted')!
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(toast.warning).toHaveBeenCalledWith('实盘已提交，待审批。')
    expect(toast.success).not.toHaveBeenCalled()
    expect(toast.error).not.toHaveBeenCalled()
    expect(wms.refreshCountExecutions).toHaveBeenCalled()
    expect(wrapper.findAllComponents(NvDialog).some((dialog) => dialog.props('open'))).toBe(false)
    wrapper.unmount()
  })

  it.each([true, false])(
    '显示原盘点结果并按权限提供复盘入口，库存盘点权限 %s',
    async (canRecount) => {
      if (!canRecount) wms.permissionCodes = ['business.wms.counts.read']
      wms.countRows = [
        { countExecutionId: 'count-1', countNo: 'CNT-1', status: 'RecountRequired' },
        {
          countExecutionId: 'count-2',
          countNo: 'CNT-2',
          status: 'Completed',
          inventoryMovementId: 'MOV-2',
        },
      ]
      const wrapper = mount(CountsPage, {
        global: { stubs: layoutStub },
      })
      await flushPromises()

      expect(wrapper.text()).toContain('库存流水 MOV-2')
      const links = wrapper.findAll('[data-router-link]')
      if (canRecount) {
        const link = links.find((entry) => entry.text() === '打开原盘点任务')
        expect(JSON.parse(link?.attributes('data-to') ?? 'null')).toEqual({
          path: '/inventory/counts',
          query: { countTaskCode: 'CNT-1' },
        })
      } else {
        expect(links.some((entry) => entry.text() === '打开原盘点任务')).toBe(false)
      }
    },
  )

  it('keeps an indeterminate count intent frozen when the dialog requests close', async () => {
    wms.completeCountExecution.mockImplementationOnce(
      (
        _id: string,
        _countedQuantity: number,
        _key: string,
        options?: { onCommandAttempt?: () => void },
      ) => {
        options?.onCommandAttempt?.()
        return Promise.reject(new Error('network interrupted'))
      },
    )
    const wrapper = mount(CountsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.get('button[aria-label="盘点操作 CNT-1"]').trigger('click')
    await flushPromises()
    const completeItem = [...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')].find(
      (item) => item.textContent?.includes('完成盘点'),
    )
    expect(completeItem).toBeTruthy()
    completeItem!.click()
    await flushPromises()

    expect(document.body.textContent).toContain('LOT-COUNT-01')
    const input = document.body.querySelector<HTMLInputElement>('#cnt-counted')!
    input.value = '8'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    const form = [...document.body.querySelectorAll<HTMLFormElement>('form')].find((candidate) =>
      candidate.querySelector('#cnt-counted'),
    )!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    const completeDialog = wrapper
      .findAllComponents(NvDialog)
      .find((dialog) => dialog.props('open'))
    expect(completeDialog).toBeTruthy()
    completeDialog!.vm.$emit('update:open', false)
    await flushPromises()
    expect(completeDialog!.props('open')).toBe(true)
    const cancel = [...document.body.querySelectorAll<HTMLButtonElement>('button')].find(
      (button) => button.textContent?.trim() === '取消',
    )
    expect(cancel?.disabled).toBe(true)
    expect(input.disabled).toBe(true)
    expect(routeGuardState.guard?.()).toBe(false)

    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(wms.completeCountExecution).toHaveBeenNthCalledWith(
      2,
      'count-1',
      8,
      'wms-intent-1',
      expect.objectContaining({ attempt: 'retry' }),
    )
  })

  it('creates a count for the selected lot and shows that lot in the list', async () => {
    const wrapper = mount(CountsPage, { global: { stubs: layoutStub } })
    await flushPromises()
    expect(wrapper.text()).toContain('LOT-COUNT-01')
    await wrapper
      .findAll('button')
      .find((b) => b.text().includes('新建盘点单'))!
      .trigger('click')
    await flushPromises()
    setInput('#cnt-sku', 'SKU-001')
    setInput('#cnt-site', 'SITE-001')
    setInput('#cnt-location', 'RACK-A-01')
    setInput('#cnt-lot', 'LOT-COUNT-01')
    setInput('#cnt-expected', '15')
    await flushPromises()
    document.body
      .querySelector<HTMLFormElement>('form:has(#cnt-expected)')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(wms.createCountExecution).toHaveBeenCalledWith(
      expect.objectContaining({
        lotNo: 'LOT-COUNT-01',
        expectedQuantity: 15,
      }),
    )
  })
})
