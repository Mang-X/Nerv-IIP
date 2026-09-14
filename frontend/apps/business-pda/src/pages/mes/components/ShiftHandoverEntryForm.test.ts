import { NvMobileRadioGroup, NvNumberKeyboard, NvPicker } from '@nerv-iip/ui-mobile'
import { mount } from '@vue/test-utils'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'
import { nextTick, ref } from 'vue'
import ShiftHandoverEntryForm from './ShiftHandoverEntryForm.vue'
import type {
  ShiftHandoverOpenIssue,
  ShiftHandoverUnfinishedWorkOrder,
  ShiftHandoverWipItem,
} from '@/composables/useBusinessShiftHandover'

// jsdom 没有实现 `Element.prototype.scrollTo`，而 NvPicker 打开时会调用它对齐滚轮。
// 不补这个桩的话，Picker 内部的 nextTick 回调会抛出未捕获拒绝，把整个文件判红——
// 那是跑法坏了，不是被测逻辑坏了。补的是 jsdom 的缺口，不是绕过被测组件。
const originalScrollTo = Element.prototype.scrollTo
beforeAll(() => {
  Element.prototype.scrollTo = function scrollToStub() {}
})
afterAll(() => {
  Element.prototype.scrollTo = originalScrollTo
})

/**
 * 全部用真组件驱动（不 stub）：数量走 NvNumberKeyboard、状态走 NvPicker、类别/严重度走
 * NvMobileRadioGroup，跟操作工在屏上走的是同一条路径。
 */
function mountForm() {
  const wipItems = ref<ShiftHandoverWipItem[]>([])
  const unfinishedWorkOrders = ref<ShiftHandoverUnfinishedWorkOrder[]>([])
  const openIssues = ref<ShiftHandoverOpenIssue[]>([])

  const wrapper = mount(ShiftHandoverEntryForm, {
    props: {
      wipItems: wipItems.value,
      'onUpdate:wipItems': async (value: ShiftHandoverWipItem[]) => {
        wipItems.value = value
        await wrapper.setProps({ wipItems: value })
      },
      unfinishedWorkOrders: unfinishedWorkOrders.value,
      'onUpdate:unfinishedWorkOrders': async (value: ShiftHandoverUnfinishedWorkOrder[]) => {
        unfinishedWorkOrders.value = value
        await wrapper.setProps({ unfinishedWorkOrders: value })
      },
      openIssues: openIssues.value,
      'onUpdate:openIssues': async (value: ShiftHandoverOpenIssue[]) => {
        openIssues.value = value
        await wrapper.setProps({ openIssues: value })
      },
    },
  })

  type Wrapper = typeof wrapper

  async function enterQuantity(w: Wrapper, cellTestId: string, value: string) {
    await w.get(`[data-testid="${cellTestId}"]`).trigger('click')
    w.findComponent(NvNumberKeyboard).vm.$emit('update:modelValue', value)
    await nextTick()
  }

  async function pickWorkOrderStatus(w: Wrapper, code: string) {
    await w.get('[data-testid="unfinished-status-cell"]').trigger('click')
    w.findComponent(NvPicker).vm.$emit('update:modelValue', code)
    await nextTick()
  }

  async function setIssueRadios(w: Wrapper, category?: string, severity?: string) {
    const groups = w.findAllComponents(NvMobileRadioGroup)
    if (category !== undefined) groups[0].vm.$emit('update:modelValue', category)
    if (severity !== undefined) groups[1].vm.$emit('update:modelValue', severity)
    await nextTick()
  }

  return {
    wrapper,
    wipItems,
    unfinishedWorkOrders,
    openIssues,
    enterQuantity,
    pickWorkOrderStatus,
    setIssueRadios,
  }
}

