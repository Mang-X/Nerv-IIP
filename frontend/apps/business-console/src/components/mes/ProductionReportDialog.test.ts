import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import ProductionReportDialog from './ProductionReportDialog.vue'

const spies = vi.hoisted(() => ({
  recordProductionReport: vi.fn(async (_body: Record<string, unknown>) => ({
    data: { reportNo: 'PRPT-2026-0001' },
  })),
  notifySuccess: vi.fn(),
  notifyError: vi.fn(),
}))

vi.mock('@/composables/useBusinessMes', () => ({
  makeIdempotencyKey: (prefix: string) => `${prefix}-test`,
  useMesProductionReporting: () => ({
    recordProductionReport: spies.recordProductionReport,
    recordProductionReportError: ref(undefined),
    recordProductionReportPending: ref(false),
  }),
}))

vi.mock('@/utils/notify', () => ({
  notifySuccess: spies.notifySuccess,
  notifyError: spies.notifyError,
}))

const stubs = {
  DialogRoot: { props: ['open'], template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  NvCheckbox: { template: '<input type="checkbox" />' },
  Field: { template: '<div><slot /></div>' },
  FieldGroup: { template: '<div><slot /></div>' },
  FieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :value="modelValue" v-bind="$attrs" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  Spinner: true,
}

const context = {
  workOrderId: 'WO-2026-0007',
  workOrderNo: 'WO-2026-0007',
  operationTaskId: 'WO-2026-0007-OP-20',
  operationTaskNo: 'WO-2026-0007-OP-20',
  operationSequence: 20,
  workCenterLabel: '精加工一线',
  skuLabel: '减速机壳体',
  plannedQuantity: 200,
}

function mountDialog(ctx: typeof context | null = context) {
  return mount(ProductionReportDialog, {
    props: { open: true, context: ctx },
    global: { stubs },
  })
}

describe('ProductionReportDialog — 带出式录入', () => {
  beforeEach(() => {
    spies.recordProductionReport.mockClear()
    spies.notifySuccess.mockClear()
    spies.notifyError.mockClear()
  })

  it('带出的上下文只读呈现，且不提供工单/工序的输入位', () => {
    const wrapper = mountDialog()

    const carried = wrapper.find('[data-slot="carried-context"]')
    expect(carried.exists()).toBe(true)
    expect(carried.text()).toContain('WO-2026-0007')
    expect(carried.text()).toContain('第 20 道')
    expect(carried.text()).toContain('精加工一线')
    expect(carried.text()).toContain('减速机壳体')
    expect(carried.text()).toContain('200')
    // 只读区是 dl，不是 readonly 输入框
    expect(carried.findAll('input')).toHaveLength(0)
    expect(wrapper.find('#report-work-order').exists()).toBe(false)
    expect(wrapper.find('#report-operation-task').exists()).toBe(false)
  })

  it('录入项只有合格数量、不合格数量与完成状态，且没有说明书文案', () => {
    const wrapper = mountDialog()

    const inputIds = wrapper.findAll('input[id]').map((input) => input.attributes('id'))
    expect(inputIds).toEqual(['report-good', 'report-scrap', 'report-complete'])
    // 报工时间不再作为录入项（提交时取当前时间）
    expect(wrapper.find('#report-time').exists()).toBe(false)
    // 可见区域零说明文案
    expect(wrapper.find('form').text()).not.toMatch(/系统|带出|只能从|必须为非负数|后端/)
  })

  it('提交时把带出的工单/工序与录入数量一起发出，成功后 toast 并关闭', async () => {
    const wrapper = mountDialog()

    await wrapper.find('#report-good').setValue('180')
    await wrapper.find('#report-scrap').setValue('3')
    await wrapper.find('form').trigger('submit')
    await wrapper.vm.$nextTick()

    expect(spies.recordProductionReport).toHaveBeenCalledTimes(1)
    const body = spies.recordProductionReport.mock.calls[0]![0]
    expect(body.workOrderId).toBe('WO-2026-0007')
    expect(body.operationTaskId).toBe('WO-2026-0007-OP-20')
    expect(body.goodQuantity).toBe(180)
    expect(body.scrapQuantity).toBe(3)
    expect(body.completesOperation).toBe(true)
    expect(typeof body.reportedAtUtc).toBe('string')
    expect(spies.notifySuccess).toHaveBeenCalledOnce()
    expect(wrapper.emitted('update:open')?.at(-1)).toEqual([false])
    expect(wrapper.emitted('reported')).toHaveLength(1)
  })

  it('合计数量为 0 时点提交只标红、不发请求', async () => {
    const wrapper = mountDialog()

    await wrapper.find('#report-good').setValue('0')
    await wrapper.find('#report-scrap').setValue('0')
    await wrapper.find('form').trigger('submit')

    expect(spies.recordProductionReport).not.toHaveBeenCalled()
    expect(wrapper.find('#report-good').attributes('data-invalid')).toBeDefined()
    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('没有带出上下文时不渲染录入表单（无法凭空报工）', () => {
    const wrapper = mountDialog(null)

    expect(wrapper.find('form').exists()).toBe(false)
  })
})
