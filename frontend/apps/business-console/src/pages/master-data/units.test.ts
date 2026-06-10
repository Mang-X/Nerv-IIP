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

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessUoms: () => ({
    createUom: stub.createUom,
    createUomError: shallowRef(undefined),
    createUomPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'unit-of-measure', skip: 0, take: 10 }),
    refreshUoms: vi.fn(),
    uoms: computed(() => [uomRow]),
    uomsError: shallowRef(undefined),
    uomsPending: shallowRef(false),
    uomsTotal: computed(() => 1),
  }),
  useMasterDataResourceActions: () => ({
    update: stub.update,
    disable: vi.fn(),
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
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}
// 把 reka-ui Select 换成原生 <select>，让测试能 setValue 完成"填表→提交"。
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

// 打开「新建计量单位」并填合法值（编码/名称为文本，量纲/取整为常量回退下拉）。
async function openAndFillValid(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('button').find((b) => b.text().includes('新建计量单位'))!.trigger('click')
  await flushPromises()
  await wrapper.find('#uom-code').setValue('EA')
  await wrapper.find('#uom-name').setValue('个')
  await flushPromises()
}

beforeEach(() => {
  stub.createUom.mockClear()
  stub.update.mockClear()
  stub.fetchDetail.mockClear()
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
    const body = stub.createUom.mock.calls[0]![0] as { code: string, name: string, dimensionType: string, roundingMode: string }
    expect(body.code).toBe('EA')
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

    // 打开新建（重置为默认：编码/名称为空 → 非法）。
    const createBtn = wrapper.findAll('button').find((b) => b.text().includes('新建计量单位'))
    await createBtn!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createUom).not.toHaveBeenCalled()
  })
})
