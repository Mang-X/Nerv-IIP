import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'
import StandardOperationsPage from './standard-operations.vue'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: { permissionCodes: ['business.engineering.standard-operations.manage'] },
  }),
}))

const { create, update } = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue(undefined),
  update: vi.fn().mockResolvedValue(undefined),
}))
const rows = shallowRef([
  {
    operationCode: 'OP-1',
    operationName: '精加工',
    defaultWorkCenterCode: 'WC-1',
    standardRunMinutes: 6,
    enabled: true,
  },
])

beforeEach(() => {
  create.mockClear()
  update.mockClear()
})

vi.mock('@/composables/engineering', () => ({
  useStandardOperations: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    standardOperations: rows,
    standardOperationsTotal: shallowRef(1),
    standardOperationsPending: shallowRef(false),
    standardOperationsError: shallowRef(undefined),
    createStandardOperation: create,
    updateStandardOperation: update,
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

function mountPage() {
  const container = { template: '<div><slot /></div>' }
  return mount(StandardOperationsPage, {
    attachTo: document.body,
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
}

async function fillCreate(wrapper: ReturnType<typeof mountPage>) {
  await wrapper
    .findAll('button')
    .find((button) => button.text() === '新建工序')!
    .trigger('click')
  await wrapper.get('#op-code').setValue('OP-CNC')
  await wrapper.get('#op-name').setValue('CNC 加工')
  await wrapper.get('#op-wc').setValue('WC-1')
}

it('提交真实数字输入产生的工时值，准备工时可为零分钟', async () => {
  const wrapper = mountPage()
  await fillCreate(wrapper)
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

it.each(['', '0', '-1', '1.5'])('新建加工工时 %j 拦截写请求并定位错误字段', async (value) => {
  const wrapper = mountPage()
  await fillCreate(wrapper)
  await wrapper.get('#op-run').setValue(value)
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(create).not.toHaveBeenCalled()
  expect(wrapper.get('#op-run').attributes('aria-invalid')).toBe('true')
  expect(document.activeElement).toBe(wrapper.get('#op-run').element)
  expect(wrapper.get('#op-run-error').text()).toContain('大于 0 的整数')
  wrapper.unmount()
})

it('编辑共用工时校验，修正后保存且留空的准备工时按零处理', async () => {
  const wrapper = mountPage()
  await wrapper
    .findAll('button')
    .find((button) => button.text() === '编辑')!
    .trigger('click')
  await wrapper.get('#op-run').setValue('0')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(update).not.toHaveBeenCalled()
  await wrapper.get('#op-run').setValue('7')
  await wrapper.get('#op-setup').setValue('1.5')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(update).not.toHaveBeenCalled()
  expect(wrapper.get('#op-setup-error').text()).toContain('非负整数')
  await wrapper.get('#op-setup').setValue('')
  await wrapper.get('form').trigger('submit')
  await flushPromises()
  expect(update).toHaveBeenCalledWith(
    'OP-1',
    expect.objectContaining({ standardRunMinutes: 7, standardSetupMinutes: 0 }),
  )
  wrapper.unmount()
})
