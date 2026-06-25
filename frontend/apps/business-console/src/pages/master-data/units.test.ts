import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import UnitsPage from './units.vue'

const stub = vi.hoisted(() => ({
  createUom: vi.fn().mockResolvedValue({ data: { code: 'EA' } }),
  update: vi.fn().mockResolvedValue(undefined),
  fetchDetail: vi.fn().mockResolvedValue({
    name: '个',
    dimensionType: 'count',
    precision: 0,
    roundingMode: 'half-up',
  }),
  createUomConversion: vi.fn().mockResolvedValue({ data: { code: 'BOX→EA' } }),
  conversionDisable: vi.fn().mockResolvedValue(undefined),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const uomRow = {
  resourceType: 'unit-of-measure',
  code: 'EA',
  displayName: '个',
  active: true,
  snapshotVersion: '2026-06-08T13:01:00',
  dimensionType: 'count',
  precision: 0,
  roundingMode: 'half-up',
}
const uomRowBox = {
  resourceType: 'unit-of-measure',
  code: 'BOX',
  displayName: '箱',
  active: true,
  snapshotVersion: '2026-06-08T13:02:00',
  dimensionType: 'count',
  precision: 0,
  roundingMode: 'half-up',
}
const conversionRow = {
  resourceType: 'uom-conversion',
  code: 'BOX→EA',
  displayName: 'BOX→EA',
  active: true,
  snapshotVersion: '2026-06-08T13:03:00',
  fromUomCode: 'BOX',
  toUomCode: 'EA',
  factor: 12,
  offset: null,
}

const conversionState = vi.hoisted(() => ({ rows: [] as unknown[] }))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessUoms: () => ({
    createUom: stub.createUom,
    createUomError: shallowRef(undefined),
    createUomPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'unit-of-measure', skip: 0, take: 10 }),
    refreshUoms: vi.fn(),
    uoms: computed(() => [uomRow, uomRowBox]),
    uomsError: shallowRef(undefined),
    uomsPending: shallowRef(false),
    uomsTotal: computed(() => 2),
  }),
  useUomConversions: () => ({
    createUomConversion: stub.createUomConversion,
    createUomConversionError: shallowRef(undefined),
    createUomConversionPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'uom-conversion', skip: 0, take: 10 }),
    refreshConversions: vi.fn(),
    conversions: computed(() => conversionState.rows),
    conversionsError: shallowRef(undefined),
    conversionsPending: shallowRef(false),
    conversionsTotal: computed(() => conversionState.rows.length),
  }),
  useMasterDataResourceActions: (resourceType: string) => ({
    update: stub.update,
    disable: resourceType === 'uom-conversion' ? stub.conversionDisable : vi.fn(),
    enable: vi.fn(),
    fetchDetail: stub.fetchDetail,
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }),
  // 量纲实时拉取：实时为空，页面回退量纲常量。
  useBusinessMasterDataResources: () => ({ resources: shallowRef([]), resourcesPending: shallowRef(false) }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
// RowActions 下拉就地渲染，让「编辑」可点。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 对话框就地渲染（不 teleport），便于断言/填写表单内容。
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
  // 行操作里的 base AlertDialog（reka portal/Teleport 在 jsdom 下卸载会崩）就地渲染，避免渲染崩溃。
  AlertDialog: { template: '<div><slot /></div>' },
  AlertDialogTrigger: { template: '<div><slot /></div>' },
  AlertDialogContent: { template: '<div><slot /></div>' },
  AlertDialogHeader: { template: '<div><slot /></div>' },
  AlertDialogFooter: { template: '<div><slot /></div>' },
  AlertDialogTitle: { template: '<h2><slot /></h2>' },
  AlertDialogDescription: { template: '<p><slot /></p>' },
  AlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogAction: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 停用/启用二次确认弹窗就地渲染（不 teleport），便于点「确认停用」。
const alertDialogStubs = {
  AlertDialog: { template: '<div><slot /></div>' },
  AlertDialogContent: { template: '<div><slot /></div>' },
  AlertDialogHeader: { template: '<div><slot /></div>' },
  AlertDialogFooter: { template: '<div><slot /></div>' },
  AlertDialogTitle: { template: '<h2><slot /></h2>' },
  AlertDialogDescription: { template: '<p><slot /></p>' },
  AlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogAction: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 把 reka-ui Select 换成原生 <select>，让测试能 setValue 完成"填表→提交"。
const selectStubs = {
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
}

// 打开「新建计量单位」并填合法值（名称为文本，量纲/取整为常量回退下拉；编码由系统自动生成）。
async function openAndFillValid(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('button').find((b) => b.text().includes('新建计量单位'))!.trigger('click')
  await flushPromises()
  await wrapper.find('#uom-name').setValue('个')
  await flushPromises()
}

// 切到指定 Tab（reka-ui Tabs 用 focus + mousedown 激活）。
async function switchTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes(label))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}

beforeEach(() => {
  conversionState.rows = [conversionRow]
  stub.createUom.mockClear()
  stub.update.mockClear()
  stub.fetchDetail.mockClear()
  stub.createUomConversion.mockClear()
  stub.conversionDisable.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
})