describe('ShiftHandoverEntryForm — 在制清点', () => {
  it('refuses to add a row without a work order id and says why', async () => {
    const { wrapper, wipItems } = mountForm()
    await wrapper.get('[data-testid="add-wip"]').trigger('click')

    expect(wipItems.value).toHaveLength(0)
    expect(wrapper.get('[data-testid="wip-section"] [role="alert"]').text()).toContain(
      '请填写工单号',
    )
  })

  it('refuses a negative quantity (域守卫：在制清点数量不能为负数)', async () => {
    const { wrapper, wipItems, enterQuantity } = mountForm()
    await wrapper.get('[data-testid="wip-section"] input').setValue('WO-2026-0001')
    await enterQuantity(wrapper, 'wip-quantity-cell', '-3')
    await wrapper.get('[data-testid="add-wip"]').trigger('click')

    expect(wipItems.value).toHaveLength(0)
    expect(wrapper.get('[data-testid="wip-section"] [role="alert"]').text()).toContain('不能为负数')
  })

  it('adds a row and omits an empty operationTaskId instead of sending a blank string', async () => {
    const { wrapper, wipItems, enterQuantity } = mountForm()
    await wrapper.get('[data-testid="wip-section"] input').setValue('WO-2026-0001')
    await enterQuantity(wrapper, 'wip-quantity-cell', '12')
    await wrapper.get('[data-testid="add-wip"]').trigger('click')

    expect(wipItems.value).toEqual([{ workOrderId: 'WO-2026-0001', quantity: 12 }])
    expect('operationTaskId' in wipItems.value[0]).toBe(false)
  })

  it('accepts quantity 0 (域守卫只禁负数，0 在制是合法清点)', async () => {
    const { wrapper, wipItems, enterQuantity } = mountForm()
    await wrapper.get('[data-testid="wip-section"] input').setValue('WO-2026-0001')
    await enterQuantity(wrapper, 'wip-quantity-cell', '0')
    await wrapper.get('[data-testid="add-wip"]').trigger('click')

    expect(wipItems.value).toEqual([{ workOrderId: 'WO-2026-0001', quantity: 0 }])
  })

  it('removes exactly the row the user tapped', async () => {
    const { wrapper, wipItems, enterQuantity } = mountForm()
    for (const [id, qty] of [
      ['WO-A', '1'],
      ['WO-B', '2'],
    ]) {
      await wrapper.get('[data-testid="wip-section"] input').setValue(id)
      await enterQuantity(wrapper, 'wip-quantity-cell', qty)
      await wrapper.get('[data-testid="add-wip"]').trigger('click')
    }
    expect(wipItems.value.map((i) => i.workOrderId)).toEqual(['WO-A', 'WO-B'])

    await wrapper.get('[data-testid="remove-wip-0"]').trigger('click')
    expect(wipItems.value.map((i) => i.workOrderId)).toEqual(['WO-B'])
  })
})

describe('ShiftHandoverEntryForm — 未完工单', () => {
  async function fill(
    ctx: ReturnType<typeof mountForm>,
    values: { workOrderId: string; planned: string; completed: string; status?: string },
  ) {
    await ctx.wrapper
      .findAll('[data-testid="unfinished-section"] input')[0]
      .setValue(values.workOrderId)
    await ctx.enterQuantity(ctx.wrapper, 'unfinished-planned-cell', values.planned)
    await ctx.enterQuantity(ctx.wrapper, 'unfinished-completed-cell', values.completed)
    if (values.status !== undefined) await ctx.pickWorkOrderStatus(ctx.wrapper, values.status)
  }

  it('rejects completed >= planned — 那不是未完工单（域方法原话）', async () => {
    const ctx = mountForm()
    await fill(ctx, { workOrderId: 'WO-1', planned: '10', completed: '10', status: 'Released' })
    await ctx.wrapper.get('[data-testid="add-unfinished"]').trigger('click')

    expect(ctx.unfinishedWorkOrders.value).toHaveLength(0)
    expect(ctx.wrapper.get('[data-testid="unfinished-section"] [role="alert"]').text()).toContain(
      '完成数量已达到计划数量的工单不是未完工单',
    )
  })

  it('rejects a non-positive planned quantity (域守卫：计划数量必须为正数)', async () => {
    const ctx = mountForm()
    await fill(ctx, { workOrderId: 'WO-1', planned: '0', completed: '0', status: 'Released' })
    await ctx.wrapper.get('[data-testid="add-unfinished"]').trigger('click')

    expect(ctx.unfinishedWorkOrders.value).toHaveLength(0)
    expect(ctx.wrapper.get('[data-testid="unfinished-section"] [role="alert"]').text()).toContain(
      '计划数量必须为正数',
    )
  })

  it('requires a work-order status before adding', async () => {
    const ctx = mountForm()
    await fill(ctx, { workOrderId: 'WO-1', planned: '10', completed: '3' })
    await ctx.wrapper.get('[data-testid="add-unfinished"]').trigger('click')

    expect(ctx.unfinishedWorkOrders.value).toHaveLength(0)
    expect(ctx.wrapper.get('[data-testid="unfinished-section"] [role="alert"]').text()).toContain(
      '请选择工单状态',
    )
  })

  it('adds a valid unfinished work order and renders it with the Chinese status', async () => {
    const ctx = mountForm()
    await fill(ctx, { workOrderId: 'WO-1', planned: '10', completed: '3', status: 'Released' })
    await ctx.wrapper.get('[data-testid="add-unfinished"]').trigger('click')

    expect(ctx.unfinishedWorkOrders.value).toEqual([
      {
        workOrderId: 'WO-1',
        plannedQuantity: 10,
        completedQuantity: 3,
        workOrderStatus: 'Released',
      },
    ])
    const rows = ctx.wrapper.get('[data-testid="unfinished-rows"]').text()
    expect(rows).toContain('已下达')
    expect(rows).not.toContain('Released')
  })
})

