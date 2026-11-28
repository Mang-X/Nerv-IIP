import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import MbomPage from './mbom.vue'

const stub = vi.hoisted(() => ({
  releaseMbom: vi.fn().mockResolvedValue({ data: {} }),
  fetchMbomDetail: vi.fn(),
  fetchEbomDetail: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const mbomRow = {
  bomCode: 'MBOM-1',
  revision: 'A',
  skuCode: 'SKU-1',
  status: 'Published',
  effectiveDate: '2026-01-01',
  materialLines: [{ skuCode: 'SKU-2', quantity: 2, unitOfMeasureCode: 'PCS', scrapRate: 0.05 }],
}

const ebomA = { bomCode: 'EBOM-1', revision: 'A', parentItemCode: 'SKU-1', status: 'Published' }
const sources = shallowRef([ebomA])
const sourceDetail = {
  ...ebomA,
  lines: [{ childItemCode: 'SKU-2', quantity: 2, unitOfMeasureCode: 'PCS' }],
}

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  skuCode: undefined as string | undefined,
  status: undefined as string | undefined,
  skip: 0,
  take: 10,
})

vi.mock('@/composables/engineering', () => ({
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
    eboms: sources,
    fetchEbomDetail: stub.fetchEbomDetail,
    ebomsPending: shallowRef(false),
    ebomsError: shallowRef(undefined),
    refreshEboms: vi.fn(),
  }),
  // 修订号输入框旁新增「已占用修订」建议（读面走 colada，需要 pinia），测试给空建议。
  useBomRevisionSuggestions: () => ({
    takenRevisions: computed(() => []),
    takenRevisionsPending: shallowRef(false),
    isTaken: () => false,
  }),
}))

