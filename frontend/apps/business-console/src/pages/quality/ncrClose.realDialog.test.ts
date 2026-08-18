import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import NcrsPage from './ncrs.vue'

/**
 * **不 stub `NvAlertDialog*`** 的一组用例（#1613 子项 d · quality 域）。
 *
 * 这一处比其它清扫点更糟：确认框原先**完全非受控**——`NvAlertDialogTrigger` 开、
 * `NvAlertDialogAction` 关，关框时机整个落在组件里。而 `NvAlertDialogAction` 渲染成
 * reka `DialogClose`，`@click` 里 `onOpenChange(false)` 无条件执行、不看 `defaultPrevented`：
 * 关单失败时框立刻消失，用户填的关闭原因、返工工单、报废库存移动全都白填一遍
 * （confirm-destroy 规则 3、票面子项 d「注意失败后表单值保留」）。
 *
 * `quality-location.test.ts` 里这页的弹层被桩成 `<div><slot /></div>`，因此上面这件事在那
 * 一份里测不到。门禁（`src/confirmDestroy.contract.test.ts`）只挡写法，行为由本文件在
 * **真弹层**上钉住。
 *
 * NCR 关闭要求状态为 `disposition-in-progress`（`statusActionGate` 的 `ncrGate`），
 * 所以样本单据的状态照此设定，否则 `canCloseNcr` 恒为 false、确认按钮根本点不动。
 */
const ncrRow = {
  id: 'NCR-001',
  code: 'NCR-001',
  status: 'disposition-in-progress',
  sourceDocumentId: 'WO-1001',
  sourceType: 'work-order',
  skuCode: 'SKU-001',
}

const spies = vi.hoisted(() => ({
  closeNcr: vi.fn(),
  submitDisposition: vi.fn().mockResolvedValue({}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

/**
 * `closeNcrPending` 必须是**真的 ref**：挂在普通对象上的布尔值改了不会触发重渲染，
 * 「pending 期间禁点」那条会假绿成"按钮没禁用"。ref 在 mock 工厂里建好后回挂到这里。
 */
const state = vi.hoisted(() => ({}) as { closeNcrPending: { value: boolean } })

vi.mock('@/composables/useBusinessQuality', async () => {
  const { computed, reactive, shallowRef } = await import('vue')
  state.closeNcrPending = shallowRef(false)
  return {
    useQualityNcrs: (initial = {}) => ({
      closeNcr: spies.closeNcr,
      closeNcrError: shallowRef(),
      closeNcrPending: state.closeNcrPending,
      filters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: undefined as string | undefined,
        keyword: undefined as string | undefined,
        skip: 0,
        take: 100,
        ...initial,
      }),
      ncrs: computed(() => [ncrRow]),
      ncrsError: shallowRef(),
      ncrsPending: shallowRef(false),
      ncrsTotal: computed(() => 1),
      refreshNcrs: vi.fn(),
      submitDisposition: spies.submitDisposition,
      submitDispositionError: shallowRef(),
      submitDispositionPending: shallowRef(false),
    }),
  }
})

vi.mock('@/composables/usePagedList', async () => {
  const { shallowRef } = await import('vue')
  return { usePagedList: () => ({ page: shallowRef(1), pageSize: shallowRef(100) }) }
})

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  const route = reactive({ query: {} })
  return {
    ...actual,
    RouterLink: { props: ['to'], template: '<a><slot /></a>' },
    useRoute: () => route,
    useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  }
})

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: spies.toastSuccess, error: spies.toastError },
}))

/**
 * 只桩与本用例无关、且在 jsdom 里会碍事的部分；**AlertDialog 一律保留真件**。
 *
 * 抽屉（`NvSheet`）就地渲染，使抽屉里的关闭表单与触发按钮可填可点；但**只桩内容壳**，
 * 绝不桩 `DialogRoot`——reka `AlertDialogRoot` 内部就是它，桩掉真弹层会报
 * Injection DialogRootContext not found。
 */
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  BusinessDocumentApprovalPanel: { template: '<section />' },
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvSheetFooter: { template: '<div><slot /></div>' },
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: { template: '<button type="button"><slot /></button>' },
  NvSelect: { template: '<select><slot /></select>' },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

/** 真弹层挂 body，用例之间必须卸载。 */
let mounted: ReturnType<typeof mount> | null = null

/** 弹层内容经 Teleport 挂到 body，断言要在 document 上找。 */
function documentButton(label: string) {
  return [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)
}

async function setInput(selector: string, value: string) {
  const input = document.querySelector<HTMLInputElement>(selector)!
  input.value = value
  input.dispatchEvent(new Event('input'))
  await flushPromises()
}

/** 打开单据抽屉、填好关闭表单、点开二次确认框。 */
async function openCloseConfirm() {
  const wrapper = mount(NcrsPage, { global: { stubs }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()

  // 行操作里的「打开处置」把单据带进抽屉。
  await wrapper
    .findAll('button')
    .find((b) => b.text().trim() === '打开处置')!
    .trigger('click')
  await flushPromises()

  await setInput('#ncr-close-reason', '返工后复检合格')
  await setInput('#ncr-scrap', 'MOV-2026-0007')

  // 抽屉底部的「关闭不合格品」是 AlertDialogTrigger，只开框、不发请求。
  documentButton('关闭不合格品')!.click()
  await flushPromises()
  expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  expect(spies.closeNcr).not.toHaveBeenCalled()
  return wrapper
}

beforeEach(() => {
  setActivePinia(createPinia())
  state.closeNcrPending.value = false
  spies.closeNcr.mockReset()
  spies.closeNcr.mockResolvedValue({})
  spies.toastSuccess.mockClear()
  spies.toastError.mockClear()
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

describe('不合格品关闭确认框在真弹层下的关闭时机', () => {
  it('关单失败时确认框保持打开、抽屉里的表单值全部保留，用户可原地重试', async () => {
    spies.closeNcr.mockRejectedValueOnce(new Error('关闭失败'))
    await openCloseConfirm()

    const confirm = documentButton('确认关闭')
    expect(confirm).toBeTruthy()
    confirm!.click()
    await flushPromises()

    expect(spies.closeNcr).toHaveBeenCalledTimes(1)
    expect(spies.toastError).toHaveBeenCalled()
    // 这两条正是 NvAlertDialogAction 会打破的：点击即无条件关框，连抽屉一起看不见。
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(documentButton('确认关闭')).toBeTruthy()
    expect(document.querySelector<HTMLInputElement>('#ncr-close-reason')!.value).toBe(
      '返工后复检合格',
    )
    expect(document.querySelector<HTMLInputElement>('#ncr-scrap')!.value).toBe('MOV-2026-0007')
  })

  it('关单成功才关框，且原因与关联单据如实提交', async () => {
    await openCloseConfirm()

    documentButton('确认关闭')!.click()
    await flushPromises()

    expect(spies.closeNcr).toHaveBeenCalledWith('NCR-001', {
      reason: '返工后复检合格',
      reworkWorkOrderId: 'WO-1001',
      scrapMovementId: 'MOV-2026-0007',
      returnDocumentId: undefined,
    })
    expect(spies.toastSuccess).toHaveBeenCalled()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()
  })

  it('pending 期间确认按钮禁用——这一瞬只有普通 NvButton 才留得住', async () => {
    await openCloseConfirm()
    expect(documentButton('确认关闭')!.hasAttribute('disabled')).toBe(false)

    state.closeNcrPending.value = true
    await flushPromises()

    expect(documentButton('确认关闭')!.hasAttribute('disabled')).toBe(true)
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  })
})
