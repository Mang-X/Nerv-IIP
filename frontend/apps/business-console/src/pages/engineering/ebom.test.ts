import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import EbomPage from './ebom.vue'

const stub = vi.hoisted(() => ({
  releaseEbom: vi.fn().mockResolvedValue({ data: {} }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const ebomRow = {
  bomCode: 'EBOM-1',
  revision: 'A',
  parentItemCode: 'SKU-1',
  status: 'Published',
  effectiveDate: '2026-01-01',
}

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', parentItemCode: undefined as string | undefined, status: undefined as string | undefined, skip: 0, take: 10 })

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringEboms: () => ({
    eboms: computed(() => [ebomRow]),
    ebomsError: shallowRef(undefined),
    ebomsPending: shallowRef(false),
    ebomsTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    releaseEbom: stub.releaseEbom,
    releasePending: shallowRef(false),
    releaseError: shallowRef(undefined),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    skus: computed(() => [
      { code: 'SKU-1', displayName: '智能网关主机', baseUomCode: 'PCS' },
      { code: 'SKU-2', displayName: '主控板', baseUomCode: 'PCS' },
    ]),
  }),
  useBusinessUoms: () => ({
    uoms: computed(() => [{ code: 'PCS', displayName: '个' }, { code: 'SET', displayName: '套' }]),
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
  stub.releaseEbom.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.parentItemCode = undefined
  filters.status = undefined
})

describe('engineering ebom page', () => {
  it('渲染标题、EBOM 行（父项显名 + 修订 + 状态用 Published）', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('EBOM 设计BOM')
    expect(wrapper.text()).toContain('智能网关主机')
    expect(wrapper.text()).toContain('EBOM-1')
    expect(wrapper.text()).toContain('已发布')
  })

  it('发布向导：填版本头 + 一行组件后提交，release 收到正确 body', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const selects = wrapper.findAll('select')
    // 顺序：父项、组件物料(行)、单位(行)…… 状态筛选在最后。
    await selects[0]!.setValue('SKU-1') // 父项
    await wrapper.find('#ebom-rev').setValue('B')
    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    // 组件行 select：父项之后的两个（组件、单位）
    const formSelects = wrapper.findAll('select')
    await formSelects[1]!.setValue('SKU-2') // 组件
    await formSelects[2]!.setValue('PCS') // 单位
    await wrapper.find('input[type="number"]').setValue('3')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseEbom).toHaveBeenCalledTimes(1)
    const body = stub.releaseEbom.mock.calls[0]![0] as Record<string, unknown>
    expect(body.parentItemCode).toBe('SKU-1')
    expect(body.revision).toBe('B')
    expect(body.effectiveDate).toBe('2026-03-01')
    const lines = body.lines as Array<Record<string, unknown>>
    expect(lines).toHaveLength(1)
    expect(lines[0]!.componentCode).toBe('SKU-2')
    expect(lines[0]!.quantity).toBe(3)
    expect(lines[0]!.unitOfMeasureCode).toBe('PCS')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('校验拦截：版本头未填点发布出现汇总提示且不发请求', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.releaseEbom).not.toHaveBeenCalled()
  })

  it('组件行可增删：增加一行后有两行组件 select 组', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const before = wrapper.findAll('select').length
    await findButton(wrapper, '增加组件')!.trigger('click')
    await flushPromises()
    // 每行新增「组件 + 单位」两个 select。
    expect(wrapper.findAll('select').length).toBe(before + 2)
  })

  it('选组件后单位自动带出其基本单位（SKU-2 → PCS）', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    // 父项(0)、组件(1)、单位(2)
    const formSelects = wrapper.findAll('select')
    await formSelects[1]!.setValue('SKU-2') // 选组件
    await flushPromises()

    const uomSelect = wrapper.findAll('select')[2]!
    expect((uomSelect.element as HTMLSelectElement).value).toBe('PCS')
  })

  it('打开向导：生效日默认今天', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const d = new Date()
    const ymd = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    expect((wrapper.findAll('input[type="date"]')[0]!.element as HTMLInputElement).value).toBe(ymd)
  })

  it('自引用拦截：组件等于父项时点发布出现专属提示且不发请求', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('SKU-1') // 父项
    await wrapper.find('#ebom-rev').setValue('B')
    const formSelects = wrapper.findAll('select')
    await formSelects[1]!.setValue('SKU-1') // 组件 = 父项
    await wrapper.find('input[type="number"]').setValue('1')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('组件不能与父项')
    expect(stub.releaseEbom).not.toHaveBeenCalled()
  })

  it('查看：行「查看」打开版本头并标注「明细待后端」', async () => {
    const wrapper = mount(EbomPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    const sheet = wrapper.find('[data-testid="sheet"]')
    expect(sheet.text()).toContain('明细待后端')
  })
})
