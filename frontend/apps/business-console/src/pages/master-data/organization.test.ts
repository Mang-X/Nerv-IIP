import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import OrganizationPage from './organization.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({ data: { code: 'DEPT-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({ name: '总装部' }),
}))

function stubResource(resourceType: string) {
  const labelByType: Record<string, { code: string, name: string }> = {
    'department': { code: 'DEPT-A', name: '总装部' },
    'team': { code: 'TEAM-A', name: '白班班组' },
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

function stubReadonlyResource(resourceType: string) {
  const rows = resourceType === 'personnel-skill'
    ? [{ resourceType, code: 'SKILL-A', displayName: '焊接技能', active: true, snapshotVersion: '1' }]
    : []
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    resources: computed(() => rows),
    resourcesTotal: computed(() => rows.length),
    resourcesError: shallowRef(undefined),
    resourcesPending: shallowRef(false),
    refreshResources: vi.fn(),
  }
}

function stubActions() {
  return {
    update: actionStub.update,
    disable: vi.fn(),
    enable: vi.fn(),
    fetchDetail: actionStub.fetchDetail,
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }
}

function stubWorkers() {
  const rows = [
    { userId: 'usr-1', displayName: '张三', employeeNo: 'E001', department: '总装部', status: 'active' },
    { userId: 'usr-2', displayName: '李四', employeeNo: 'E002', department: '焊接部', status: 'active' },
  ]
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', keyword: undefined, pageIndex: 0, pageSize: 100 }),
    refresh: vi.fn(),
    workers: computed(() => rows),
    workersError: shallowRef(undefined),
    workersPending: shallowRef(false),
    workersTotal: computed(() => rows.length),
  }
}

function stubTeamMembers() {
  const rows = [{ teamCode: 'TEAM-A', userId: 'usr-1', isLeader: true, active: true }]
  return {
    members: computed(() => rows),
    membersError: shallowRef(undefined),
    membersPending: shallowRef(false),
    refresh: vi.fn(),
    addMember: vi.fn(),
    addPending: shallowRef(false),
    removeMember: vi.fn(),
    removePending: shallowRef(false),
    memberError: shallowRef(undefined),
  }
}

