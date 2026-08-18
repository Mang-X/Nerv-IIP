import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref, shallowRef } from 'vue'

import ProductionVersionsPage from './production-versions.vue'

/**
 * **不 stub `NvAlertDialog*`** 的一组用例（#1613 子项 c · engineering 域）。
 *
 * `production-versions.test.ts` 不只桩了弹层，还桩了 `DialogRoot`——reka `AlertDialogRoot`
 * 内部就是它，桩掉真弹层直接拿不到上下文。于是「归档失败后框还开不开」在那份用例里
 * 无从断言，而原确认按钮 `NvAlertDialogAction` 渲染成 `DialogClose`：点击即无条件关框，
 * 归档失败时用户填的原因连框一起消失（confirm-destroy 规则 3）。
 *
 * `standard-operations.vue` 的停用确认与本页同构（同一份 `archivePending` + `confirmArchive`
 * 形态），子项 c 的另一处由这份用例代表；写法层面两处都由
 * `src/confirmDestroy.contract.test.ts` 兜住。
 */
const stub = vi.hoisted(() => ({
  archiveProductionVersion: vi.fn().mockResolvedValue(undefined),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const archivePending = shallowRef(false)
const pvRow = {
  productionVersionId: 'pv-1',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  skuCode: 'SKU-1',
  mbomVersionId: 'MBOM-1',
  routingVersionId: 'RT-1',
  validFrom: '2026-01-01',
  validTo: '2026-12-31',
  lotSizeMin: 10,
  lotSizeMax: 500,
  priority: 5,
  isDefault: true,
  status: 'active',
}

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringProductionVersions: () => ({
    archiveProductionVersion: stub.archiveProductionVersion,
    archivePending,
    archiveError: shallowRef(undefined),
    createProductionVersion: vi.fn().mockResolvedValue({ data: {} }),
    createPending: shallowRef(false),
    createError: shallowRef(undefined),
    filters: reactive({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      skip: 0,
      take: 10,
    }),
    productionVersions: computed(() => [pvRow]),
    productionVersionsError: shallowRef(undefined),
    productionVersionsPending: shallowRef(false),
    productionVersionsTotal: computed(() => 1),
    refresh: vi.fn(),
    updateProductionVersion: vi.fn().mockResolvedValue({ data: {} }),
    updatePending: shallowRef(false),
    updateError: shallowRef(undefined),
  }),
  usePublishedMboms: () => ({
    filters: reactive({}),
    mboms: computed(() => [
      { bomCode: 'MBOM-1', revision: 'A', skuCode: 'SKU-1', status: 'Published' },
    ]),
    mbomsError: shallowRef(undefined),
    mbomsPending: shallowRef(false),
    refreshMboms: vi.fn(),
  }),
  usePublishedRoutings: () => ({
    filters: reactive({}),
    routings: computed(() => [
      { routingCode: 'RT-1', revision: 'A', skuCode: 'SKU-1', status: 'Published' },
    ]),
    routingsError: shallowRef(undefined),
    routingsPending: shallowRef(false),
    refreshRoutings: vi.fn(),
  }),
  useProductionVersionResolve: () => ({
    resolve: vi.fn(),
    clear: vi.fn(),
    resolved: shallowRef(undefined),
    resolvePending: shallowRef(false),
    resolvedOnce: ref(false),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    skus: computed(() => [{ code: 'SKU-1', displayName: '智能网关主机' }]),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

/**
 * 只桩与本用例无关、且在 jsdom 里会碍事的部分；**AlertDialog 与 `DialogRoot` 一律保留真件**。
 * 桩 `DialogRoot` 会让真 `AlertDialogRoot` 失去上下文（`DialogOverlay` 报
 * Injection DialogRootContext not found）——那正是页面测试里测不到关框时机的原因之一。
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
  NvDatePicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input type="date" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value || null)" />',
  },
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
  const wrapper = mount(ProductionVersionsPage, { global: { stubs }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()

  await wrapper
    .findAll('button')
    .find((b) => b.text().trim() === '归档')!
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
  return document.querySelector<HTMLInputElement>('#archive-reason')
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
  stub.archiveProductionVersion.mockReset()
  stub.archiveProductionVersion.mockResolvedValue(undefined)
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
})

describe('生产版本归档确认框在真弹层下的关闭时机', () => {
  it('归档失败时框保持打开、已填原因仍在，用户可原地重试', async () => {
    stub.archiveProductionVersion.mockRejectedValueOnce(new Error('归档失败'))
    await openArchiveConfirm()
    await fillReason('工艺变更')

    const confirm = documentButton('确认归档')
    expect(confirm).toBeTruthy()
    confirm!.click()
    await flushPromises()

    expect(stub.archiveProductionVersion).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalled()
    // 这一条正是 NvAlertDialogAction 会打破的：点击即无条件关框。
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(reasonInput()!.value).toBe('工艺变更')
  })

  it('归档成功才关框', async () => {
    await openArchiveConfirm()
    await fillReason('工艺变更')

    documentButton('确认归档')!.click()
    await flushPromises()

    expect(stub.archiveProductionVersion).toHaveBeenCalledWith('pv-1', '工艺变更')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()
  })

  it('pending 期间确认按钮禁用——这一瞬只有普通 NvButton 才留得住', async () => {
    await openArchiveConfirm()
    expect(documentButton('确认归档')!.hasAttribute('disabled')).toBe(false)

    archivePending.value = true
    await flushPromises()

    expect(documentButton('确认归档')!.hasAttribute('disabled')).toBe(true)
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  })
})
