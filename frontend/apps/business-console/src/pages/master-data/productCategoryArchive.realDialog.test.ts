import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ProductCategoriesPage from './product-categories.vue'

/**
 * **不 stub `NvAlertDialog*`** 的一组用例（#1613 子项 b · master-data 域）。
 *
 * `product-categories.test.ts` 把弹层桩成 `<div><slot /></div>`，于是「点确认后框还开不开」
 * 根本测不到——它连确认按钮都是自己那个桩渲染的。而原先的确认按钮是 `NvAlertDialogAction`，
 * 包的是 reka `AlertDialogAction` → 渲染成 `DialogClose`，`@click` 里 `onOpenChange(false)`
 * 无条件执行、不看 `defaultPrevented`：停用失败时框早已消失，用户填过的原因一起没了。
 *
 * 门禁（`src/confirmDestroy.contract.test.ts`）只挡**写法**，关框时机得由这份用例在**真弹层**
 * 上钉住。样例来源：`components/masterData/MasterDataLifecycleDialog.realDialog.test.ts`。
 */
const stub = vi.hoisted(() => ({
  archiveCategory: vi.fn().mockResolvedValue({}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const archivePending = shallowRef(false)
const categories = [
  {
    categoryCode: 'PCAT-SHOCK-FR',
    categoryName: '前减振器',
    parentCode: 'PCAT-SHOCK',
    enabled: true,
  },
]

vi.mock('@/composables/usePromotedCatalogs', () => ({
  useProductCategories: () => ({
    archiveCategory: stub.archiveCategory,
    archivePending,
    categories: computed(() => categories),
    categoriesError: shallowRef(undefined),
    categoriesPending: shallowRef(false),
    categoriesTotal: computed(() => categories.length),
    createCategory: vi.fn().mockResolvedValue({}),
    createPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    refresh: vi.fn(),
    updateCategory: vi.fn().mockResolvedValue({}),
    updatePending: shallowRef(false),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

/**
 * 只桩与本用例无关、且在 jsdom 里会碍事的部分；**AlertDialog 一律保留真件**。
 *
 * 注意不能顺手把 `NvDialog` 系（新建/编辑弹窗）也桩成 `DialogRoot` 替身——reka
 * `AlertDialogRoot` 内部就是 `DialogRoot`，桩掉会让真弹层拿不到上下文
 * （`DialogOverlay` 报 Injection DialogRootContext not found）。这里只桩内容壳。
 */
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvSelect: { template: '<select><slot /></select>' },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

/** 真弹层挂 body，用例之间必须卸载。 */
let mounted: ReturnType<typeof mount> | null = null

async function openArchiveConfirm() {
  const wrapper = mount(ProductCategoriesPage, { global: { stubs }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()

  await wrapper
    .findAll('button')
    .find((b) => b.text().trim() === '停用')!
    .trigger('click')
  await flushPromises()
  expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  return wrapper
}

/** 弹层内容经 Teleport 挂到 body，断言要在 document 上找。 */
function documentButton(label: string) {
  return [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)
}

function reasonInput() {
  return document.querySelector<HTMLInputElement>('#category-archive-reason')
}

async function fillReason(value: string) {
  const input = reasonInput()!
  input.value = value
  input.dispatchEvent(new Event('input'))
  await flushPromises()
}

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

beforeEach(() => {
  archivePending.value = false
  stub.archiveCategory.mockReset()
  stub.archiveCategory.mockResolvedValue({})
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
})

describe('产品分类停用确认框在真弹层下的关闭时机', () => {
  it('停用失败时框保持打开、已填原因仍在，用户可原地重试', async () => {
    stub.archiveCategory.mockRejectedValueOnce(new Error('停用失败'))
    await openArchiveConfirm()
    await fillReason('与上级分类合并')

    const confirm = documentButton('确认停用')
    expect(confirm).toBeTruthy()
    confirm!.click()
    await flushPromises()

    expect(stub.archiveCategory).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalled()
    // 这一条正是 NvAlertDialogAction 会打破的：点击即无条件关框。
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(reasonInput()!.value).toBe('与上级分类合并')
  })

  it('停用成功才关框', async () => {
    await openArchiveConfirm()
    await fillReason('图纸取消该分类')

    documentButton('确认停用')!.click()
    await flushPromises()

    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()
  })

  it('原因为空时确认按钮在真 UI 上真的禁用：点了不发请求也不关框', async () => {
    await openArchiveConfirm()

    const confirm = documentButton('确认停用')!
    expect(confirm.hasAttribute('disabled')).toBe(true)
    confirm.click()
    await flushPromises()

    expect(stub.archiveCategory).not.toHaveBeenCalled()
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  })

  it('pending 期间确认按钮禁用——这一瞬只有普通 NvButton 才留得住', async () => {
    await openArchiveConfirm()
    await fillReason('工艺淘汰')
    expect(documentButton('确认停用')!.hasAttribute('disabled')).toBe(false)

    archivePending.value = true
    await flushPromises()

    expect(documentButton('确认停用')!.hasAttribute('disabled')).toBe(true)
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  })
})