vi.mock('@/composables/master-data', () => ({
  useBusinessSkus: () => ({
    skus: computed(() => [
      { code: 'SKU-1', displayName: '智能网关主机', baseUomCode: 'PCS' },
      { code: 'SKU-2', displayName: '主控板', baseUomCode: 'PCS' },
    ]),
  }),
  useBusinessUoms: () => ({
    uoms: computed(() => [
      { code: 'PCS', displayName: '个' },
      { code: 'SET', displayName: '套' },
    ]),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }
const dialogStubs = {
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
}
const sheetStubs = {
  // NvSheet 根与 NvDialog 同为 reka DialogRoot，但 barrel 已给两个别名各自的 name，
  // 因而这里能分开打桩（过去只能共用一个 DialogRoot 键）。
  NvSheet: { template: '<div><slot /></div>' },
  NvSheetTrigger: { template: '<div><slot /></div>' },
  NvSheetContent: { template: '<div data-testid="sheet"><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
}
const datePickerStub = {
  NvDatePicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input type="date" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value || null)" />',
  },
}
const formSelectStubs = {
  NvEntityPicker: {
    props: ['modelValue', 'options', 'disabled'],
    emits: ['update:modelValue'],
    template: `<select :value="modelValue" :disabled="disabled" @change="$emit('update:modelValue', $event.target.value)">
      <option value=""></option>
      <option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option>
    </select>`,
  },

  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  NvSelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
  // 目录选择器（取数、就地新增由 DirectoryPicker 自己的用例覆盖）换成原生 <select>，下标顺序不变。
  DirectoryPicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><option value=""></option><option value="SKU-1">SKU-1</option><option value="SKU-2">SKU-2</option></select>',
  },
}

const allStubs = {
  ...layoutStub,
  ...dialogStubs,
  ...sheetStubs,
  ...datePickerStub,
  ...formSelectStubs,
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  sources.value = [ebomA]
  stub.fetchEbomDetail.mockReset().mockResolvedValue(sourceDetail)
  stub.releaseMbom.mockReset().mockResolvedValue({ data: {} })
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
      materialLines: [{ skuCode: 'SKU-2', quantity: 2, unitOfMeasureCode: 'PCS', scrapRate: 0.05 }],
      recipeLines: [{ parameterCode: '温度', targetValue: '180', unitOfMeasureCode: '℃' }],
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

    expect(stub.fetchEbomDetail).toHaveBeenCalledWith('EBOM-1', 'A')
    expect(wrapper.get('#mbom-sku').attributes('readonly')).toBeDefined()
    expect(wrapper.text()).toContain('已带入 1 行')
    await wrapper.get('#mbom-rev').setValue('B')
    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    expect((wrapper.get('#mbom-qty-0').element as HTMLInputElement).value).toBe('2')

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
    expect(lines[0]).toMatchObject({ quantity: 2, unitOfMeasureCode: 'PCS' })
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('EBOM 选择器仅含已发布版本（usePublishedEboms 已过滤 Published）', async () => {
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const ebomSelect = wrapper.get('#mbom-ebom')
    const options = ebomSelect.findAll('option')
    expect(options.length).toBe(2)
    expect(options[1]!.attributes('value')).toBe('EBOM-1::A')
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

    await wrapper.get('#mbom-mat-0').setValue('SKU-1')
    await wrapper.get('#mbom-mat-0').setValue('SKU-2')
    await flushPromises()

    const uomSelect = wrapper.findAll('select')[2]!
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

    await wrapper.get('#mbom-rev').setValue('B')
    await wrapper.get('#mbom-mat-0').setValue('SKU-1')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('物料不能与产出物料')
    expect(stub.releaseMbom).not.toHaveBeenCalled()
  })
  it('切换来源先保护修改，确认后只替换物料行并保留配方', async () => {
    const ebomB = { ...ebomA, revision: 'B' }
    sources.value = [ebomA, ebomB]
    stub.fetchEbomDetail.mockResolvedValueOnce(sourceDetail).mockResolvedValueOnce({
      ...ebomB,
      lines: [{ childItemCode: 'SKU-2', quantity: 6, unitOfMeasureCode: 'SET' }],
    })
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await wrapper.get('#mbom-ebom').setValue('EBOM-1::A')
    await flushPromises()
    await wrapper.get('#mbom-qty-0').setValue('7')
    await findButton(wrapper, '增加配方')!.trigger('click')
    await wrapper.get('#mbom-param-0').setValue('温度')
    await wrapper.get('#mbom-ebom').setValue('EBOM-1::B')
    expect(stub.fetchEbomDetail).toHaveBeenCalledTimes(1)
    await findButton(wrapper, '保留当前来源')!.trigger('click')
    expect((wrapper.get('#mbom-qty-0').element as HTMLInputElement).value).toBe('7')
    await wrapper.get('#mbom-ebom').setValue('EBOM-1::B')
    await findButton(wrapper, '更换来源')!.trigger('click')
    await flushPromises()
    expect(stub.fetchEbomDetail).toHaveBeenLastCalledWith('EBOM-1', 'B')
    expect((wrapper.get('#mbom-qty-0').element as HTMLInputElement).value).toBe('6')
    expect((wrapper.get('#mbom-param-0').element as HTMLInputElement).value).toBe('温度')
  })

  it('来源晚到不覆盖新版本，失败可重试当前精确修订', async () => {
    const ebomB = { ...ebomA, revision: 'B' }
    sources.value = [ebomA, ebomB]
    let resolveA!: (value: typeof sourceDetail) => void
    stub.fetchEbomDetail
      .mockImplementationOnce(
        () =>
          new Promise((resolve) => {
            resolveA = resolve
          }),
      )
      .mockRejectedValueOnce(new Error('读取失败'))
      .mockResolvedValueOnce({ ...sourceDetail, revision: 'B', parentItemCode: 'SKU-2', lines: [] })
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await wrapper.get('#mbom-ebom').setValue('EBOM-1::A')
    await wrapper.get('#mbom-ebom').setValue('EBOM-1::B')
    await flushPromises()
    await findButton(wrapper, '重试读取来源')!.trigger('click')
    await flushPromises()
    resolveA(sourceDetail)
    await flushPromises()
    expect(stub.fetchEbomDetail).toHaveBeenLastCalledWith('EBOM-1', 'B')
    expect(wrapper.text()).toContain('EBOM-1 · 修订 B，已带入 0 行')
    expect((wrapper.get('#mbom-sku').element as HTMLInputElement).value).toContain('主控板')
  })
  it('发布失败保留来源、物料行和配方输入，修正后可重试', async () => {
    stub.releaseMbom.mockRejectedValueOnce(new Error('暂时无法发布'))
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()
    await wrapper.get('#mbom-rev').setValue('B')
    await wrapper.get('#mbom-qty-0').setValue('8')
    await findButton(wrapper, '增加配方')!.trigger('click')
    await wrapper.get('#mbom-param-0').setValue('温度')
    await wrapper.get('#mbom-target-0').setValue('180')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(stub.toastError).toHaveBeenCalled()
    expect((wrapper.get('#mbom-ebom').element as HTMLSelectElement).value).toBe('EBOM-1::A')
    expect((wrapper.get('#mbom-qty-0').element as HTMLInputElement).value).toBe('8')
    expect((wrapper.get('#mbom-target-0').element as HTMLInputElement).value).toBe('180')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(stub.releaseMbom).toHaveBeenCalledTimes(2)
  })
  it('单位目录尚无对应项时仍显示来源物料行的单位', async () => {
    stub.fetchEbomDetail.mockResolvedValueOnce({
      ...sourceDetail,
      lines: [{ childItemCode: 'SKU-2', quantity: 3, unitOfMeasureCode: 'BOX' }],
    })
    const wrapper = mount(MbomPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()
    const unit = wrapper.findAll('select')[2]!
    expect((unit.element as HTMLSelectElement).value).toBe('BOX')
    expect(unit.get('option[value="BOX"]').text()).toBe('BOX')
  })
})