describe('master-data units page', () => {
  it('渲染标题、单位行、列与新建按钮', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    expect(wrapper.text()).toContain('计量单位')
    expect(wrapper.text()).toContain('个')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建计量单位'))).toBe(true)
    for (const header of ['编码', '名称', '量纲', '更新时间']) {
      expect(wrapper.text()).toContain(header)
    }
    // 量纲映射：count → 计数（实时为空，回退常量）
    expect(wrapper.text()).toContain('计数')
    // 过长 ISO 时间被格式化成本地「YYYY-MM-DD HH:mm」
    expect(wrapper.text()).toContain('2026-06-08 13:01')
  })

  it('填全必填后提交：调用 createUom 并弹成功 toast', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createUom).toHaveBeenCalledTimes(1)
    const body = stub.createUom.mock.calls[0]![0] as { code?: string, name: string, dimensionType: string, roundingMode: string }
    expect(body.code).toBeUndefined()
    expect(body.name).toBe('个')
    expect(body.dimensionType).toBe('count')
    expect(body.roundingMode).toBe('half-up')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('提交失败：弹错误 toast（人话）且不重置表单', async () => {
    stub.createUom.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createUom).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 表单未被重置（仍可重试）：名称保留。
    expect((wrapper.find('#uom-name').element as HTMLInputElement).value).toBe('个')
  })

  it('行「编辑」触发：拉详情回填、对话框进入编辑态、编码只读', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 详情被拉取用于全字段回填。
    expect(stub.fetchDetail).toHaveBeenCalledWith('EA')
    // 对话框进入编辑态：标题含「编辑计量单位」，编码只读。
    expect(wrapper.text()).toContain('编辑计量单位')
    const codeInput = wrapper.find('#uom-code').element as HTMLInputElement
    expect(codeInput.disabled).toBe(true)
  })

  it('必填未填点保存：出现汇总提示且不发创建请求', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    // 打开新建（重置为默认：名称为空 → 非法；编码已由系统自动生成）。
    const createBtn = wrapper.findAll('button').find((b) => b.text().includes('新建计量单位'))
    await createBtn!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createUom).not.toHaveBeenCalled()
  })

  it('换算 Tab：渲染换算列表，源/目标单位显示名称（非编码）', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '换算关系')

    // 公式与源/目标单位显示单位名称（箱/个），不显编码（BOX/EA）。
    expect(wrapper.text()).toContain('1 箱 = 12 个')
    expect(wrapper.text()).toContain('箱')
    expect(wrapper.text()).toContain('个')
    expect(wrapper.text()).not.toContain('BOX→EA')
  })

  it('换算 Tab：填全必填后提交，createUomConversion 收到 body 且弹成功 toast', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '换算关系')

    await wrapper.findAll('button').find((b) => b.text().includes('新建换算关系'))!.trigger('click')
    await flushPromises()
    // 表单内的下拉顺序：源单位 / 目标单位 / 取整方式。
    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('BOX')
    await selects[1]!.setValue('EA')
    await wrapper.find('#conv-factor').setValue('12')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createUomConversion).toHaveBeenCalledTimes(1)
    const body = stub.createUomConversion.mock.calls[0]![0] as {
      fromUomCode: string, toUomCode: string, factor: number, roundingMode: string
    }
    expect(body.fromUomCode).toBe('BOX')
    expect(body.toUomCode).toBe('EA')
    expect(body.factor).toBe(12)
    expect(body.roundingMode).toBe('half-up')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('换算 Tab：源=目标单位时校验拦截，不发创建请求', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '换算关系')

    await wrapper.findAll('button').find((b) => b.text().includes('新建换算关系'))!.trigger('click')
    await flushPromises()
    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('EA')
    await selects[1]!.setValue('EA')
    await wrapper.find('#conv-factor').setValue('2')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('源单位与目标单位不能相同')
    expect(stub.createUomConversion).not.toHaveBeenCalled()
  })

  it('换算 Tab：factor≤0 时校验拦截，不发创建请求', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchTab(wrapper, '换算关系')

    await wrapper.findAll('button').find((b) => b.text().includes('新建换算关系'))!.trigger('click')
    await flushPromises()
    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('BOX')
    await selects[1]!.setValue('EA')
    await wrapper.find('#conv-factor').setValue('0')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createUomConversion).not.toHaveBeenCalled()
  })

  it('换算 Tab：行「停用」二次确认后调用换算的 disable', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs: { ...layoutStub, ...rowActionStubs, ...alertDialogStubs } } })
    await flushPromises()
    await switchTab(wrapper, '换算关系')

    const disableItem = wrapper.findAll('button').find((b) => b.text().trim() === '停用')
    expect(disableItem).toBeTruthy()
    await disableItem!.trigger('click')
    await flushPromises()

    const confirmBtn = wrapper.findAll('button').find((b) => b.text().includes('确认停用'))
    expect(confirmBtn).toBeTruthy()
    await confirmBtn!.trigger('click')
    await flushPromises()

    expect(stub.conversionDisable).toHaveBeenCalledWith('BOX→EA')
  })

  it('换算 Tab：无换算时显示「去新建」空态', async () => {
    conversionState.rows = []
    const wrapper = mount(UnitsPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await switchTab(wrapper, '换算关系')

    expect(wrapper.text()).toContain('还没有换算关系')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建换算关系'))).toBe(true)
  })
})
