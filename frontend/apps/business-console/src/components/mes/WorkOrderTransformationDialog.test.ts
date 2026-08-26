import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import WorkOrderTransformationDialog from './WorkOrderTransformationDialog.vue'

const stubs = {
  NvDialog: { props: ['open'], template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvButton: { template: '<button v-bind="$attrs"><slot /></button>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input v-bind="$attrs" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvStatusBadge: { template: '<span><slot />{{ label }}</span>', props: ['label'] },
  Spinner: true,
}

function mountDialog(props: Record<string, unknown>) {
  return mount(WorkOrderTransformationDialog, {
    props: {
      open: true,
      mode: 'split',
      idempotencyKey: 'split-test-1',
      ...props,
    },
    global: { stubs },
  })
}

describe('WorkOrderTransformationDialog', () => {
  it('数量守恒后提交拆分目标与原因', async () => {
    const wrapper = mountDialog({
      source: { workOrderId: 'WO-SOURCE', label: 'WO-SOURCE', quantity: 10, skuId: 'SKU-1' },
    })
    await wrapper.find('#split-target-id-0').setValue('WO-CHILD-1')
    await wrapper.find('#split-target-quantity-0').setValue('4')
    await wrapper.find('#split-target-id-1').setValue('WO-CHILD-2')
    await wrapper.find('#split-target-quantity-1').setValue('6')
    await wrapper.find('#split-reason').setValue('按客户批次拆分')
    await wrapper.find('[data-testid="submit-work-order-transformation"]').trigger('click')
    expect(wrapper.emitted('submit')?.[0]?.[0]).toEqual({
      targets: [
        { workOrderId: 'WO-CHILD-1', quantity: 4 },
        { workOrderId: 'WO-CHILD-2', quantity: 6 },
      ],
      reason: '按客户批次拆分',
      idempotencyKey: 'split-test-1',
    })
  })

  it('提交前显示数量校验，不发送不守恒请求', async () => {
    const wrapper = mountDialog({
      source: { workOrderId: 'WO-SOURCE', quantity: 10, skuId: 'SKU-1' },
    })
    await wrapper.find('#split-target-id-0').setValue('WO-CHILD-1')
    await wrapper.find('#split-target-quantity-0').setValue('4')
    await wrapper.find('#split-target-id-1').setValue('WO-CHILD-2')
    await wrapper.find('#split-target-quantity-1').setValue('5')
    await wrapper.find('#split-reason').setValue('调整')
    await wrapper.find('[data-testid="submit-work-order-transformation"]').trigger('click')

    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(wrapper.find('[data-testid="transformation-validation-errors"]').text()).toContain(
      '拆分后数量必须等于源工单数量 10。',
    )
  })

  it('合并同上下文工单并显示 409 冲突态', async () => {
    const wrapper = mountDialog({
      mode: 'merge',
      idempotencyKey: 'merge-test-1',
      sources: [
        {
          workOrderId: 'WO-1',
          skuId: 'SKU-1',
          productionVersionId: 'PV-1',
          quantity: 2,
          status: 'created',
        },
        {
          workOrderId: 'WO-2',
          skuId: 'SKU-1',
          productionVersionId: 'PV-1',
          quantity: 3,
          status: 'released',
        },
      ],
      state: 'conflict',
    })
    await wrapper.find('#merge-target-work-order').setValue('WO-NEW')
    await wrapper.find('#merge-reason').setValue('同 SKU 小单合并')
    await wrapper.find('[data-testid="submit-work-order-transformation"]').trigger('click')

    expect(wrapper.emitted('submit')?.[0]?.[0]).toEqual({
      sourceWorkOrderIds: ['WO-1', 'WO-2'],
      targetWorkOrderId: 'WO-NEW',
      reason: '同 SKU 小单合并',
      idempotencyKey: 'merge-test-1',
    })
    expect(wrapper.find('[data-testid="transformation-status"]').text()).toContain('409')
  })
})
