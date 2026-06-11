import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import OrganizationPage from './organization.vue'

const stub = vi.hoisted(() => ({
  createDept: vi.fn().mockResolvedValue({ data: { code: 'DEPT-NEW' } }),
  createTeam: vi.fn().mockResolvedValue({ data: { code: 'TEAM-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

// 列表回传 parentDepartmentCode（#375）→ 前端拼多层部门树：
// DEPT-A（总装部，根）▸ DEPT-A1（总装一工段，挂 DEPT-A）；DEPT-B（焊接部，根）。
const DEPT_ROWS = [
  { resourceType: 'department', code: 'DEPT-A', displayName: '总装部', active: true, snapshotVersion: '1' },
  { resourceType: 'department', code: 'DEPT-A1', displayName: '总装一工段', active: true, snapshotVersion: '1', parentDepartmentCode: 'DEPT-A' },
  { resourceType: 'department', code: 'DEPT-B', displayName: '焊接部', active: true, snapshotVersion: '1' },
]
// 列表回传 team.departmentCode（#375）→ 班组按部门归集：TEAM-A 属 DEPT-A、TEAM-B 属 DEPT-B。
const TEAM_ROWS = [
  { resourceType: 'team', code: 'TEAM-A', displayName: '白班班组', active: true, snapshotVersion: '1', departmentCode: 'DEPT-A', shiftCode: 'SHIFT-A' },
  { resourceType: 'team', code: 'TEAM-B', displayName: '焊工班组', active: true, snapshotVersion: '1', departmentCode: 'DEPT-B', shiftCode: 'SHIFT-A' },
]
const SHIFT_ROWS = [
  { resourceType: 'shift', code: 'SHIFT-A', displayName: '白班', active: true, snapshotVersion: '1' },
]

const CREATE_BY_TYPE: Record<string, ReturnType<typeof vi.fn>> = {
  department: stub.createDept,
  team: stub.createTeam,
}

function stubResource(resourceType: string) {
  const rows = resourceType === 'department'
    ? DEPT_ROWS
    : resourceType === 'team'
      ? TEAM_ROWS
      : SHIFT_ROWS
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 200 }),
    items: computed(() => rows),
    total: computed(() => rows.length),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    create: CREATE_BY_TYPE[resourceType] ?? vi.fn().mockResolvedValue({}),
    createError: shallowRef(undefined),
    createPending: shallowRef(false),
  }
}

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({ name: '总装部' }),
}))

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

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useMasterDataResourceActions: () => stubActions(),
  useBusinessWorkers: () => stubWorkers(),
  useTeamMembers: () => stubTeamMembers(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 对话框就地渲染（不 teleport），便于断言/填写表单内容。
const dialogStubs = {
  Dialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
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

// 找到树里某节点的「选中」按钮（按文本，排除带「新建」aria-label 的 + 按钮）。
function findNodeButton(wrapper: ReturnType<typeof mount>, label: string) {
  return wrapper.findAll('button').find((b) => b.text().includes(label) && !b.attributes('aria-label')?.includes('新建'))
}

const mountOpts = { global: { stubs: { ...layoutStub, ...dialogStubs } } }

describe('master-data organization (department tree) page', () => {
  it('renders title, hint and department tree nodes', async () => {
    const wrapper = mount(OrganizationPage, mountOpts)
    await flushPromises()

    expect(wrapper.text()).toContain('组织与班组')
    expect(wrapper.text()).toContain('总装部')
    expect(wrapper.text()).toContain('焊接部')
  })

  it('selecting a department shows its detail with breadcrumb and team section', async () => {
    const wrapper = mount(OrganizationPage, mountOpts)
    await flushPromises()
    // 挂载即默认选中首个部门（DEPT-A）；点第二个部门触发选中变化 → 拉详情。
    actionStub.fetchDetail.mockClear()

    await findNodeButton(wrapper, '焊接部')!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('DEPT-B')
    expect(wrapper.text()).toContain('部门编码')
    const nav = wrapper.find('[aria-label="选中路径"]')
    expect(nav.exists()).toBe(true)
    expect(nav.text()).toContain('焊接部')
    // 右侧出现该部门的班组区与成员维护出口。
    expect(wrapper.text()).toContain('班组')
    expect(wrapper.findAll('button').some((b) => b.text().includes('管理成员'))).toBe(true)
    // 班组按部门归集：选中 DEPT-B 只列其班组（TEAM-B），不混入别部门的 TEAM-A。
    expect(wrapper.text()).toContain('焊工班组')
    expect(wrapper.text()).not.toContain('白班班组')
  })

  it('renders a multi-level department tree from parentDepartmentCode', async () => {
    const wrapper = mount(OrganizationPage, mountOpts)
    await flushPromises()

    // 子部门由 parentDepartmentCode 挂到上级下；默认展开（节点少）→ 应可见。
    expect(wrapper.text()).toContain('总装部')
    expect(wrapper.text()).toContain('总装一工段')
    // 子部门是 treeitem，且其父行 aria-expanded 反映层级（有子级）。
    const parentItem = wrapper.findAll('[role="treeitem"]').find((li) => li.text().includes('总装部'))
    expect(parentItem?.attributes('aria-expanded')).toBe('true')
  })

  it('班组按部门归集：选中部门只列归属该部门的班组', async () => {
    const wrapper = mount(OrganizationPage, mountOpts)
    await flushPromises()

    // 挂载默认选中首个部门 DEPT-A → 只列 TEAM-A（白班班组）。
    expect(wrapper.text()).toContain('白班班组')
    expect(wrapper.text()).not.toContain('焊工班组')
  })

  it('「新建子部门」prefills parent department code read-only', async () => {
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await findNodeButton(wrapper, '总装部')!.trigger('click')
    await flushPromises()

    const createChildBtn = wrapper.findAll('button').find((b) => b.text().includes('新建子部门'))
    expect(createChildBtn).toBeTruthy()
    await createChildBtn!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('新建子部门')
    const parentInput = wrapper.find('#dept-parent').element as HTMLInputElement
    expect(parentInput.value).toBe('DEPT-A')
    expect(parentInput.disabled).toBe(true)
  })

  it('在此部门下新建班组 prefills departmentCode read-only', async () => {
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await findNodeButton(wrapper, '总装部')!.trigger('click')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('在此部门下新建班组'))!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('新建班组')
    const deptInput = wrapper.find('#team-dept-locked').element as HTMLInputElement
    expect(deptInput.value).toBe('DEPT-A')
    expect(deptInput.disabled).toBe(true)
  })

  it('creating a root department posts code/name and fires success toast', async () => {
    stub.createDept.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建部门'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#dept-code').setValue('DEPT-NEW')
    await wrapper.find('#dept-name').setValue('喷涂部')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createDept).toHaveBeenCalledTimes(1)
    const body = stub.createDept.mock.calls[0]![0] as { code: string, name: string }
    expect(body.code).toBe('DEPT-NEW')
    expect(body.name).toBe('喷涂部')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('blocks department create on empty required fields with summary alert and no create call', async () => {
    stub.createDept.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建部门'))!.trigger('click')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createDept).not.toHaveBeenCalled()
  })

  it('creating a team under a department posts prefilled departmentCode + shiftCode', async () => {
    stub.createTeam.mockClear()
    stub.toastSuccess.mockClear()
    const wrapper = mount(OrganizationPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await findNodeButton(wrapper, '总装部')!.trigger('click')
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().includes('在此部门下新建班组'))!.trigger('click')
    await flushPromises()

    await wrapper.find('#team-code').setValue('TEAM-NEW')
    await wrapper.find('#team-name').setValue('夜班班组')
    // 班次 Select 桩渲染为 <select>；按其 SHIFT-A 选项定位（id 落在 SelectTrigger span 上）。
    const shiftSelect = wrapper.findAll('select').find((s) => s.html().includes('SHIFT-A'))!
    await shiftSelect.setValue('SHIFT-A')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createTeam).toHaveBeenCalledTimes(1)
    const body = stub.createTeam.mock.calls[0]![0] as { code: string, name: string, departmentCode: string, shiftCode: string }
    expect(body.code).toBe('TEAM-NEW')
    expect(body.departmentCode).toBe('DEPT-A')
    expect(body.shiftCode).toBe('SHIFT-A')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('editing a department: code read-only, can rehome to another parent (update gets new parentDepartmentCode)', async () => {
    actionStub.fetchDetail.mockClear()
    actionStub.update.mockClear()
    const rowActionStubs = {
      RowActions: { template: '<div><slot /></div>' },
      DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
    }
    const wrapper = mount(OrganizationPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs, ...rowActionStubs } },
    })
    await flushPromises()

    // 选中子部门 DEPT-A1（默认展开可见），打开编辑。
    await findNodeButton(wrapper, '总装一工段')!.trigger('click')
    await flushPromises()
    actionStub.fetchDetail.mockClear()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('DEPT-A1')
    expect(wrapper.text()).toContain('编辑部门')
    const codeInput = wrapper.find('#dept-edit-code').element as HTMLInputElement
    expect(codeInput.disabled).toBe(true)
    // 改挂上级：把 DEPT-A1 从 DEPT-A 改挂到 DEPT-B。
    const parentSelect = wrapper.findAll('select').find((s) => s.html().includes('DEPT-B'))!
    await parentSelect.setValue('DEPT-B')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(actionStub.update).toHaveBeenCalledWith('DEPT-A1', expect.objectContaining({ parentDepartmentCode: 'DEPT-B' }))
  })

  it('防环：编辑部门时上级候选排除自身及其子孙', async () => {
    const rowActionStubs = {
      RowActions: { template: '<div><slot /></div>' },
      DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
    }
    const wrapper = mount(OrganizationPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs, ...rowActionStubs } },
    })
    await flushPromises()

    // 选中父部门 DEPT-A（其子孙含自身 DEPT-A 与 DEPT-A1），打开编辑。
    await findNodeButton(wrapper, '总装部')!.trigger('click')
    await flushPromises()
    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    await editItem!.trigger('click')
    await flushPromises()

    // 上级下拉（formSelect 桩把 options 渲染进 <select>）：含其他根 DEPT-B。
    const parentSelect = wrapper.findAll('select').find((s) => s.html().includes('DEPT-B'))!
    expect(parentSelect).toBeTruthy()
    const html = parentSelect.html()
    // 防环：不含子孙（value="DEPT-A1"）与自身（value="DEPT-A"），但含其他根 DEPT-B。
    expect(html).not.toContain('value="DEPT-A1"')
    expect(html).not.toContain('value="DEPT-A"')
    expect(html).toContain('value="DEPT-B"')
  })

  it('editing a team: can rehome department/shift (update gets new departmentCode + shiftCode)', async () => {
    actionStub.update.mockClear()
    const rowActionStubs = {
      RowActions: { template: '<div><slot /></div>' },
      DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
    }
    const wrapper = mount(OrganizationPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs, ...rowActionStubs } },
    })
    await flushPromises()

    // 默认选中 DEPT-A → 列出 TEAM-A（白班班组）；打开其行编辑（班组行 RowActions 在部门 RowActions 之后）。
    expect(wrapper.text()).toContain('白班班组')
    const editButtons = wrapper.findAll('button').filter((b) => b.text().trim() === '编辑')
    // 末个「编辑」属班组行（部门详情区的「编辑」在前）。
    await editButtons[editButtons.length - 1]!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('编辑班组')
    // 班组编码只读。
    expect((wrapper.find('#team-code').element as HTMLInputElement).disabled).toBe(true)
    // 改挂部门：DEPT-A → DEPT-B（部门下拉可改，非只读）。
    const deptSelect = wrapper.findAll('select').find((s) => s.html().includes('DEPT-B') && s.html().includes('DEPT-A'))!
    await deptSelect.setValue('DEPT-B')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(actionStub.update).toHaveBeenCalledWith(
      'TEAM-A',
      expect.objectContaining({ departmentCode: 'DEPT-B', shiftCode: 'SHIFT-A' }),
    )
  })
})
