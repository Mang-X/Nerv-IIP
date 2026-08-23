import { shallowRef } from 'vue'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import FirstArticlePlanDialog from './FirstArticlePlanDialog.vue'

const state = vi.hoisted(() => ({
  createAndActivate: vi.fn(),
  notifyFailure: vi.fn(),
  notifySuccess: vi.fn(),
}))

vi.mock('@/composables/useBusinessQuality', () => ({
  useQualityFirstArticlePlanActions: () => ({
    createAndActivateFirstArticlePlan: state.createAndActivate,
    createFirstArticlePlanPending: shallowRef(false),
  }),
}))
vi.mock('@/utils/notify', () => ({
  notifyOperationFailure: state.notifyFailure,
  notifySuccess: state.notifySuccess,
}))

const stubs = {
  NvDialog: { props: ['open'], template: '<section v-if="open"><slot /></section>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvField: { template: '<div><slot /></div>' },
  NvFieldDescription: { template: '<p><slot /></p>' },
  NvFieldGroup: { template: '<div><slot /></div>' },
  NvFieldLabel: { template: '<label><slot /></label>' },
  NvInput: {
    props: ['id', 'modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvEntityPicker: {
    props: ['id', 'modelValue', 'options'],
    emits: ['update:modelValue'],
    template:
      '<select :id="id" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value=""></option><option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option></select>',
  },
  NvButton: {
    props: ['disabled', 'type'],
    template: '<button :type="type || \'button\'" :disabled="disabled"><slot /></button>',
  },
  NvSelect: { template: '<div><slot /></div>' },
  NvSelectContent: { template: '<div><slot /></div>' },
  NvSelectItem: { template: '<span><slot /></span>' },
  NvSelectTrigger: { template: '<button type="button"><slot /></button>' },
  NvSelectValue: { template: '<span />' },
  Spinner: { template: '<span />' },
  SelectRoot: { template: '<div><slot /></div>' },
  SelectContent: { template: '<div><slot /></div>' },
  SelectItem: { template: '<span><slot /></span>' },
  SelectTrigger: { template: '<button type="button"><slot /></button>' },
  SelectValue: { template: '<span />' },
}

function mountDialog() {
  return mount(FirstArticlePlanDialog, {
    props: {
      open: true,
      organizationId: 'org-1',
      environmentId: 'env-1',
      skuOptions: [{ value: 'SKU-FA-001', label: '精密泵体' }],
      skusPending: false,
      workCenterOptions: [{ value: 'WC-ASSEMBLY-01', label: '总装一线' }],
      workCentersPending: false,
    },
    global: { stubs },
  })
}

async function fillRequiredFields(wrapper: ReturnType<typeof mountDialog>) {
  await wrapper.get('#first-article-plan-code').setValue('FA-PLAN-001')
  await wrapper.get('#first-article-sku').setValue('SKU-FA-001')
  await wrapper.get('#first-article-work-center').setValue('WC-ASSEMBLY-01')
  await wrapper.get('#first-article-item-code-0').setValue('appearance')
  await wrapper.get('#first-article-item-name-0').setValue('外观完整性')
}

describe('首件检验方案配置', () => {
  beforeEach(() => {
    state.createAndActivate.mockReset()
    state.notifyFailure.mockReset()
    state.notifySuccess.mockReset()
  })

  it('点提交后显示必填缺口且不发送请求', async () => {
    const wrapper = mountDialog()
    await wrapper.get('form').trigger('submit')

    expect(wrapper.text()).toContain('请选择适用物料。')
    expect(wrapper.text()).toContain('请选择工序工作中心。')
    expect(state.createAndActivate).not.toHaveBeenCalled()
  })

  it('固定首件分类提交，启用失败时保留对话框并明确提示可恢复', async () => {
    const activationError = new Error('activation failed')
    state.createAndActivate.mockResolvedValue({
      inspectionPlanId: 'plan-1',
      activated: false,
      activationError,
    })
    const wrapper = mountDialog()
    await fillRequiredFields(wrapper)
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.createAndActivate).toHaveBeenCalledWith(
      expect.objectContaining({
        organizationId: 'org-1',
        environmentId: 'env-1',
        category: 'first-article',
        skuCode: 'SKU-FA-001',
        workCenterId: 'WC-ASSEMBLY-01',
        characteristics: [
          expect.objectContaining({
            characteristicCode: 'appearance',
            name: '外观完整性',
            required: true,
          }),
        ],
      }),
    )
    expect(state.notifyFailure).toHaveBeenCalledWith(
      '首件方案启用失败',
      activationError,
      '方案已创建但未启用，请在方案列表中重新启用。',
    )
    expect(wrapper.emitted('update:open')).toBeUndefined()
  })
})
