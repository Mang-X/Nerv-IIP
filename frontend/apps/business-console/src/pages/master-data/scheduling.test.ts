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
// 班次行操作：保留 RowActions 内的下拉项（编辑/停用）以便点击「编辑」。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 工作日历左列：压平行尾「⋯」菜单 + StatusBadge，让整行按钮的可见文本就是日历名。
const calRowActionStubs = {
  MasterDataRowActions: { template: '<span data-testid="row-actions" />' },
  StatusBadge: { template: '<span />' },
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
// 抽屉照 dialog 风格内联展开，使其内容在挂载后即可断言。
const sheetStubs = {
  Sheet: { template: '<div><slot /></div>' },
  SheetContent: { template: '<div><slot /></div>' },
  SheetHeader: { template: '<div><slot /></div>' },
  SheetTitle: { template: '<h2><slot /></h2>' },
  SheetDescription: { template: '<p><slot /></p>' },
}
// DatePicker 暴露一个原生 date input，让测试可 setValue 完成日期录入。
const datePickerStub = {
  DatePicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input type="date" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value || null)" />',
  },
}
// 把 reka-ui Select 换成原生 <select>，让测试能 setValue。
const formSelectStubs = {
  Select: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectContent: { template: '<slot />' },
  SelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

// AlertDialog 内联展开：Trigger 渲染其插槽（即垃圾桶按钮），Action 渲染为可点击按钮，
// 让测试能断言「点删 → 出现确认 → 确认后才调 remove」。
const alertDialogStubs = {
  AlertDialog: { template: '<div><slot /></div>' },
  AlertDialogTrigger: { template: '<div><slot /></div>' },
  AlertDialogContent: { template: '<div data-testid="confirm"><slot /></div>' },
  AlertDialogHeader: { template: '<div><slot /></div>' },
  AlertDialogFooter: { template: '<div><slot /></div>' },
  AlertDialogTitle: { template: '<h2><slot /></h2>' },
  AlertDialogDescription: { template: '<p><slot /></p>' },
  AlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogAction: { emits: ['click'], template: '<button type="button" data-testid="confirm-delete" @click="$emit(\'click\', $event)"><slot /></button>' },
}

const calStubs = { ...layoutStub, ...calRowActionStubs, ...sheetStubs, ...datePickerStub, ...formSelectStubs, ...alertDialogStubs }

async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

// 整行可点的日历选择按钮（文本含日历名；排除行尾占位）。
function findCalRowButton(wrapper: ReturnType<typeof mount>, name: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === name)
}

