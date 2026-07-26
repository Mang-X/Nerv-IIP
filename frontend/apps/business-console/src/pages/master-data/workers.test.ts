import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import WorkersPage from './workers.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({}),
  update: vi.fn().mockResolvedValue({}),
  disable: vi.fn().mockResolvedValue({}),
  enable: vi.fn().mockResolvedValue({}),
  refresh: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  // 与 types.gen.ts 的 BusinessConsoleWorkerDirectoryItem 字段一一对应。
  workers: [
    {
      userId: 'user-op-001',
      employeeNo: 'EMP-1001',
      displayName: '陈志强',
      departmentCode: 'DEPT-PROD',
      departmentName: '生产部',
      jobTitle: '装配班组长',
      employmentStatus: 'active',
      phone: null,
      active: true,
      teams: [
        { teamCode: 'TEAM-CNC', teamName: 'CNC 精加工班组', isLeader: true, workCenterCode: 'WC-CNC' },
      ],
      skills: [{ skillCode: 'cnc-operation', skillName: 'CNC 操作', level: 'senior' }],
      snapshotVersion: '1',
    },
    {
      userId: 'user-op-005',
      employeeNo: 'EMP-1005',
      displayName: '何俊',
      departmentCode: 'DEPT-PROD',
      departmentName: '生产部',
      jobTitle: '焊接操作工',
      employmentStatus: 'on-leave',
      phone: null,
      active: true,
      teams: [],
      skills: [],
      snapshotVersion: '1',
    },
  ],
}))

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  keyword: undefined as string | undefined,
  departmentCode: undefined as string | undefined,
  pageIndex: 1,
  pageSize: 20,
})

vi.mock('@/composables/useBusinessMasterData', () => ({
  useWorkerRegistry: () => ({
    filters,
    workers: computed(() => stub.workers),
    workersError: shallowRef(undefined),
    workersPending: shallowRef(false),
    workersTotal: computed(() => stub.workers.length),
    refresh: stub.refresh,
    create: stub.create,
    createPending: shallowRef(false),
    createError: shallowRef(undefined),
    update: stub.update,
    updatePending: shallowRef(false),
    disable: stub.disable,
    disablePending: shallowRef(false),
    enable: stub.enable,
    enablePending: shallowRef(false),
    fetchDetail: vi.fn(),
    actionError: computed(() => undefined),
  }),
  useMasterDataResource: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    items: computed(() => [
      { resourceType: 'department', code: 'DEPT-PROD', displayName: '生产部', active: true },
    ]),
    total: computed(() => 1),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    create: vi.fn(),
    createError: shallowRef(undefined),
    createPending: shallowRef(false),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  NvDialog: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  // NvAlertDialogContent 含 reka portal/Teleport，jsdom 卸载会崩——就地渲染。
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  NvAlertDialogAction: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}
const selectStubs = {
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

describe('master-data workers page', () => {
  it('renders employee number, name, department, teams, skills and duty status', async () => {
    const wrapper = mount(WorkersPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('EMP-1001')
    expect(text).toContain('陈志强')
    expect(text).toContain('生产部')
    expect(text).toContain('CNC 精加工班组 · 组长')
    expect(text).toContain('CNC 操作')
    expect(text).toContain('在岗')
    expect(text).toContain('休假')
    // 内部人员标识不进业务界面。
    expect(text).not.toContain('user-op-001')
  })

  it('blocks creation without a name and never calls the facade', async () => {
    stub.create.mockClear()
    const wrapper = mount(WorkersPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新增员工'))!.trigger('click')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请填写姓名')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('creates a worker with the selected department and duty status', async () => {
    stub.create.mockClear()
    const wrapper = mount(WorkersPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新增员工'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#worker-name').setValue('周立新')
    const departmentSelect = wrapper.findAll('select').find((s) => s.html().includes('DEPT-PROD'))!
    await departmentSelect.setValue('DEPT-PROD')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as Record<string, unknown>
    expect(body.name).toBe('周立新')
    expect(body.departmentCode).toBe('DEPT-PROD')
    expect(body.employmentStatus).toBe('active')
    // 工号由系统分配，前端不编号。
    expect(body.code).toBeNull()
  })

  it('edits a worker through the employee number as its identity', async () => {
    stub.update.mockClear()
    const wrapper = mount(WorkersPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } },
    })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text() === '编辑')!.trigger('click')
    await flushPromises()
    await wrapper.find('#worker-title').setValue('装配主管')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.update).toHaveBeenCalledTimes(1)
    expect(stub.update.mock.calls[0]![0]).toBe('EMP-1001')
    expect(stub.update.mock.calls[0]![1]).toMatchObject({ jobTitle: '装配主管', name: '陈志强' })
  })
})
