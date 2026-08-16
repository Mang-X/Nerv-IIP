import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ProductCategoriesPage from './product-categories.vue'

const stub = vi.hoisted(() => ({
  archiveCategory: vi.fn().mockResolvedValue({}),
  createCategory: vi.fn().mockResolvedValue({}),
  updateCategory: vi.fn().mockResolvedValue({}),
  refresh: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  categories: [
    {
      categoryCode: 'PCAT-SHOCK-FR',
      categoryName: '前减振器',
      parentCode: 'PCAT-SHOCK',
      enabled: true,
    },
  ],
}))

vi.mock('@/composables/usePromotedCatalogs', () => ({
  useProductCategories: () => ({
    archiveCategory: stub.archiveCategory,
    archivePending: shallowRef(false),
    categories: computed(() => stub.categories),
    categoriesError: shallowRef(undefined),
    categoriesPending: shallowRef(false),
    categoriesTotal: computed(() => stub.categories.length),
    createCategory: stub.createCategory,
    createPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    refresh: stub.refresh,
    updateCategory: stub.updateCategory,
    updatePending: shallowRef(false),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
// 确认弹层含 reka portal/Teleport，jsdom 卸载会崩——就地渲染，便于填原因、点确认。
const dialogStubs = {
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  NvAlertDialogAction: {
    props: ['disabled'],
    emits: ['click'],
    template:
      '<button type="button" :disabled="disabled" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  // NvSelectValue 是 reka SelectValue 的裸导出，按 Nv 名 stub 不掉；渲染它会去 inject
  // SelectRoot（已被 NvSelect stub 抹平）而抛错。trigger 不吐 slot 即可自洽。
  NvSelectTrigger: { template: '<span />' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.archiveCategory.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
})

describe('产品分类停用原因', () => {
  async function openArchiveDialog() {
    const wrapper = mount(ProductCategoriesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs } },
    })
    await flushPromises()
    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()
    return wrapper
  }

  it('确认框提供原因输入，空原因时确认按钮禁用且不发请求', async () => {
    const wrapper = await openArchiveDialog()

    const reason = wrapper.find('#category-archive-reason')
    expect(reason.exists()).toBe(true)

    const confirm = () => findButton(wrapper, '确认停用')!
    expect(confirm().attributes('disabled')).toBeDefined()
    await confirm().trigger('click')
    await flushPromises()
    expect(stub.archiveCategory).not.toHaveBeenCalled()

    // 纯空白不算填写。
    await reason.setValue('   ')
    await flushPromises()
    expect(confirm().attributes('disabled')).toBeDefined()
  })

  it('把用户填写的原因原样提交（去首尾空白），不再写死「不再使用」', async () => {
    const wrapper = await openArchiveDialog()
    await wrapper.find('#category-archive-reason').setValue('  与「减振器总成」合并，停止单列  ')
    await flushPromises()
    await findButton(wrapper, '确认停用')!.trigger('click')
    await flushPromises()

    expect(stub.archiveCategory).toHaveBeenCalledWith(
      'PCAT-SHOCK-FR',
      '与「减振器总成」合并，停止单列',
    )
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('再次打开确认框时原因已清空，不残留上一条', async () => {
    const wrapper = await openArchiveDialog()
    await wrapper.find('#category-archive-reason').setValue('与「减振器总成」合并，停止单列')
    await flushPromises()
    await findButton(wrapper, '确认停用')!.trigger('click')
    await flushPromises()

    await findButton(wrapper, '停用')!.trigger('click')
    await flushPromises()

    expect((wrapper.find('#category-archive-reason').element as HTMLInputElement).value).toBe('')
    expect(findButton(wrapper, '确认停用')!.attributes('disabled')).toBeDefined()
  })

  it('提交失败时保留已填原因，便于重试', async () => {
    stub.archiveCategory.mockRejectedValueOnce(new Error('停用失败'))
    const wrapper = await openArchiveDialog()
    await wrapper.find('#category-archive-reason').setValue('图纸取消该分类')
    await flushPromises()
    await findButton(wrapper, '确认停用')!.trigger('click')
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalled()
    expect((wrapper.find('#category-archive-reason').element as HTMLInputElement).value).toBe(
      '图纸取消该分类',
    )
  })
})
