import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SchedulingPage from './scheduling.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({ data: { code: 'SHIFT-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

// 各资源类型独立的 actions stub，避免 shift 与 work-calendar 互相串调用记录。
const actionStub = vi.hoisted(() => ({
  shiftUpdate: vi.fn().mockResolvedValue({}),
  shiftFetchDetail: vi.fn().mockResolvedValue({ name: '白班', startsAt: '08:00:00', endsAt: '20:00:00', paidMinutes: 660 }),
  calUpdate: vi.fn().mockResolvedValue({}),
  calFetchDetail: vi.fn().mockResolvedValue({
    name: '标准日历',
    workingTimes: [
      { dayOfWeek: 'monday', startsAt: '08:00:00', endsAt: '17:00:00' },
      { dayOfWeek: 'tuesday', startsAt: '08:00:00', endsAt: '17:00:00' },
    ],
    holidays: [{ date: '2026-06-19', name: '端午节' }],
    exceptions: [{ date: '2026-06-20', isWorkingDay: true, reason: '调休' }],
  }),
}))

function stubResource(resourceType: string) {
  const labelByType: Record<string, { code: string, name: string }> = {
    'shift': { code: 'SHIFT-A', name: '白班' },
    'work-calendar': { code: 'CAL-A', name: '标准日历' },
  }
  const entry = labelByType[resourceType]
  const rows = entry ? [{ resourceType, code: entry.code, displayName: entry.name, active: true, snapshotVersion: '1' }] : []
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

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}

async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

beforeEach(() => {
  for (const fn of [stub.create, stub.toastSuccess, stub.toastError, actionStub.shiftUpdate, actionStub.shiftFetchDetail, actionStub.calUpdate, actionStub.calFetchDetail]) {
    fn.mockClear()
  }
})

describe('master-data scheduling page', () => {
  it('renders title and two tabs, no 建设中 placeholder', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('排班与日历')
    const tabs = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabs.some((t) => t.includes('班次'))).toBe(true)
    expect(tabs.some((t) => t.includes('工作日历'))).toBe(true)
    expect(wrapper.text()).toContain('白班')
    // 不再有「建设中 / 待 #373」文案。
    expect(wrapper.text()).not.toContain('建设中')
    expect(wrapper.text()).not.toContain('#373')
  })

  it('shift edit loads time/paid fields editable and update posts startsAt/endsAt/paidMinutes', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...rowActionStubs, ...dialogStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.shiftFetchDetail).toHaveBeenCalledWith('SHIFT-A')
    expect(wrapper.text()).toContain('编辑班次')
    // 编码只读，时段 / 计薪可改（不再 disabled）。
    const codeInput = wrapper.find('#shift-code').element as HTMLInputElement
    expect(codeInput.disabled).toBe(true)
    const startInput = wrapper.find('#shift-start').element as HTMLInputElement
    expect(startInput.disabled).toBe(false)
    expect(startInput.value).toBe('08:00')

    // 编辑态有「班次编码」「班次名称」两个 form？只有一个班次表单（dialogStubs 内联）。
    const shiftForm = wrapper.findAll('form').find((f) => f.find('#shift-code').exists())!
    await shiftForm.trigger('submit')
    await flushPromises()

    expect(actionStub.shiftUpdate).toHaveBeenCalledTimes(1)
    const [code, patch] = actionStub.shiftUpdate.mock.calls[0]!
    expect(code).toBe('SHIFT-A')
    expect(patch.startsAt).toBe('08:00:00')
    expect(patch.endsAt).toBe('20:00:00')
    expect(patch.paidMinutes).toBe(660)
  })

  it('creating a shift posts code/name/paidMinutes and fires success toast', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建班次'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#shift-code').setValue('SHIFT-NEW')
    await wrapper.find('#shift-name').setValue('夜班')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as { code: string, name: string, paidMinutes: number }
    expect(body.code).toBe('SHIFT-NEW')
    expect(body.name).toBe('夜班')
    expect(body.paidMinutes).toBe(480)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('work-calendar tab shows empty month-view guidance before a calendar is selected', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    expect(wrapper.text()).toContain('先在左侧选择或新建一个工作日历')
    // 未选中前不调用 detail。
    expect(actionStub.calFetchDetail).not.toHaveBeenCalled()
  })

  it('selecting a calendar reads real detail and renders month grid with holiday/exception badges', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    const nameBtn = wrapper.findAll('button').find((b) => b.text().trim() === '标准日历')
    expect(nameBtn).toBeTruthy()
    await nameBtn!.trigger('click')
    await flushPromises()

    expect(actionStub.calFetchDetail).toHaveBeenCalledWith('CAL-A')
    // 每周工作模式按周一/周二点亮（来自 workingTimes）。
    expect(wrapper.text()).toContain('每周工作模式')
    // 节假日 / 例外日清单读自真实明细。
    expect(wrapper.text()).toContain('端午节')
    expect(wrapper.text()).toContain('调休')
    // 月历图例存在。
    expect(wrapper.text()).toContain('法定节假日')
    expect(wrapper.text()).toContain('例外日')
  })

  it('adding a holiday writes back via update with the new holiday list', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')
    await wrapper.findAll('button').find((b) => b.text().trim() === '标准日历')!.trigger('click')
    await flushPromises()

    await wrapper.find('#holiday-date').setValue('2026-10-01')
    await wrapper.find('#holiday-name').setValue('国庆节')
    await flushPromises()
    // 提交节假日表单（其内 [添加] 为 type=submit）。
    const holidayForm = wrapper.findAll('form').find((f) => f.find('#holiday-date').exists())!
    await holidayForm.trigger('submit')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [code, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(code).toBe('CAL-A')
    expect(patch.holidays).toEqual(expect.arrayContaining([
      expect.objectContaining({ date: '2026-06-19' }),
      expect.objectContaining({ date: '2026-10-01', name: '国庆节' }),
    ]))
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('toggling a weekday writes back the working-times pattern via update', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')
    await wrapper.findAll('button').find((b) => b.text().trim() === '标准日历')!.trigger('click')
    await flushPromises()

    // 点「周三」（原本未配置）→ 加入工作日。
    const wedBtn = wrapper.findAll('button').find((b) => b.text().trim() === '周三')
    expect(wedBtn).toBeTruthy()
    await wedBtn!.trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(patch.workingTimes).toEqual(expect.arrayContaining([
      expect.objectContaining({ dayOfWeek: 'wednesday' }),
    ]))
  })
})
