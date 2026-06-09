import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SkusPage from './skus.vue'

const stub = vi.hoisted(() => ({
  createSku: vi.fn().mockResolvedValue({ data: { code: 'SKU-NEW' } }),
  update: vi.fn().mockResolvedValue(undefined),
  fetchDetail: vi.fn().mockResolvedValue({
    name: '智能网关主机',
    baseUomCode: 'PCS',
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
  baseUomCode: 'PCS',
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

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
// 对话框就地渲染（不 teleport），便于断言表单内容。
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}
// RowActions 下拉就地渲染，让「编辑」可点。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  DropdownMenuItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
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
    for (const cs of ['product-category', 'material-type', 'batch-tracking-policy', 'serial-tracking-policy', 'shelf-life-policy', 'storage-condition', 'barcode-rule', 'compliance-tag']) {
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
})
