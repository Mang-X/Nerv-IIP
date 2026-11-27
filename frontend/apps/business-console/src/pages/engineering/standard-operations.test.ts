import { flushPromises, mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'
import StandardOperationsPage from './standard-operations.vue'

const { create } = vi.hoisted(() => ({ create: vi.fn().mockResolvedValue(undefined) }))

vi.mock('@/composables/engineering', () => ({
  useStandardOperations: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    standardOperations: shallowRef([]),
    standardOperationsTotal: shallowRef(0),
    standardOperationsPending: shallowRef(false),
    standardOperationsError: shallowRef(undefined),
    createStandardOperation: create,
    createPending: shallowRef(false),
    updatePending: shallowRef(false),
    archivePending: shallowRef(false),
  }),
}))

vi.mock('@/composables/master-data', () => ({
  useBusinessMasterDataResources: () => ({
    resources: shallowRef([{ code: 'WC-1', displayName: '加工中心' }]),
    resourcesPending: shallowRef(false),
  }),
}))

vi.mock('@/utils/notify', () => ({
  friendlyErrorMessage: () => '',
  notifySuccess: vi.fn(),
  notifyOperationFailure: vi.fn(),
}))

it('提交真实数字输入产生的工时值，包含零分钟', async () => {
  const container = { template: '<div><slot /></div>' }
  const wrapper = mount(StandardOperationsPage, {
    global: {
      stubs: {
        BusinessLayout: container,
        NvDialog: container,
        NvDialogTrigger: container,
        NvDialogContent: container,
        NvDialogHeader: container,
        NvDialogTitle: container,
        NvDialogDescription: container,
        NvDialogFooter: container,
        DirectoryPicker: {
          props: ['modelValue'],
          emits: ['update:modelValue'],
          template:
            '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
        },
      },
    },
  })
  await wrapper
    .findAll('button')
    .find((button) => button.text() === '新建工序')!
    .trigger('click')
  await wrapper.get('#op-code').setValue('OP-CNC')
  await wrapper.get('#op-name').setValue('CNC 加工')
  await wrapper.get('#op-wc').setValue('WC-1')
  await wrapper.get('#op-setup').setValue('0')
  await wrapper.get('#op-run').setValue('5')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(create).toHaveBeenCalledWith(
    expect.objectContaining({
      operationCode: 'OP-CNC',
      standardSetupMinutes: 0,
      standardRunMinutes: 5,
    }),
  )
  wrapper.unmount()
})
