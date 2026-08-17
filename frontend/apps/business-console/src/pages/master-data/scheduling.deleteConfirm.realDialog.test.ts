import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SchedulingPage from './scheduling.vue'

/**
 * **不 stub `NvAlertDialog*`** 的一组用例（#1608）。
 *
 * `scheduling.test.ts` 把弹层桩成 `<div><slot /></div>`，于是「点确认后框还开不开」根本测不到。
 * 而 `NvAlertDialogAction` 包的是 reka `AlertDialogAction` → 渲染成 `DialogClose`，`@click` 里
 * `onOpenChange(false)` 无条件执行、不看 `defaultPrevented`——用它做确认按钮，写回失败时框早就没了。
 * 所以删除 / 替换的确认按钮改成普通 `NvButton`，由这份用例在真弹层上钉住关框时机。
 * 样例来源：`components/masterData/MasterDataLifecycleDialog.realDialog.test.ts`。
 */
const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({ data: { code: 'SHIFT-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  shiftUpdate: vi.fn().mockResolvedValue({}),
  shiftFetchDetail: vi.fn().mockResolvedValue({ name: '白班' }),
  calUpdate: vi.fn().mockResolvedValue({}),
  calFetchDetail: vi.fn().mockResolvedValue({
    name: '标准日历',
    workingTimes: [{ dayOfWeek: 'monday', startsAt: '08:00:00', endsAt: '17:00:00' }],
    holidays: [{ date: '2026-06-19', name: '端午节' }],
    exceptions: [{ date: '2026-06-20', isWorkingDay: true, reason: '调休' }],
  }),
}))

function stubResource(resourceType: string) {
  const labelByType: Record<string, { code: string; name: string }> = {
    shift: { code: 'SHIFT-A', name: '白班' },
    'work-calendar': { code: 'CAL-A', name: '标准日历' },
  }
  const entry = labelByType[resourceType]
  const rows = entry
    ? [
        {
          resourceType,
          code: entry.code,
          displayName: entry.name,
          active: true,
          snapshotVersion: '1',
        },
      ]
    : []
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    items: computed(() => rows),
    total: computed(() => rows.length),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    create: stub.create,
    createError: shallowRef(undefined),
    createPending: shallowRef(false),
  }
}

function stubActions(resourceType: string) {
  const isCal = resourceType === 'work-calendar'
  return {
    update: isCal ? actionStub.calUpdate : actionStub.shiftUpdate,
    disable: vi.fn(),
    enable: vi.fn(),
    fetchDetail: isCal ? actionStub.calFetchDetail : actionStub.shiftFetchDetail,
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useMasterDataResourceActions: (resourceType: string) => stubActions(resourceType),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

// 只桩与本用例无关、且在 jsdom 里会碍事的部分；**AlertDialog 一律保留真件**。
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  MasterDataRowActions: { template: '<span />' },
  NvStatusBadge: { template: '<span />' },
  // 抽屉就地渲染，使抽屉里的删除触发按钮可点（弹层本身不受影响）。
  // 注意：**不能桩 `DialogRoot`** —— reka `AlertDialogRoot` 内部就是它，桩掉会让真弹层拿不到
  // 上下文（DialogOverlay 报 Injection DialogRootContext not found）。只桩 Sheet 的内容壳。
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<div><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: { template: '<button type="button"><slot /></button>' },
  NvDatePicker: { template: '<input type="date" />' },
  NvSelect: { template: '<select><slot /></select>' },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

/** 真弹层挂 body，用例之间必须卸载。 */
let mounted: ReturnType<typeof mount> | null = null

async function mountSchedulingSheet() {
  const wrapper = mount(SchedulingPage, { global: { stubs }, attachTo: document.body })
  mounted = wrapper
  await flushPromises()

  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes('工作日历'))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()

  await wrapper
    .findAll('button')
    .find((b) => b.text().trim() === '标准日历')!
    .trigger('click')
  await flushPromises()

  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('管理节假日'))!
    .trigger('click')
  await flushPromises()
  return wrapper
}

/** 弹层内容经 Teleport 挂到 body，断言要在 document 上找。 */
function documentButton(label: string) {
  return [...document.querySelectorAll('button')].find((b) => b.textContent?.trim() === label)
}

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

beforeEach(() => {
  for (const fn of [
    stub.toastSuccess,
    stub.toastError,
    actionStub.calUpdate,
    actionStub.calFetchDetail,
  ]) {
    fn.mockClear()
  }
})

describe('节假日删除确认框在真弹层下的关闭时机', () => {
  it('写回失败时框保持打开，用户可原地重试', async () => {
    actionStub.calUpdate.mockRejectedValueOnce(new Error('保存失败'))
    const wrapper = await mountSchedulingSheet()

    await wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === '删除节假日')!
      .trigger('click')
    await flushPromises()
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()

    const confirm = documentButton('确认删除')
    expect(confirm).toBeTruthy()
    confirm!.click()
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalled()
    // 这一条正是 NvAlertDialogAction 会打破的：点击即无条件关框。
    expect(document.querySelector('[role="alertdialog"]')).not.toBeNull()
    expect(documentButton('确认删除')).toBeTruthy()
  })

  it('写回成功才关框', async () => {
    const wrapper = await mountSchedulingSheet()

    await wrapper
      .findAll('button')
      .find((b) => b.attributes('aria-label') === '删除节假日')!
      .trigger('click')
    await flushPromises()

    documentButton('确认删除')!.click()
    await flushPromises()

    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(document.querySelector('[role="alertdialog"]')).toBeNull()
  })
})
