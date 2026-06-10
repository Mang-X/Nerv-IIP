import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SkillsPage from './skills.vue'

const stub = vi.hoisted(() => ({
  assign: vi.fn().mockResolvedValue({}),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

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
    update: vi.fn(),
    disable: vi.fn(),
    enable: vi.fn(),
    fetchDetail: vi.fn().mockResolvedValue({}),
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

function stubSkillAssignment() {
  return {
    assign: stub.assign,
    assignPending: shallowRef(false),
    assignError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessMasterDataResources: (resourceType: string) => stubReadonlyResource(resourceType),
  useMasterDataResourceActions: () => stubActions(),
  useBusinessWorkers: () => stubWorkers(),
  usePersonnelSkillAssignment: () => stubSkillAssignment(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}
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
  WorkerSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value="usr-1">张三</option></select>',
  },
}

describe('master-data skills page', () => {
  it('renders title, a disabled 矩阵视图 entry marked 建设中 and an enabled 登记技能 button', async () => {
    const wrapper = mount(SkillsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('人员技能')
    expect(wrapper.text()).toContain('焊接技能')
    const matrixBtn = wrapper.findAll('button').find((b) => b.text().includes('矩阵视图'))
    expect(matrixBtn).toBeTruthy()
    expect(matrixBtn!.text()).toContain('建设中')
    expect(matrixBtn!.attributes('disabled')).toBeDefined()

    const registerBtn = wrapper.findAll('button').find((b) => b.text().includes('登记技能'))
    expect(registerBtn).toBeDefined()
    expect(registerBtn!.attributes('disabled')).toBeUndefined()
  })

  it('blocks skill assignment on empty required fields with summary alert and no assign call', async () => {
    stub.assign.mockClear()
    const wrapper = mount(SkillsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('登记技能'))!.trigger('click')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.assign).not.toHaveBeenCalled()
  })

  it('assigns a skill with worker / code / level and fires success toast', async () => {
    stub.assign.mockClear()
    stub.toastSuccess.mockClear()
    const wrapper = mount(SkillsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('登记技能'))!.trigger('click')
    await flushPromises()
    // WorkerSelect 桩为 <select>（含 usr-1 选项）；等级 Select 桩为 <select>（含 senior 选项）。
    const workerSelect = wrapper.findAll('select').find((s) => s.html().includes('usr-1'))!
    await workerSelect.setValue('usr-1')
    await wrapper.find('#skill-code').setValue('SKILL-WELD')
    const levelSelect = wrapper.findAll('select').find((s) => s.html().includes('senior'))!
    await levelSelect.setValue('senior')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.assign).toHaveBeenCalledTimes(1)
    const body = stub.assign.mock.calls[0]![0] as { userId: string, skillCode: string, level: string }
    expect(body.userId).toBe('usr-1')
    expect(body.skillCode).toBe('SKILL-WELD')
    expect(body.level).toBe('senior')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })
})
