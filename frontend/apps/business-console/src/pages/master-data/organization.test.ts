import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import OrganizationPage from './organization.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn(),
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
  toast: { success: vi.fn(), error: vi.fn() },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 把 RowActions 的下拉换成同步渲染插槽的轻量桩，让「编辑」菜单项可直接点击。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
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
