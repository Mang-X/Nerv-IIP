import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import MasterDataLifecycleDialog from './MasterDataLifecycleDialog.vue'
import { useMasterDataLifecycleConfirm } from '@/composables/masterDataLifecycleConfirm'

/**
 * **不 stub 弹层**，挂真 reka `NvAlertDialog` 的一组用例（#1607）。
 *
 * 为什么必须单独有这一份：其余确认框测试都把 `NvAlertDialog*` 换成了 `<div><slot /></div>`，
 * 于是「点确认后框还开不开」这件事**根本测不到**。而 `NvAlertDialogAction` 包的是 reka
 * `AlertDialogAction` → 渲染成 `DialogClose`，`@click` 里 `onOpenChange(false)` 无条件执行、
 * 不看 `defaultPrevented`——用它做确认按钮，点下去框立刻关，「失败保留原因原地重试」和
 * 「pending 期间禁点」就都只是控制器层的幻觉。
 *
 * 所以确认按钮改用普通 `NvButton`，并由这份用例在**真弹层**上钉住行为。
 */
const stub = vi.hoisted(() => ({ toastSuccess: vi.fn(), toastError: vi.fn() }))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const disable = vi.fn()
const actions = {
  disable,
  enable: vi.fn().mockResolvedValue({}),
  disablePending: shallowRef(false),
  enablePending: shallowRef(false),
}
const row = { resourceType: 'unit-of-measure', code: 'EA', displayName: '个', active: true }

function mountDialog() {
  const controller = useMasterDataLifecycleConfirm()
  const wrapper = mount(MasterDataLifecycleDialog, {
    props: { controller },
    attachTo: document.body,
  })
  return { controller, wrapper }
}

/** 真弹层经 Teleport 挂到 body，断言要在 document 上找。 */
function confirmButton() {
  return [...document.querySelectorAll('button')].find((b) => b.textContent?.includes('确认停用'))
}

beforeEach(() => {
  document.body.innerHTML = ''
  disable.mockReset()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  actions.disablePending.value = false
})

describe('确认框在真弹层下的关闭时机', () => {
  it('提交失败时框保持打开、原因仍在——用户可以原地重试', async () => {
    disable.mockRejectedValueOnce(new Error('停用失败'))
    const { controller } = mountDialog()
    controller.request(row, actions, '计量单位')
    await flushPromises()
    controller.reason.value = '设备报废'
    await flushPromises()

    const button = confirmButton()
    expect(button).toBeTruthy()
    button!.click()
    await flushPromises()

    expect(stub.toastError).toHaveBeenCalled()
    // 这一条正是 NvAlertDialogAction 会打破的：点击即无条件关框。
    expect(controller.open.value).toBe(true)
    expect(controller.reason.value).toBe('设备报废')
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
  })

  it('提交成功才关框', async () => {
    disable.mockResolvedValueOnce({})
    const { controller } = mountDialog()
    controller.request(row, actions, '计量单位')
    await flushPromises()
    controller.reason.value = '产线拆除'
    await flushPromises()

    confirmButton()!.click()
    await flushPromises()

    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(controller.open.value).toBe(false)
  })

  it('原因为空时确认按钮真的禁用（点了也不发请求、也不关框）', async () => {
    const { controller } = mountDialog()
    controller.request(row, actions, '计量单位')
    await flushPromises()

    const button = confirmButton()
    expect(button!.hasAttribute('disabled')).toBe(true)
    button!.click()
    await flushPromises()

    expect(disable).not.toHaveBeenCalled()
    expect(controller.open.value).toBe(true)
  })
})
