import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import MbomPage from './mbom.vue'

const stub = vi.hoisted(() => ({
  releaseMbom: vi.fn().mockResolvedValue({ data: {} }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const mbomRow = {
  bomCode: 'MBOM-1',
  revision: 'A',
  skuCode: 'SKU-1',
  status: 'Published',
  effectiveDate: '2026-01-01',
  materialLines: [
    { skuCode: 'SKU-2', quantity: 2, unitOfMeasureCode: 'PCS', scrapRate: 0.05 },
  ],
}

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skuCode: undefined as string | undefined, status: undefined as string | undefined, skip: 0, take: 10 })

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringMboms: () => ({
    mboms: computed(() => [mbomRow]),
    mbomsError: shallowRef(undefined),
    mbomsPending: shallowRef(false),
    mbomsTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    releaseMbom: stub.releaseMbom,
    releasePending: shallowRef(false),
    releaseError: shallowRef(undefined),
  }),
  usePublishedEboms: () => ({
    eboms: computed(() => [{ bomCode: 'EBOM-1', revision: 'A', parentItemCode: 'SKU-1', status: 'Published' }]),
    ebomsPending: shallowRef(false),
    ebomsError: shallowRef(undefined),
    refreshEboms: vi.fn(),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    skus: computed(() => [
      { code: 'SKU-1', displayName: '智能网关主机' },
      { code: 'SKU-2', displayName: '主控板' },
    ]),
  }),
  useBusinessUoms: () => ({
    uoms: computed(() => [{ code: 'PCS', displayName: '个' }]),
  }),
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
const sheetStubs = {
  Sheet: { template: '<div><slot /></div>' },
  SheetContent: { template: '<div data-testid="sheet"><slot /></div>' },
  SheetHeader: { template: '<div><slot /></div>' },
  SheetTitle: { template: '<h2><slot /></h2>' },
  SheetDescription: { template: '<p><slot /></p>' },
}
const datePickerStub = {
  DatePicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input type="date" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value || null)" />',
  },
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
}

const allStubs = { ...layoutStub, ...dialogStubs, ...sheetStubs, ...datePickerStub, ...formSelectStubs }

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.releaseMbom.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.skuCode = undefined
  filters.status = undefined
})

describe('engineering mbom page', () => {
  it('渲染标题、MBOM 行（产出物料显名 + 物料行数 + 状态）', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('MBOM 制造BOM')
    expect(wrapper.text()).toContain('智能网关主机')
    expect(wrapper.text()).toContain('MBOM-1')
    expect(wrapper.text()).toContain('已发布')
  })

  it('查看物料：行「查看物料」展开物料行（list 带 MaterialLines），并标注配方待后端', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看物料')!.trigger('click')
    await flushPromises()

    const sheet = wrapper.find('[data-testid="sheet"]')
    // 物料行显名 + 损耗率
    expect(sheet.text()).toContain('主控板')
    expect(sheet.text()).toContain('5.0%')
    expect(sheet.text()).toContain('配方明细暂不在列表回显')
  })

  it('发布向导：选已发布 EBOM + 产出物料 + 一行物料后提交，release 收到 engineeringBomCode/Revision', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const selects = wrapper.findAll('select')
    // 顺序：引用EBOM、产出物料、物料行物料、物料行单位……
    await selects[0]!.setValue('EBOM-1::A')
    await selects[1]!.setValue('SKU-1')
    await wrapper.find('#mbom-rev').setValue('B')
    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    const after = wrapper.findAll('select')
    await after[2]!.setValue('SKU-2') // 物料
    await after[3]!.setValue('PCS') // 单位
    await wrapper.findAll('input[type="number"]')[0]!.setValue('4') // 数量
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseMbom).toHaveBeenCalledTimes(1)
    const body = stub.releaseMbom.mock.calls[0]![0] as Record<string, unknown>
    expect(body.engineeringBomCode).toBe('EBOM-1')
    expect(body.engineeringBomRevision).toBe('A')
    expect(body.skuCode).toBe('SKU-1')
    expect(body.revision).toBe('B')
    const lines = body.materialLines as Array<Record<string, unknown>>
    expect(lines[0]!.skuCode).toBe('SKU-2')
    expect(lines[0]!.quantity).toBe(4)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('EBOM 选择器仅含已发布版本（usePublishedEboms 已过滤 Published）', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const ebomSelect = wrapper.findAll('select')[0]!
    const options = ebomSelect.findAll('option')
    expect(options.length).toBe(1)
    expect(options[0]!.attributes('value')).toBe('EBOM-1::A')
  })

  it('校验拦截：必填未填点发布出现汇总提示且不发请求', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.releaseMbom).not.toHaveBeenCalled()
  })
})
