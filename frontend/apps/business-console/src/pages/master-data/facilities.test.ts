import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import FacilitiesPage from './facilities.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({ data: { code: 'PLANT-NEW' } }),
  createWorkshop: vi.fn().mockResolvedValue({ data: { code: 'WS-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

function stubResource(resourceType: string) {
  const rows = resourceType === 'site'
    ? [{ resourceType: 'site', code: 'PLANT-A', displayName: '宁波工厂', active: true, snapshotVersion: '1' }]
    : resourceType === 'production-line'
      ? [{ resourceType: 'production-line', code: 'LINE-A', displayName: '前桥线', active: true, snapshotVersion: '1' }]
      : [{ resourceType: 'work-center', code: 'WC-A', displayName: '焊接中心', active: true, snapshotVersion: '1' }]
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

function stubWorkshops() {
  const rows = [{ resourceType: 'workshop', code: 'WS-A', displayName: '总装车间', active: true, siteCode: 'PLANT-A' }]
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    workshops: computed(() => rows),
    workshopsTotal: computed(() => rows.length),
    workshopsError: shallowRef(undefined),
    workshopsPending: shallowRef(false),
    refreshWorkshops: vi.fn(),
    createWorkshop: stub.createWorkshop,
    createWorkshopError: shallowRef(undefined),
    createWorkshopPending: shallowRef(false),
  }
}

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  // 全字段超集：工厂取 name/timezone，产线取 name/siteCode，工作中心取 name/plantCode/lineCode/日历/产能。
  fetchDetail: vi.fn().mockResolvedValue({
    name: '宁波工厂',
    timezone: 'Asia/Shanghai',
    siteCode: 'PLANT-A',
    plantCode: 'PLANT-A',
    lineCode: 'LINE-A',
    defaultCalendarCode: 'CAL-A',
    capacityMinutesPerDay: 480,
  }),
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

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useBusinessWorkshops: () => stubWorkshops(),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 把 RowActions 的下拉（reka-ui，懒挂载到 body）换成同步渲染插槽的轻量桩，
// 让「编辑」菜单项可直接点击，从而断言行操作触发 @edit 后对话框进入编辑态。
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
const selectStubs = {
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

describe('master-data facilities page', () => {
  it('renders the title, four tabs, sample rows and create buttons', async () => {
    const wrapper = mount(FacilitiesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('工厂与产线')
    expect(wrapper.text()).toContain('工厂 → 车间 → 产线 → 工作中心 → 设备')
    const tabs = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabs.some((t) => t.includes('工厂'))).toBe(true)
    expect(tabs.some((t) => t.includes('车间'))).toBe(true)
    expect(tabs.some((t) => t.includes('产线'))).toBe(true)
    expect(tabs.some((t) => t.includes('工作中心'))).toBe(true)

    expect(wrapper.text()).toContain('宁波工厂')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建工厂'))).toBe(true)
  })

  it('renders real workshop list with create button and per-row actions', async () => {
    const wrapper = mount(FacilitiesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const workshopTab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes('车间'))
    expect(workshopTab).toBeTruthy()
    await workshopTab!.trigger('focus')
    await workshopTab!.trigger('mousedown')
    await flushPromises()

    expect(wrapper.text()).toContain('总装车间')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建车间'))).toBe(true)
    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('exposes per-row actions (detail / rename / disable)', async () => {
    const wrapper = mount(FacilitiesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('opens the site dialog in edit mode (full-field) when a row 编辑 is triggered', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 详情被拉取用于全字段回填。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('PLANT-A')
    // 对话框进入编辑态：标题含「编辑」，编码只读。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑工厂')
    const codeInput = document.getElementById('site-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('工厂主表单：填全必填后提交调用 create 并弹成功 toast', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建工厂'))!.trigger('click')
    await flushPromises()
    // 时区默认 Asia/Shanghai（合法），仅需补编码与名称。
    await wrapper.find('#site-code').setValue('PLANT-NEW')
    await wrapper.find('#site-name').setValue('上海工厂')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as { code: string, name: string, timezone: string }
    expect(body.code).toBe('PLANT-NEW')
    expect(body.name).toBe('上海工厂')
    expect(body.timezone).toBe('Asia/Shanghai')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('工厂主表单：提交失败弹错误 toast（人话）且不重置', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.create.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建工厂'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#site-code').setValue('PLANT-NEW')
    await wrapper.find('#site-name').setValue('上海工厂')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    expect((wrapper.find('#site-name').element as HTMLInputElement).value).toBe('上海工厂')
  })

  it('产线子表单：行「编辑」拉详情、对话框进入编辑态、编码只读', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '产线')

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('LINE-A')
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑产线')
    const codeInput = document.getElementById('line-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('产线子表单：必填留空提交出现汇总提示且不发 create', async () => {
    stub.create.mockClear()
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '产线')

    await wrapper.findAll('button').find((b) => b.text().includes('新建产线'))!.trigger('click')
    await flushPromises()
    // 产线表单（编码/名称/所属工厂为空）→ 非法。就地渲染，从 wrapper 取表单。
    expect(wrapper.find('#line-code').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('工作中心子表单：行「编辑」拉详情、对话框进入编辑态、编码只读', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()
    await switchTab(wrapper, '工作中心')

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('WC-A')
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑工作中心')
    const codeInput = document.getElementById('wc-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('工作中心子表单：必填留空提交出现汇总提示且不发 create', async () => {
    stub.create.mockClear()
    const wrapper = mount(FacilitiesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '工作中心')

    await wrapper.findAll('button').find((b) => b.text().includes('新建工作中心'))!.trigger('click')
    await flushPromises()
    // 工作中心表单（编码/名称/工厂/产线/默认日历为空）→ 非法。就地渲染，从 wrapper 取表单。
    expect(wrapper.find('#wc-code').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })
})