async function selectStandardCalendar(wrapper: ReturnType<typeof mount>) {
  await switchTab(wrapper, '工作日历')
  const rowBtn = findCalRowButton(wrapper, '标准日历')
  expect(rowBtn).toBeTruthy()
  await rowBtn!.trigger('click')
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

  it('creating a shift posts name/paidMinutes (no code — system-assigned) and fires success toast', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建班次'))!.trigger('click')
    await flushPromises()
    // 新建态不再有编码输入框（编码由系统自动生成）。
    expect(wrapper.find('#shift-code').exists()).toBe(false)
    await wrapper.find('#shift-name').setValue('夜班')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as { code?: string, name: string, paidMinutes: number }
    expect(body.code).toBeUndefined()
    expect(body.name).toBe('夜班')
    expect(body.paidMinutes).toBe(480)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('work-calendar tab shows empty month-view guidance before a calendar is selected', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    expect(wrapper.text()).toContain('先在左侧选择或新建一个工作日历')
    // 未选中前不调用 detail。
    expect(actionStub.calFetchDetail).not.toHaveBeenCalled()
  })

  it('clicking anywhere on a calendar row (not just the name text) selects it and reads detail', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    // 整行就是一个 <button>，点中其内任意处都触发选中。
    const rowBtn = findCalRowButton(wrapper, '标准日历')
    expect(rowBtn).toBeTruthy()
    expect(rowBtn!.element.tagName).toBe('BUTTON')
    await rowBtn!.trigger('click')
    await flushPromises()

    expect(actionStub.calFetchDetail).toHaveBeenCalledWith('CAL-A')
    // 选中后行带 aria-current。
    expect(rowBtn!.attributes('aria-current')).toBe('true')
  })

  it('selecting a calendar reads real detail and renders month grid with holiday/exception data', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    expect(actionStub.calFetchDetail).toHaveBeenCalledWith('CAL-A')
    // 每周工作模式 chip 在月历上方。
    expect(wrapper.text()).toContain('每周工作模式')
    // 月历图例存在。
    expect(wrapper.text()).toContain('法定节假日')
    expect(wrapper.text()).toContain('例外日')
  })

  it('weekly-mode chips render above the month grid and toggling writes the pattern back via update', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    // 周一/周二来自 workingTimes → 点亮（default variant）；周三未配置。
    const wedBtn = wrapper.findAll('button').find((b) => b.text().trim() === '周三')
    expect(wedBtn).toBeTruthy()
    // chip 位置：每周工作模式标题在月历 7 列网格之前（在月历上方）。
    const html = wrapper.html()
    expect(html.indexOf('每周工作模式')).toBeLessThan(html.indexOf('grid-cols-7'))

    await wedBtn!.trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(patch.workingTimes).toEqual(expect.arrayContaining([
      expect.objectContaining({ dayOfWeek: 'wednesday' }),
    ]))
  })

  it('opens the holiday/exception sheet from the 管理 button and lists existing detail there', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    const manageBtn = wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))
    expect(manageBtn).toBeTruthy()
    await manageBtn!.trigger('click')
    await flushPromises()

    // 抽屉内含读自真实明细的节假日 / 例外日清单。
    expect(wrapper.text()).toContain('端午节')
    expect(wrapper.text()).toContain('调休')
  })

  it('adding a holiday via the DatePicker writes back the new holiday list through update', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    await wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))!.trigger('click')
    await flushPromises()

    // 抽屉内的节假日表单：DatePicker(原生 date 桩) + 名称。
    const holidayForm = wrapper.findAll('form').find((f) => f.find('#holiday-name').exists())!
    await holidayForm.find('input[type="date"]').setValue('2026-10-01')
    await holidayForm.find('#holiday-name').setValue('国庆节')
    await flushPromises()
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

  it('adding an exception via DatePicker + Select writes back exceptions through update', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    await wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))!.trigger('click')
    await flushPromises()

    // 例外日表单：含「原因」字段，借此定位该 form。
    const exceptionForm = wrapper.findAll('form').find((f) => f.find('#exception-reason').exists())!
    await exceptionForm.find('input[type="date"]').setValue('2026-10-08')
    await exceptionForm.find('select').setValue('false') // 当日休息
    await exceptionForm.find('#exception-reason').setValue('补休')
    await flushPromises()
    await exceptionForm.trigger('submit')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [code, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(code).toBe('CAL-A')
    expect(patch.exceptions).toEqual(expect.arrayContaining([
      expect.objectContaining({ date: '2026-06-20', isWorkingDay: true }),
      expect.objectContaining({ date: '2026-10-08', isWorkingDay: false, reason: '补休' }),
    ]))
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('deleting a holiday asks for confirmation first, then calls remove (update) only after confirm', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    await wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))!.trigger('click')
    await flushPromises()

    // 找到节假日删除触发按钮（aria-label）；点它本身不应直接调 update。
    const delTrigger = wrapper.findAll('button').find((b) => b.attributes('aria-label') === '删除节假日')
    expect(delTrigger).toBeTruthy()
    await delTrigger!.trigger('click')
    await flushPromises()
    expect(actionStub.calUpdate).not.toHaveBeenCalled()

    // 确认文案出现。
    expect(wrapper.text()).toContain('确定删除节假日')

    // 点「确认删除」后才真正写回（删掉端午节）。
    const confirmBtn = wrapper.findAll('[data-testid="confirm-delete"]').at(0)!
    await confirmBtn.trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [code, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(code).toBe('CAL-A')
    expect(patch.holidays.some((h: { date?: string }) => h.date === '2026-06-19')).toBe(false)
  })

  it('deleting an exception asks for confirmation first, then calls remove only after confirm', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    await wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))!.trigger('click')
    await flushPromises()

    const delTrigger = wrapper.findAll('button').find((b) => b.attributes('aria-label') === '删除例外日')
    expect(delTrigger).toBeTruthy()
    await delTrigger!.trigger('click')
    await flushPromises()
    expect(actionStub.calUpdate).not.toHaveBeenCalled()

    expect(wrapper.text()).toContain('确定删除例外日')

    // 在「确定删除例外日」那个确认弹层内点确认（按弹层文案定位，避开互斥冲突弹层）。
    const exDialog = wrapper.findAll('[data-testid="confirm"]').find((d) => d.text().includes('确定删除例外日'))!
    await exDialog.find('[data-testid="confirm-delete"]').trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(patch.exceptions.some((e: { date?: string }) => e.date === '2026-06-20')).toBe(false)
  })

  it('adding a holiday on a date that already has an exception prompts a conflict confirm, then replaces the exception with the holiday', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    await wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))!.trigger('click')
    await flushPromises()

    // 2026-06-20 当前是例外日（调休）。在节假日表单为该日加节假日 → 应弹冲突确认，先不写回。
    const holidayForm = wrapper.findAll('form').find((f) => f.find('#holiday-name').exists())!
    await holidayForm.find('input[type="date"]').setValue('2026-06-20')
    await flushPromises()
    await holidayForm.trigger('submit')
    await flushPromises()

    expect(actionStub.calUpdate).not.toHaveBeenCalled()
    const conflictDialog = wrapper.findAll('[data-testid="confirm"]').find((d) => d.text().includes('已设为例外日'))
    expect(conflictDialog).toBeTruthy()

    // 确认替换：删掉该日例外日、加节假日，单次写回。
    await conflictDialog!.find('[data-testid="confirm-delete"]').trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [code, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(code).toBe('CAL-A')
    expect(patch.exceptions.some((e: { date?: string }) => e.date === '2026-06-20')).toBe(false)
    expect(patch.holidays.some((h: { date?: string }) => h.date === '2026-06-20')).toBe(true)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('adding an exception on a date that already has a holiday prompts a conflict confirm, then replaces the holiday with the exception', async () => {
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    await wrapper.findAll('button').find((b) => b.text().includes('管理节假日'))!.trigger('click')
    await flushPromises()

    // 2026-06-19 当前是节假日（端午节）。在例外日表单为该日加例外日 → 应弹冲突确认。
    const exceptionForm = wrapper.findAll('form').find((f) => f.find('#exception-reason').exists())!
    await exceptionForm.find('input[type="date"]').setValue('2026-06-19')
    await exceptionForm.find('select').setValue('false')
    await exceptionForm.find('#exception-reason').setValue('替换')
    await flushPromises()
    await exceptionForm.trigger('submit')
    await flushPromises()

    expect(actionStub.calUpdate).not.toHaveBeenCalled()
    const conflictDialog = wrapper.findAll('[data-testid="confirm"]').find((d) => d.text().includes('已是节假日'))
    expect(conflictDialog).toBeTruthy()

    await conflictDialog!.find('[data-testid="confirm-delete"]').trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [code, patch] = actionStub.calUpdate.mock.calls[0]!
    expect(code).toBe('CAL-A')
    expect(patch.holidays.some((h: { date?: string }) => h.date === '2026-06-19')).toBe(false)
    expect(patch.exceptions.some((e: { date?: string, isWorkingDay?: boolean }) => e.date === '2026-06-19' && e.isWorkingDay === false)).toBe(true)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('persistCalendar de-duplicates workingTimes/holidays/exceptions before sending', async () => {
    // 注入带重复项的明细，断言写回的 patch 中各集合已按键去重（保留第一条）。
    actionStub.calFetchDetail.mockResolvedValueOnce({
      name: '标准日历',
      workingTimes: [
        { dayOfWeek: 'monday', startsAt: '08:00:00', endsAt: '17:00:00' },
        { dayOfWeek: 'monday', startsAt: '09:00:00', endsAt: '18:00:00' },
        { dayOfWeek: 'tuesday', startsAt: '08:00:00', endsAt: '17:00:00' },
      ],
      holidays: [
        { date: '2026-06-19', name: '端午节' },
        { date: '2026-06-19', name: '端午节(重复)' },
      ],
      exceptions: [
        { date: '2026-06-20', isWorkingDay: true, reason: '调休' },
        { date: '2026-06-20', isWorkingDay: false, reason: '调休(重复)' },
      ],
    })
    const wrapper = mount(SchedulingPage, { global: { stubs: calStubs } })
    await flushPromises()
    await selectStandardCalendar(wrapper)

    // 触发一次写回：切换周三（toggleWeekday → persistCalendar）。
    const wedBtn = wrapper.findAll('button').find((b) => b.text().trim() === '周三')!
    await wedBtn.trigger('click')
    await flushPromises()

    expect(actionStub.calUpdate).toHaveBeenCalledTimes(1)
    const [, patch] = actionStub.calUpdate.mock.calls[0]!

    // workingTimes 按 dayOfWeek 去重：monday 仅一条（保留第一条 08:00）。
    const mondays = patch.workingTimes.filter((w: { dayOfWeek?: string }) => w.dayOfWeek === 'monday')
    expect(mondays).toHaveLength(1)
    expect(mondays[0].startsAt).toBe('08:00:00')

    // holidays 按 date 去重。
    expect(patch.holidays.filter((h: { date?: string }) => h.date === '2026-06-19')).toHaveLength(1)
    expect(patch.holidays[0].name).toBe('端午节')

    // exceptions 按 date 去重（保留第一条 isWorkingDay=true）。
    const ex = patch.exceptions.filter((e: { date?: string }) => e.date === '2026-06-20')
    expect(ex).toHaveLength(1)
    expect(ex[0].isWorkingDay).toBe(true)
  })
})
