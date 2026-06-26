import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import MbomPage from './mbom.vue'

const stub = vi.hoisted(() => ({
  releaseMbom: vi.fn().mockResolvedValue({ data: {} }),
  fetchMbomDetail: vi.fn(),
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
    fetchMbomDetail: stub.fetchMbomDetail,
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
  DialogRoot: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
}
const sheetStubs = {
  // SheetPro 根 = reka DialogRoot（与对话框共用 DialogRoot stub），内容/标头为真 .vue 按 Pro 名打桩。
  SheetProContent: { template: '<div data-testid="sheet"><slot /></div>' },
  SheetProHeader: { template: '<div><slot /></div>' },
  SheetProTitle: { template: '<h2><slot /></h2>' },
  SheetProDescription: { template: '<p><slot /></p>' },
}
const datePickerStub = {
  DatePickerPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input type="date" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value || null)" />',
  },
}
const formSelectStubs = {
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

const allStubs = { ...layoutStub, ...dialogStubs, ...sheetStubs, ...datePickerStub, ...formSelectStubs }

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.releaseMbom.mockClear()
  stub.fetchMbomDetail.mockReset()
  stub.fetchMbomDetail.mockResolvedValue(undefined)
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

  it('查看物料：list 物料行先显，get-by-id 补齐物料行 + 配方行（移除「待后端」标注）', async () => {
    stub.fetchMbomDetail.mockResolvedValue({
      bomCode: 'MBOM-1',
      revision: 'A',
      skuCode: 'SKU-1',
      status: 'Published',
      materialLines: [
        { skuCode: 'SKU-2', quantity: 2, unitOfMeasureCode: 'PCS', scrapRate: 0.05 },
      ],
      recipeLines: [
        { parameterCode: '温度', targetValue: '180', unitOfMeasureCode: '℃' },
      ],
    })
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看物料')!.trigger('click')
    await flushPromises()

    expect(stub.fetchMbomDetail).toHaveBeenCalledWith('MBOM-1', 'A')
    const sheet = wrapper.find('[data-testid="sheet"]')
    // 物料行显名 + 损耗率
    expect(sheet.text()).toContain('主控板')
    expect(sheet.text()).toContain('5.0%')
    // 配方行真实渲染。
    expect(sheet.text()).toContain('温度')
    expect(sheet.text()).toContain('180')
    expect(sheet.text()).not.toContain('待后端')
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

  it('选物料后单位自动带出其基本单位（SKU-2 → PCS）', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    // 引用EBOM(0)、产出物料(1)、物料行物料(2)、物料行单位(3)
    const selects = wrapper.findAll('select')
    await selects[2]!.setValue('SKU-2') // 选物料
    await flushPromises()

    const uomSelect = wrapper.findAll('select')[3]!
    expect((uomSelect.element as HTMLSelectElement).value).toBe('PCS')
  })

  it('打开向导：生效日默认今天', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const d = new Date()
    const ymd = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    expect((wrapper.findAll('input[type="date"]')[0]!.element as HTMLInputElement).value).toBe(ymd)
  })

  it('自引用拦截：物料等于产出物料时点发布出现专属提示且不发请求', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const selects = wrapper.findAll('select')
    await selects[0]!.setValue('EBOM-1::A')
    await selects[1]!.setValue('SKU-1') // 产出物料
    await wrapper.find('#mbom-rev').setValue('B')
    const after = wrapper.findAll('select')
    await after[2]!.setValue('SKU-1') // 物料 = 产出物料
    await wrapper.findAll('input[type="number"]')[0]!.setValue('1')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('物料不能与产出物料')
    expect(stub.releaseMbom).not.toHaveBeenCalled()
  })
})
