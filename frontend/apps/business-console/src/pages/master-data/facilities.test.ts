import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import FacilitiesPage from './facilities.vue'

const stub = vi.hoisted(() => ({
  createSite: vi.fn().mockResolvedValue({ data: { code: 'PLANT-NEW' } }),
  createWorkshop: vi.fn().mockResolvedValue({ data: { code: 'WS-NEW' } }),
  createLine: vi.fn().mockResolvedValue({ data: { code: 'LINE-NEW' } }),
  createWorkCenter: vi.fn().mockResolvedValue({ data: { code: 'WC-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

// 两个工厂 → 树不压扁根，工厂节点可见、可就地建子级。
// 关系靠 typed code 链路：workshop.siteCode / line.workshopCode / workCenter.lineCode。
const SITE_ROWS = [
  { resourceType: 'site', code: 'PLANT-A', displayName: '宁波工厂', active: true, snapshotVersion: '1' },
  { resourceType: 'site', code: 'PLANT-B', displayName: '上海工厂', active: true, snapshotVersion: '1' },
]
const WORKSHOP_ROWS = [
  { resourceType: 'workshop', code: 'WS-A', displayName: '总装车间', active: true, siteCode: 'PLANT-A' },
]
const LINE_ROWS = [
  { resourceType: 'production-line', code: 'LINE-A', displayName: '前桥线', active: true, siteCode: 'PLANT-A', workshopCode: 'WS-A' },
]
const WC_ROWS = [
  { resourceType: 'work-center', code: 'WC-A', displayName: '焊接中心', active: true, plantCode: 'PLANT-A', lineCode: 'LINE-A', capacityMinutesPerDay: 480 },
]

const CREATE_BY_TYPE: Record<string, ReturnType<typeof vi.fn>> = {
  'site': stub.createSite,
  'production-line': stub.createLine,
  'work-center': stub.createWorkCenter,
}

function stubResource(resourceType: string) {
  const rows = resourceType === 'site'
    ? SITE_ROWS
    : resourceType === 'production-line'
      ? LINE_ROWS
      : WC_ROWS
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

function stubWorkshops() {
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 200 }),
    workshops: computed(() => WORKSHOP_ROWS),
    workshopsTotal: computed(() => WORKSHOP_ROWS.length),
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
  fetchDetail: vi.fn().mockResolvedValue({
    name: '宁波工厂',
    timezone: 'Asia/Shanghai',
    siteCode: 'PLANT-A',
    plantCode: 'PLANT-A',
    lineCode: 'LINE-A',
    workshopCode: 'WS-A',
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

// RouterLink 桩：避免依赖真实 router；把 to（对象）序列化进 data-to 便于断言。
const routerLinkStub = {
  RouterLink: {
    props: ['to'],
    computed: { serialized() { return JSON.stringify((this as unknown as { to: unknown }).to) } },
    template: '<a :data-to="serialized"><slot /></a>',
  },
}

// 对话框就地渲染（不 teleport），便于断言/填写表单内容。
const dialogStubs = {
  Dialog: { props: ['open'], template: '<div v-if="open"><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}

// 把 reka-ui Select 换成原生 <select>，让测试能 setValue（归属改挂下拉）。
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

// 找到树里某节点的「选中」按钮（按文本）。
function findNodeButton(wrapper: ReturnType<typeof mount>, label: string) {
  return wrapper.findAll('button').find((b) => b.text().includes(label) && !b.attributes('aria-label')?.includes('新建'))
}

const mountOpts = { global: { stubs: { ...layoutStub, ...dialogStubs, ...routerLinkStub } } }

describe('master-data facilities tree page', () => {
  it('renders title and tree nodes for all four levels', async () => {
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    expect(wrapper.text()).toContain('工厂结构')
    // 两工厂 → 根可见；车间/产线/工作中心节点（少节点默认展开）。
    expect(wrapper.text()).toContain('宁波工厂')
    expect(wrapper.text()).toContain('上海工厂')
    expect(wrapper.text()).toContain('总装车间')
    expect(wrapper.text()).toContain('前桥线')
    expect(wrapper.text()).toContain('焊接中心')
  })

  it('selecting a node shows its detail in the right panel with breadcrumb path', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    await findNodeButton(wrapper, '焊接中心')!.trigger('click')
    await flushPromises()

    // 详情拉取 + 字段展示。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('WC-A')
    expect(wrapper.text()).toContain('工作中心编码')
    expect(wrapper.text()).toContain('默认工作日历')
    // 面包屑路径含工厂/车间/产线/工作中心。
    const nav = wrapper.find('[aria-label="选中路径"]')
    expect(nav.exists()).toBe(true)
    expect(nav.text()).toContain('宁波工厂')
    expect(nav.text()).toContain('前桥线')
    expect(nav.text()).toContain('焊接中心')
  })

  it('work-center node offers a device drill-down link with workCenterCode query', async () => {
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    await findNodeButton(wrapper, '焊接中心')!.trigger('click')
    await flushPromises()

    const link = wrapper.findAll('a').find((a) => a.text().includes('查看该工作中心下设备'))
    expect(link).toBeTruthy()
    const to = link!.attributes('data-to')
    // RouterLink 桩把对象 to 序列化进 data-to；断言含目标 path 与 workCenterCode query。
    expect(String(to)).toContain('/master-data/devices')
    expect(String(to)).toContain('workCenterCode')
    expect(String(to)).toContain('WC-A')
  })

  it('「新建子级」prefills parent code read-only (site → workshop)', async () => {
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    // 选中工厂节点 → 右侧出现「新建车间」。
    await findNodeButton(wrapper, '宁波工厂')!.trigger('click')
    await flushPromises()

    const createChildBtn = wrapper.findAll('button').find((b) => b.text().includes('新建车间'))
    expect(createChildBtn).toBeTruthy()
    await createChildBtn!.trigger('click')
    await flushPromises()

    // 对话框：标题「新建车间」，父归属（所属工厂）预填 PLANT-A 且只读。
    expect(wrapper.text()).toContain('新建车间')
    const siteInput = wrapper.find('#create-site').element as HTMLInputElement
    expect(siteInput.value).toBe('PLANT-A')
    expect(siteInput.disabled).toBe(true)
  })

  it('creating a workshop posts with prefilled siteCode and fires success toast', async () => {
    stub.createWorkshop.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    await findNodeButton(wrapper, '宁波工厂')!.trigger('click')
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().includes('新建车间'))!.trigger('click')
    await flushPromises()

    // 新建态不再有编码输入框（编码由系统自动生成）。
    expect(wrapper.find('#create-code').exists()).toBe(false)
    await wrapper.find('#create-name').setValue('涂装车间')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createWorkshop).toHaveBeenCalledTimes(1)
    const body = stub.createWorkshop.mock.calls[0]![0] as { code?: string, name: string, siteCode: string }
    expect(body.code).toBeUndefined()
    expect(body.name).toBe('涂装车间')
    expect(body.siteCode).toBe('PLANT-A')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('新建根工厂：填全必填后提交调用 create 并弹成功 toast', async () => {
    stub.createSite.mockClear()
    stub.toastSuccess.mockClear()
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建工厂'))!.trigger('click')
    await flushPromises()

    // 新建态不再有编码输入框（编码由系统自动生成）。
    expect(wrapper.find('#create-code').exists()).toBe(false)
    await wrapper.find('#create-name').setValue('广州工厂')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createSite).toHaveBeenCalledTimes(1)
    const body = stub.createSite.mock.calls[0]![0] as { code?: string, name: string, timezone: string }
    expect(body.code).toBeUndefined()
    expect(body.name).toBe('广州工厂')
    expect(body.timezone).toBe('Asia/Shanghai')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('新建必填留空提交：出现汇总提示且不发 create', async () => {
    stub.createSite.mockClear()
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建工厂'))!.trigger('click')
    await flushPromises()
    // 名称留空 → 非法（编码已由系统自动生成，不再校验）。
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createSite).not.toHaveBeenCalled()
  })

  it('提交失败弹错误 toast（人话）且对话框保持打开', async () => {
    stub.createSite.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.createSite.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(FacilitiesPage, mountOpts)
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建工厂'))!.trigger('click')
    await flushPromises()
    await wrapper.find('#create-name').setValue('广州工厂')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createSite).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 对话框仍开、输入保留。
    expect((wrapper.find('#create-name').element as HTMLInputElement).value).toBe('广州工厂')
  })

  const editStubs = {
    RowActions: { template: '<div><slot /></div>' },
    DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
  }

  it('编辑工厂：编码只读、可改名 / 时区（工厂无归属，不显示归属选择）', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(FacilitiesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...routerLinkStub, ...formSelectStubs, ...editStubs } },
    })
    await flushPromises()

    await findNodeButton(wrapper, '宁波工厂')!.trigger('click')
    await flushPromises()
    actionStub.fetchDetail.mockClear()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(actionStub.fetchDetail).toHaveBeenCalledWith('PLANT-A')
    expect(wrapper.text()).toContain('编辑工厂')
    const codeInput = wrapper.find('#edit-code').element as HTMLInputElement
    expect(codeInput.disabled).toBe(true)
    // 改挂提示已删除；工厂无上级，不渲染归属选择器。
    expect(wrapper.text()).not.toContain('归属（上级）创建后不可更改')
    expect(wrapper.find('#edit-site').exists()).toBe(false)
  })

  it('编辑车间：可改挂工厂（update 收到新 siteCode），编码只读', async () => {
    actionStub.update.mockClear()
    const wrapper = mount(FacilitiesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...routerLinkStub, ...formSelectStubs, ...editStubs } },
    })
    await flushPromises()

    await findNodeButton(wrapper, '总装车间')!.trigger('click')
    await flushPromises()
    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    await editItem!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('编辑车间')
    expect((wrapper.find('#edit-code').element as HTMLInputElement).disabled).toBe(true)
    // 改挂工厂：PLANT-A → PLANT-B。
    const siteSelect = wrapper.findAll('select').find((s) => s.html().includes('PLANT-B'))!
    await siteSelect.setValue('PLANT-B')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(actionStub.update).toHaveBeenCalledWith('WS-A', expect.objectContaining({ siteCode: 'PLANT-B' }))
  })

  it('编辑工作中心：可改挂工厂 / 产线（update 收到新 plantCode + lineCode）', async () => {
    actionStub.update.mockClear()
    const wrapper = mount(FacilitiesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...routerLinkStub, ...formSelectStubs, ...editStubs } },
    })
    await flushPromises()

    await findNodeButton(wrapper, '焊接中心')!.trigger('click')
    await flushPromises()
    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    await editItem!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('编辑工作中心')
    // 归属字段就位（工厂 / 产线选择器）。
    expect(wrapper.find('#edit-wc-plant').exists()).toBe(true)
    expect(wrapper.find('#edit-wc-line').exists()).toBe(true)
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    // 不改归属直接提交：透传当前归属（plantCode/lineCode）给 update。
    expect(actionStub.update).toHaveBeenCalledWith(
      'WC-A',
      expect.objectContaining({ plantCode: 'PLANT-A', lineCode: 'LINE-A' }),
    )
  })

  it('编辑产线改挂工厂后清空不匹配的车间（防归属错配）', async () => {
    actionStub.update.mockClear()
    const wrapper = mount(FacilitiesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...routerLinkStub, ...formSelectStubs, ...editStubs } },
    })
    await flushPromises()

    await findNodeButton(wrapper, '前桥线')!.trigger('click')
    await flushPromises()
    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    await editItem!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('编辑产线')
    // 初始：siteCode=PLANT-A、workshopCode=WS-A（属 PLANT-A）。改挂到 PLANT-B：
    // WS-A 不属 PLANT-B → 车间应被级联清空，避免归属错配。
    const siteSelect = wrapper.find('#edit-line-site').exists()
      ? wrapper.findAll('select').find((s) => s.html().includes('PLANT-B') && s.html().includes('PLANT-A'))!
      : wrapper.findAll('select')[0]!
    await siteSelect.setValue('PLANT-B')
    await flushPromises()
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(actionStub.update).toHaveBeenCalledWith(
      'LINE-A',
      expect.objectContaining({ siteCode: 'PLANT-B', workshopCode: null }),
    )
  })
})