function stubSkillAssignment() {
  return {
    assign: vi.fn(),
    assignPending: shallowRef(false),
    assignError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useBusinessMasterDataResources: (resourceType: string) => stubReadonlyResource(resourceType),
  useMasterDataResourceActions: () => stubActions(),
  useBusinessWorkers: () => stubWorkers(),
  useTeamMembers: () => stubTeamMembers(),
  usePersonnelSkillAssignment: () => stubSkillAssignment(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 把 RowActions 的下拉换成同步渲染插槽的轻量桩，让「编辑」菜单项可直接点击。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 对话框就地渲染（不 teleport），便于断言/填写表单内容。
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
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

// 切到某个 Tab（reka-ui：focus + mousedown 激活）。
async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

describe('master-data organization page', () => {
  it('renders the title, five tabs, sample row and create button', async () => {
    const wrapper = mount(OrganizationPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('组织与人员')
    const tabs = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabs.some((t) => t.includes('部门'))).toBe(true)
    expect(tabs.some((t) => t.includes('班组'))).toBe(true)
    expect(tabs.some((t) => t.includes('班次'))).toBe(true)
    expect(tabs.some((t) => t.includes('工作日历'))).toBe(true)
    expect(tabs.some((t) => t.includes('人员技能'))).toBe(true)

    expect(wrapper.text()).toContain('总装部')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建部门'))).toBe(true)
  })

  it('exposes per-row actions (detail / rename / disable)', async () => {
    const wrapper = mount(OrganizationPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('offers a 管理成员 entry on team rows and an enabled 登记技能 button', async () => {
    const wrapper = mount(OrganizationPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const teamTab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes('班组'))
    await teamTab!.trigger('focus')
    await teamTab!.trigger('mousedown')
    await flushPromises()
    expect(wrapper.findAll('button').some((b) => b.text().includes('管理成员'))).toBe(true)

    const skillTab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes('人员技能'))
    await skillTab!.trigger('focus')
    await skillTab!.trigger('mousedown')
    await flushPromises()
    const registerButton = wrapper.findAll('button').find((b) => b.text().includes('登记技能'))
    expect(registerButton).toBeDefined()
    expect(registerButton!.attributes('disabled')).toBeUndefined()
  })

  it('opens the department dialog in edit mode (full-field) when a department row 编辑 is triggered', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    // 默认即「部门」Tab，行内 RowActions 已就地渲染「编辑」。
    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 部门详情被拉取用于回填。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('DEPT-A')
    // 对话框进入编辑态：标题含「编辑部门」，部门编码只读。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑部门')
    const codeInput = document.getElementById('dept-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('blocks department create on empty required fields with a summary alert and no create call', async () => {
    stub.create.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 打开「新建部门」（重置后 code/name 为空 → 非法）。
    await wrapper.findAll('button').find((b) => b.text().includes('新建部门'))!.trigger('click')
    await flushPromises()

    // 部门对话框 teleport 到 body，从 body 取就地表单触发提交。
    const form = document.body.querySelector('form')
    expect(form).toBeTruthy()
    form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('部门主表单：填全必填后提交调用 create 并弹成功 toast', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建部门'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#dept-code').setValue('DEPT-NEW')
    await wrapper.find('#dept-name').setValue('焊接部')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as { code: string, name: string }
    expect(body.code).toBe('DEPT-NEW')
    expect(body.name).toBe('焊接部')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('部门主表单：提交失败弹错误 toast（人话）且不重置', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.create.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建部门'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#dept-code').setValue('DEPT-NEW')
    await wrapper.find('#dept-name').setValue('焊接部')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    expect((wrapper.find('#dept-name').element as HTMLInputElement).value).toBe('焊接部')
  })

  it('班组子表单：行「编辑」拉详情、对话框进入编辑态、编码只读', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '班组')

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('TEAM-A')
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑班组')
    const codeInput = document.getElementById('team-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('班组子表单：必填留空提交出现汇总提示且不发 create', async () => {
    stub.create.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '班组')

    await wrapper.findAll('button').find((b) => b.text().includes('新建班组'))!.trigger('click')
    await flushPromises()
    expect(wrapper.find('#team-code').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('班次子表单：行「编辑」拉详情、对话框进入编辑态、编码只读', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '班次')

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('SHIFT-A')
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑班次')
    const codeInput = document.getElementById('shift-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('班次子表单：必填留空提交出现汇总提示且不发 create', async () => {
    stub.create.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '班次')

    await wrapper.findAll('button').find((b) => b.text().includes('新建班次'))!.trigger('click')
    await flushPromises()
    // 班次新建态默认时段/计薪合法，但编码/名称为空 → 非法。
    expect(wrapper.find('#shift-code').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('工作日历子表单：行「编辑」拉详情、对话框进入编辑态、编码只读', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('CAL-A')
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑工作日历')
    const codeInput = document.getElementById('cal-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('工作日历子表单：必填留空提交出现汇总提示且不发 create', async () => {
    stub.create.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '工作日历')

    await wrapper.findAll('button').find((b) => b.text().includes('新建工作日历'))!.trigger('click')
    await flushPromises()
    expect(wrapper.find('#cal-code').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })
})

// WorkerSelect 的下拉来自 reka-ui Select（内容懒挂载到 body），单测里把 Select 原语
// 替换成同步渲染插槽的轻量桩，以断言「选项展示工人姓名 / 工号，而非 userId」。
const selectStubs = {
  Select: { template: '<div><slot /></div>' },
  SelectTrigger: { template: '<button type="button" role="combobox"><slot /></button>' },
  SelectValue: { props: ['placeholder'], template: '<span>{{ placeholder }}</span>' },
  SelectContent: { template: '<div><slot /></div>' },
  SelectItem: { props: ['value'], template: '<div role="option"><slot /></div>' },
  Input: { template: '<input />' },
}

describe('worker selector', () => {
  it('renders worker names (not userId) as options', async () => {
    const WorkerSelect = (await import('@/components/masterData/WorkerSelect.vue')).default
    const wrapper = mount(WorkerSelect, {
      props: { modelValue: '' },
      global: { stubs: selectStubs },
    })
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('张三')
    expect(text).toContain('E001')
    expect(text).toContain('总装部')
    expect(text).not.toContain('usr-1')

    wrapper.unmount()
  })
})
