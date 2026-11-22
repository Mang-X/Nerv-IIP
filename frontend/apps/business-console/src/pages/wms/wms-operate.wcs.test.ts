import { wms, layoutStub, resetFixture } from './wms-operate.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import WcsPage from './wcs.vue'

describe('WMS operate actions', () => {
  beforeEach(resetFixture)

  it('renders per-row WCS action menus', async () => {
    const wrapper = mount(WcsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.find('button[aria-label="WCS 任务操作 EXT-1"]').exists()).toBe(true)
  })

  it('offers warehouse work links when the current scope has no device tasks', async () => {
    wms.wcsRows = []
    const wrapper = mount(WcsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('当前范围暂无设备任务')
    expect(wrapper.text()).toContain('上架任务')
    expect(wrapper.text()).toContain('拣货任务')
  })

  describe('WCS 人工处置', () => {
    const wcsStubs = {
      ...layoutStub,
      RowActions: { template: '<div><slot /></div>' },
      NvDropdownMenuItem: {
        props: ['disabled'],
        emits: ['click'],
        template:
          '<button type="button" data-menu-item :disabled="disabled" @click="$emit(\'click\')"><slot /></button>',
      },
    }
    async function openAction(label: string) {
      const wrapper = mount(WcsPage, { global: { stubs: wcsStubs }, attachTo: document.body })
      await flushPromises()
      await wrapper
        .findAll('[data-menu-item]')
        .find((item) => item.text().includes(label))!
        .trigger('click')
      await flushPromises()
      return document.querySelector<HTMLElement>('[role="dialog"]')!
    }
    async function submitDialog(dialog: HTMLElement) {
      dialog
        .querySelector('form')!
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
      await flushPromises()
    }

    it('re-dispatches with the row warehouse-task version and lets the backend resend the original payload', async () => {
      wms.dispatchWcs.mockResolvedValue(undefined)
      const dialog = await openAction('重新下发')

      expect(dialog.querySelector('input')).toBeNull()
      await submitDialog(dialog)

      expect(wms.dispatchWcs).toHaveBeenCalledWith('WT-1', {
        adapterType: 'agv',
        externalTaskId: 'EXT-1',
        expectedVersion: 7,
      })
    })

    it('reports a completion receipt as the cumulative quantity and does not claim a partial one finished', async () => {
      wms.completeWcs.mockResolvedValue(undefined)
      const { toast } = await import('@nerv-iip/ui')
      const dialog = await openAction('标记完成')
      const quantity = dialog.querySelector<HTMLInputElement>('#wcs-completion')!
      expect(quantity.value).toBe('10')

      quantity.value = '6'
      quantity.dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
      await submitDialog(dialog)

      expect(wms.completeWcs).toHaveBeenCalledWith('EXT-1', {
        completionPayloadJson: '{"actualQuantity":6}',
      })
      expect(toast.success).toHaveBeenCalledWith('已记录进度，未完成：累计 6，计划 10')
    })

    it.each([
      ['重新下发', 'wcs-retry-not-due', '还没到允许重新下发的时间'],
      ['重新下发', 'wcs-retry-limit-reached', '重新下发次数已用完'],
      ['重新下发', 'wcs-device-circuit-open', '已暂停向它下发任务'],
      ['标记完成', 'wcs-completion-quantity-out-of-range', '累计完成数量不能超过计划数量'],
    ])('names the real reason when %s is refused with %s', async (action, code, reason) => {
      // 网关对 409/422 的信封：稳定原因码放在 message 位。
      const refusal = { success: false, message: code, code, errorData: [] }
      wms.dispatchWcs.mockRejectedValue(refusal)
      wms.completeWcs.mockRejectedValue(refusal)
      const { toast } = await import('@nerv-iip/ui')
      const dialog = await openAction(action)

      await submitDialog(dialog)

      const shown = String(vi.mocked(toast.error).mock.calls.at(-1)?.[0])
      expect(shown).toContain(reason)
      expect(shown).not.toContain('状态已被其他操作更新')
      expect(shown).not.toContain('服务暂时不可用')
    })

    it('stops counting a re-dispatched task as failed and drops its old failure reason', async () => {
      wms.wcsRows = [
        {
          wcsTaskId: 'w-1',
          externalTaskId: 'EXT-1',
          warehouseTaskId: 'WT-1',
          adapterType: 'agv',
          status: 'Completed',
          attemptCount: 2,
          failureCode: '设备故障',
          failureMessage: '小车在上架途中报急停',
          failedAtUtc: '2026-09-27T08:09:28Z',
          warehouseTaskVersion: 4,
          plannedQuantity: 1,
          executedQuantity: 1,
        },
      ]
      const wrapper = mount(WcsPage, { global: { stubs: wcsStubs } })
      await flushPromises()

      expect(wrapper.text()).toContain('本页无失败')
      expect(wrapper.text()).toMatch(/本页执行中\s*0/)
      // 不在失败状态的任务不能重新下发：点了设备侧什么也收不到。
      const redispatch = wrapper
        .findAll('[data-menu-item]')
        .find((item) => item.text().includes('重新下发'))!
      expect(redispatch.attributes('disabled')).toBeDefined()
      expect(wrapper.text()).not.toContain('设备故障')
      expect(wrapper.text()).not.toContain('需人工跟进')
    })

    it('reports a full completion receipt as finished', async () => {
      wms.completeWcs.mockResolvedValue(undefined)
      const { toast } = await import('@nerv-iip/ui')
      const dialog = await openAction('标记完成')

      await submitDialog(dialog)

      expect(wms.completeWcs).toHaveBeenCalledWith('EXT-1', {
        completionPayloadJson: '{"actualQuantity":10}',
      })
      expect(toast.success).toHaveBeenCalledWith('设备任务已完成')
    })
  })
})
