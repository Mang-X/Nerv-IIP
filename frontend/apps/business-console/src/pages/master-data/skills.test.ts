import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SkillsPage from './skills.vue'

const stub = vi.hoisted(() => ({
  assign: vi.fn().mockResolvedValue({}),
  matrixRefresh: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
  // 可被各用例覆写的矩阵数据源（默认含一名工人 + 一项技能）。
  skillCodes: ['SKILL-A'] as string[],
  rows: [
    {
      userId: 'usr-1',
      skills: [{ skillCode: 'SKILL-A', level: 'senior', effectiveTo: undefined as string | undefined }],
    },
  ] as Array<{ userId: string, skills: Array<{ skillCode: string, level: string, effectiveTo?: string }> }>,
}))

// 技能目录字典（reference-data, codeSet=skill）：SKILL-A → 焊接技能、SKILL-WELD → 电焊；工人解析另走 useBusinessWorkers。
function stubReadonlyResource(resourceType: string) {
  const rows = resourceType === 'reference-data'
    ? [
        { resourceType, code: 'SKILL-A', displayName: '焊接技能', active: true, snapshotVersion: '1' },
        { resourceType, code: 'SKILL-WELD', displayName: '电焊', active: true, snapshotVersion: '1' },
      ]
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

function stubSkillMatrix() {
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', userId: undefined, skillCode: undefined, includeDisabled: undefined }),
    refresh: stub.matrixRefresh,
    skillCodes: computed(() => stub.skillCodes),
    rows: computed(() => stub.rows),
    matrixError: shallowRef(undefined),
    matrixPending: shallowRef(false),
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
  useBusinessWorkers: () => stubWorkers(),
  usePersonnelSkillMatrix: () => stubSkillMatrix(),
  usePersonnelSkillAssignment: () => stubSkillAssignment(),
}))

// 技能目录主数据（#402）：SKILL-A → 焊接技能、SKILL-WELD → 电焊。
vi.mock('@/composables/usePromotedCatalogs', () => ({
  useSkillCatalog: () => ({
    skills: computed(() => [
      { skillCode: 'SKILL-A', skillName: '焊接技能', enabled: true },
      { skillCode: 'SKILL-WELD', skillName: '电焊', enabled: true },
    ]),
    skillsPending: shallowRef(false),
    refresh: vi.fn(),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  DialogPro: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  DialogProTrigger: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
}
const formSelectStubs = {
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectProValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  WorkerSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value="usr-1">张三</option></select>',
  },
}

function resetMatrix() {
  stub.skillCodes = ['SKILL-A']
  stub.rows = [{ userId: 'usr-1', skills: [{ skillCode: 'SKILL-A', level: 'senior', effectiveTo: undefined }] }]
}

describe('master-data skills page (matrix)', () => {
  it('renders the matrix with worker name, skill 中文名 column header and level 中文 cell', async () => {
    resetMatrix()
    const wrapper = mount(SkillsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('人员技能')
    // userId → 工人姓名（不暴露裸 usr-1）。
    expect(wrapper.text()).toContain('张三')
    expect(wrapper.text()).not.toContain('usr-1')
    // skillCode → 技能中文名（列头）。
    expect(wrapper.text()).toContain('焊接技能')
    expect(wrapper.text()).not.toContain('SKILL-A')
    // level → 中文。
    expect(wrapper.text()).toContain('高级')
    // 已删除「建设中」文案。
    expect(wrapper.text()).not.toContain('建设中')
  })

  it('highlights soon-expiring cells with a 临期 badge and token class', async () => {
    resetMatrix()
    const soon = new Date(Date.now() + 10 * 86_400_000).toISOString()
    stub.rows = [{ userId: 'usr-1', skills: [{ skillCode: 'SKILL-A', level: 'senior', effectiveTo: soon }] }]
    const wrapper = mount(SkillsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('临期')
    expect(wrapper.html()).toContain('text-warning')
  })

  it('highlights past-expired cells with 已过期 badge and destructive token', async () => {
    resetMatrix()
    const past = new Date(Date.now() - 5 * 86_400_000).toISOString()
    stub.rows = [{ userId: 'usr-1', skills: [{ skillCode: 'SKILL-A', level: 'expert', effectiveTo: past }] }]
    const wrapper = mount(SkillsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('已过期')
    expect(wrapper.html()).toContain('text-destructive')
  })

  it('shows a 去登记 empty state when there is no dimension data', async () => {
    stub.skillCodes = []
    stub.rows = []
    const wrapper = mount(SkillsPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('还没有任何技能登记')
    const goBtn = wrapper.findAll('button').find((b) => b.text().includes('去登记技能'))
    expect(goBtn).toBeTruthy()
  })

  it('blocks assignment on empty required fields with summary alert and no assign call', async () => {
    resetMatrix()
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

  it('assigns a skill and refreshes the matrix on success', async () => {
    resetMatrix()
    stub.assign.mockClear()
    stub.matrixRefresh.mockClear()
    stub.toastSuccess.mockClear()
    const wrapper = mount(SkillsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...formSelectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('登记技能'))!.trigger('click')
    await flushPromises()
    const workerSelect = wrapper.findAll('select').find((s) => s.html().includes('usr-1'))!
    await workerSelect.setValue('usr-1')
    const skillSelect = wrapper.findAll('select').find((s) => s.html().includes('SKILL-WELD'))!
    await skillSelect.setValue('SKILL-WELD')
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
    // 登记成功后矩阵 refresh。
    expect(stub.matrixRefresh).toHaveBeenCalled()
  })
})