describe('ShiftHandoverEntryForm — 遗留问题', () => {
  it('requires category, severity and description in that order', async () => {
    const ctx = mountForm()
    const alert = () => ctx.wrapper.get('[data-testid="issues-section"] [role="alert"]').text()

    await ctx.wrapper.get('[data-testid="add-issue"]').trigger('click')
    expect(alert()).toContain('请选择问题类别')

    await ctx.setIssueRadios(ctx.wrapper, 'Equipment')
    expect(alert()).toContain('请选择严重度')

    await ctx.setIssueRadios(ctx.wrapper, undefined, 'High')
    expect(alert()).toContain('请填写问题描述')

    expect(ctx.openIssues.value).toHaveLength(0)
  })

  it('adds an issue with the enum-cased codes the MES vocabulary parses', async () => {
    const ctx = mountForm()
    const inputs = ctx.wrapper.findAll('[data-testid="issues-section"] input')
    await ctx.setIssueRadios(ctx.wrapper, 'Quality', 'Medium')
    await inputs[0].setValue('3 号线首件尺寸超差待复判')
    await inputs[1].setValue('NCR-2026-0007')
    await ctx.wrapper.get('[data-testid="add-issue"]').trigger('click')

    expect(ctx.openIssues.value).toEqual([
      {
        category: 'Quality',
        severity: 'Medium',
        description: '3 号线首件尺寸超差待复判',
        referenceId: 'NCR-2026-0007',
      },
    ])
    const rows = ctx.wrapper.get('[data-testid="issue-rows"]').text()
    expect(rows).toContain('质量')
    expect(rows).toContain('中')
    expect(rows).not.toContain('Quality')
  })

  it('omits a blank referenceId rather than sending an empty string', async () => {
    const ctx = mountForm()
    const inputs = ctx.wrapper.findAll('[data-testid="issues-section"] input')
    await ctx.setIssueRadios(ctx.wrapper, 'Equipment', 'Low')
    await inputs[0].setValue('2 号机导轨异响，已降速运行')
    await inputs[1].setValue('   ')
    await ctx.wrapper.get('[data-testid="add-issue"]').trigger('click')

    expect(ctx.openIssues.value).toHaveLength(1)
    expect('referenceId' in ctx.openIssues.value[0]).toBe(false)
  })

  it('rejects a description longer than the 1000-char domain bound', async () => {
    const ctx = mountForm()
    const inputs = ctx.wrapper.findAll('[data-testid="issues-section"] input')
    await ctx.setIssueRadios(ctx.wrapper, 'Equipment', 'Low')
    await inputs[0].setValue('x'.repeat(1001))
    await ctx.wrapper.get('[data-testid="add-issue"]').trigger('click')

    expect(ctx.openIssues.value).toHaveLength(0)
    expect(ctx.wrapper.get('[data-testid="issues-section"] [role="alert"]').text()).toContain(
      '不能超过 1000 个字符',
    )
  })
})
