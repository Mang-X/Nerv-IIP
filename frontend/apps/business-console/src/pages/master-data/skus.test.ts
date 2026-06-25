import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SkusPage from './skus.vue'

const stub = vi.hoisted(() => ({
  createSku: vi.fn().mockResolvedValue({ data: { code: 'SKU-NEW' } }),
  update: vi.fn().mockResolvedValue(undefined),
  fetchDetail: vi.fn().mockResolvedValue({
    name: '智能网关主机',
    baseUomCode: 'pcs',
    category: 'electronic',
    materialType: 'finished-goods',
    batchTrackingPolicy: 'none',
    serialTrackingPolicy: 'none',
    shelfLifePolicyCode: 'none',
    storageConditionCode: 'ambient',
    defaultBarcodeRuleCode: 'code128',
    qualityRequired: true,
  }),
  // 记录每个 codeSet 的实时拉取调用，断言"实时拉字典"已接线。
  resourcesCalls: [] as Array<{ resourceType: string, codeSet?: string }>,
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const skuRow = {
  resourceType: 'sku',
  code: 'SKU-1',
  displayName: '智能网关主机',
  active: true,
  snapshotVersion: '2026-06-08T13:01:00',
  category: 'electronic',
  materialType: 'finished-goods',
  baseUomCode: 'pcs',
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    createSku: stub.createSku,
    createSkuPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    refreshSkus: vi.fn(),
    skus: computed(() => [skuRow]),
    skusError: shallowRef(undefined),
    skusPending: shallowRef(false),
    skusTotal: computed(() => 1),
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
  useBusinessMasterDataResources: (resourceType: string, options: { codeSet?: string } = {}) => {
    stub.resourcesCalls.push({ resourceType, codeSet: options.codeSet })
    return { resources: shallowRef([]), resourcesPending: shallowRef(false) }
  },
}))

// 产品分类已升为主数据（#400）：electronic → 电子料。
vi.mock('@/composables/usePromotedCatalogs', () => ({
  useProductCategories: () => ({
    categories: shallowRef([{ categoryCode: 'electronic', categoryName: '电子料', enabled: true }]),
    categoriesPending: shallowRef(false),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
// 对话框就地渲染（不 teleport），便于断言表单内容。
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
  // 行操作里的 AlertDialogPro（reka portal/Teleport 在 jsdom 下卸载会崩）就地渲染，避免渲染崩溃。
  AlertDialogPro: { template: '<div><slot /></div>' },
  AlertDialogProTrigger: { template: '<div><slot /></div>' },
  AlertDialogProContent: { template: '<div><slot /></div>' },
  AlertDialogProHeader: { template: '<div><slot /></div>' },
  AlertDialogProFooter: { template: '<div><slot /></div>' },
  AlertDialogProTitle: { template: '<h2><slot /></h2>' },
  AlertDialogProDescription: { template: '<p><slot /></p>' },
  AlertDialogProCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogProAction: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
  // RowActions 的 Pro 下拉就地渲染，避免 reka DropdownMenu portal 在 jsdom 卸载崩。
  DropdownMenuProContent: { template: '<div><slot /></div>' },
  DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// RowActions 下拉就地渲染，让「编辑」可点。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
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

async function openAndFillValid(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('button').find((b) => b.text().includes('新建物料'))!.trigger('click')
  await flushPromises()
  await wrapper.find('#sku-name').setValue('测试物料')
  // 仅产品分类默认空（其它字段默认值均合法），设为合法码值即可通过校验。
  const categorySelect = wrapper.findAll('select').find((s) => s.findAll('option').some((o) => o.text().includes('电子料')))
  await categorySelect!.setValue('electronic')
  await flushPromises()
}

beforeEach(() => {
  stub.createSku.mockClear()
  stub.update.mockClear()
  stub.fetchDetail.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  stub.resourcesCalls.length = 0
})

describe('master-data skus page', () => {
  it('渲染标题、物料行、新建按钮、补的列与统一时间格式', async () => {
    const wrapper = mount(SkusPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    expect(wrapper.text()).toContain('物料与产品')
    expect(wrapper.text()).toContain('智能网关主机')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建物料'))).toBe(true)
    // 补的列
    for (const header of ['产品分类', '物料类型', '基本单位', '更新时间']) {
      expect(wrapper.text()).toContain(header)
    }
    // 字典映射：electronic → 电子料（实时为空，回退常量）
    expect(wrapper.text()).toContain('电子料')
    // 过长 ISO 时间被格式化成本地「YYYY-MM-DD HH:mm」
    expect(wrapper.text()).toContain('2026-06-08 13:01')
    expect(wrapper.text()).not.toContain('2026-06-08T13:01:00')
  })

  it('每个字典化字段都接了"实时拉字典"（含合规标签）', async () => {
    mount(SkusPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    const codeSets = stub.resourcesCalls.filter((c) => c.resourceType === 'reference-data').map((c) => c.codeSet)
    for (const cs of ['material-type', 'batch-tracking-policy', 'serial-tracking-policy', 'shelf-life-policy', 'storage-condition', 'barcode-rule', 'compliance-tag']) {
      expect(codeSets).toContain(cs)
    }
  })

  it('行「编辑」触发：拉详情回填、对话框进入编辑态、编号只读显示', async () => {
    const wrapper = mount(SkusPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    expect(stub.fetchDetail).toHaveBeenCalledWith('SKU-1')
    expect(wrapper.text()).toContain('编辑物料')
    // 编辑态：物料编号显示真实编码，而非"保存后由系统分配"
    expect(wrapper.text()).toContain('SKU-1')
    expect(wrapper.text()).not.toContain('保存后由系统分配')
  })

  it('必填未填点保存：出现汇总提示且不发创建请求', async () => {
    const wrapper = mount(SkusPage, { global: { stubs: { ...layoutStub, ...dialogStubs } } })
    await flushPromises()

    // 打开新建（重置为默认：产品分类为空 → 非法）
    const createBtn = wrapper.findAll('button').find((b) => b.text().includes('新建物料'))
    await createBtn!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createSku).not.toHaveBeenCalled()
  })

  it('填全必填后提交：调用 createSku 并弹成功 toast', async () => {
    const wrapper = mount(SkusPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createSku).toHaveBeenCalledTimes(1)
    const body = stub.createSku.mock.calls[0]![0] as { name: string, category: string }
    expect(body.name).toBe('测试物料')
    expect(body.category).toBe('electronic')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('提交失败：弹错误 toast（人话）且不重置/不关闭表单', async () => {
    stub.createSku.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(SkusPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createSku).toHaveBeenCalledTimes(1)
    // 失败走 toast.error 的人话映射，不暴露技术串
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 表单未被重置（仍可重试）：名称保留
    expect((wrapper.find('#sku-name').element as HTMLInputElement).value).toBe('测试物料')
  })
})
