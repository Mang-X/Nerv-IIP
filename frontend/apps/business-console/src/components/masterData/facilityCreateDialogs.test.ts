import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import ProductionLineCreateDialog from './ProductionLineCreateDialog.vue'
import WorkCenterCreateDialog from './WorkCenterCreateDialog.vue'

const create = vi.hoisted(() => vi.fn().mockResolvedValue({ data: { code: 'NEW-1' } }))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useCreateMasterDataResource: () => ({
    create,
    error: shallowRef(undefined),
    pending: shallowRef(false),
  }),
  useBusinessMasterDataResources: (resourceType: string) => ({
    filters: reactive({ take: 100 }),
    resources: computed(() =>
      resourceType === 'work-calendar' ? [{ code: 'CAL-A', displayName: '标准日历' }] : [],
    ),
  }),
}))

// 上级选择器换成原生输入框，直接改值（选择器取数与收窄由 DirectoryPicker 自己的用例覆盖）。
const stubs = {
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  DirectoryPicker: {
    props: ['modelValue', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}

// 用户在弹窗里自选上级：先选了工厂 A 下的下级，再把工厂改成 B，原先的下级不属于 B，必须清掉，
// 否则会提交一个工厂与车间 / 产线对不上的新建项。
describe('新增弹窗里换工厂时清空下级', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    create.mockClear()
  })

  it('产线：换工厂后清掉已选车间，提交不带旧车间', async () => {
    const wrapper = mount(ProductionLineCreateDialog, {
      props: { open: true, context: {} },
      global: { stubs },
    })
    await wrapper.find('#line-name').setValue('涂装线')
    await wrapper.find('#line-site').setValue('PLANT-A')
    await wrapper.find('#line-workshop').setValue('WS-A1')
    await wrapper.find('#line-site').setValue('PLANT-B')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(create).toHaveBeenCalledTimes(1)
    expect(create.mock.calls[0]![0]).toMatchObject({ siteCode: 'PLANT-B' })
    expect(create.mock.calls[0]![0]).not.toHaveProperty('workshopCode')
  })

  it('工作中心：换工厂后清掉已选产线，产线必填所以不提交', async () => {
    const wrapper = mount(WorkCenterCreateDialog, {
      props: { open: true, context: {} },
      global: { stubs },
    })
    await wrapper.find('#wc-name').setValue('喷涂中心')
    await wrapper.find('#wc-plant').setValue('PLANT-A')
    await wrapper.find('#wc-line').setValue('LINE-A1')
    await wrapper.find('#wc-plant').setValue('PLANT-B')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(create).not.toHaveBeenCalled()
    expect((wrapper.find('#wc-line').element as HTMLInputElement).value).toBe('')
  })
})
