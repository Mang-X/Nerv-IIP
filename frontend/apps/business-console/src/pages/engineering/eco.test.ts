import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import EcoPage from './eco.vue'

const stub = vi.hoisted(() => ({
  releaseChange: vi.fn().mockResolvedValue({ data: {} }),
  previewImpact: vi.fn(),
  fetchChangeDetail: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const changeRow = {
  changeNumber: 'ECO-1',
  reason: '更换供应商物料',
  approvalReferenceId: 'APR-1',
  status: 'Released',
  effectiveDate: '2026-02-01',
  affectedVersions: [{ versionKind: 'EngineeringBom', versionId: 'VER-1' }],
}

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', status: undefined as string | undefined, skip: 0, take: 10 })
const impactPreview = shallowRef()

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringChanges: () => ({
    changes: computed(() => [changeRow]),
    changesError: shallowRef(undefined),
    changesPending: shallowRef(false),
    changesTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    releaseChange: stub.releaseChange,
    releasePending: shallowRef(false),
    releaseError: shallowRef(undefined),
    previewImpact: stub.previewImpact,
    previewPending: shallowRef(false),
    previewError: shallowRef(undefined),
    impactPreview,
    clearImpactPreview: () => {
      impactPreview.value = undefined
    },
    fetchChangeDetail: stub.fetchChangeDetail,
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
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
}
const sheetStubs = {
  // NvSheet 根 = reka DialogRoot（与对话框共用 DialogRoot stub），内容/标头为真 .vue 按 Pro 名打桩。
  NvSheetContent: { template: '<div data-testid="sheet"><slot /></div>' },
  NvSheetHeader: { template: '<div><slot /></div>' },
  NvSheetTitle: { template: '<h2><slot /></h2>' },
  NvSheetDescription: { template: '<p><slot /></p>' },
}
const datePickerStub = {
  NvDatePicker: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input type="date" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value || null)" />',
  },
}
const formSelectStubs = {
  NvSelect: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  NvSelectTrigger: { template: '<span><slot /></span>' },
  SelectValue: { template: '<span />' },
  NvSelectContent: { template: '<slot />' },
  NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}
const routerLinkStubs = {
  RouterLink: { props: ['to'], template: '<a data-router-link><slot /></a>' },
}

const approvalPanelStub = {
  BusinessDocumentApprovalPanel: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<section data-testid="approval-panel"><button type="button" @click="$emit(\'update:modelValue\', \'APR-9\')">关联审批链</button><span>{{ modelValue }}</span></section>',
  },
}

const allStubs = { ...layoutStub, ...dialogStubs, ...sheetStubs, ...datePickerStub, ...formSelectStubs, ...routerLinkStubs, ...approvalPanelStub }

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.releaseChange.mockClear()
  stub.previewImpact.mockReset()
  stub.previewImpact.mockImplementation(async () => {
    impactPreview.value = {
      effectiveDate: '2026-03-01',
      nodes: [
        {
          nodeType: 'manufacturing-bom',
          versionId: 'MBOM-1:B',
          displayName: 'MBOM MBOM-1 / B',
          impactLevel: 'derived',
          skuCode: 'SKU-FG',
          consoleRoute: '/engineering/mbom?bomCode=MBOM-1&revision=B',
        },
        {
          nodeType: 'mrp-candidate',
          versionId: 'mrp:pv-1',
          displayName: 'MRP 候选',
          impactLevel: 'candidate',
          skuCode: 'SKU-FG',
          consoleRoute: null,
        },
        {
          nodeType: 'aps-plan-candidate',
          versionId: 'aps:pv-1',
          displayName: '已发布计划候选',
          impactLevel: 'candidate',
          skuCode: 'SKU-FG',
          consoleRoute: '/scheduling',
        },
      ],
      risks: [
        {
          code: 'downstream-execution-impact',
          severity: 'warning',
          message: '变更会影响 MRP、MES、APS 和在制执行候选。',
          relatedVersionId: 'MBOM-1:B',
        },
      ],
    }
    return impactPreview.value
  })
  impactPreview.value = undefined
  stub.fetchChangeDetail.mockReset()
  stub.fetchChangeDetail.mockResolvedValue(undefined)
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.status = undefined
})

describe('engineering eco page', () => {
  it('渲染标题与变更行（变更号/原因/状态已发布）', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('工程变更')
    expect(wrapper.text()).toContain('ECO-1')
    expect(wrapper.text()).toContain('更换供应商物料')
    expect(wrapper.text()).toContain('已发布')
  })

  it('状态筛选只暴露「已发布」，不假造草稿/待审状态', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()
    const text = wrapper.text()
    expect(text).not.toContain('草稿')
    expect(text).not.toContain('待审')
  })

  it('发布向导：填变更信息 + 一条受影响版本提交，release 收到正确 body', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    await wrapper.find('#eco-reason').setValue('工艺优化')
    await findButton(wrapper, '关联审批链')!.trigger('click')
    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    // 受影响版本：第一个 select 是对象种类
    await wrapper.findAll('select')[0]!.setValue('Routing')
    await wrapper.find('#eco-vid-0').setValue('ROUTING-VER-1')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseChange).toHaveBeenCalledTimes(1)
    const body = stub.releaseChange.mock.calls[0]![0] as Record<string, unknown>
    expect(body.reason).toBe('工艺优化')
    expect(body.approvalReferenceId).toBe('APR-9')
    expect(body.effectiveDate).toBe('2026-03-01')
    const affected = body.affectedVersions as Array<Record<string, unknown>>
    expect(affected).toHaveLength(1)
    expect(affected[0]!.versionKind).toBe('Routing')
    expect(affected[0]!.versionId).toBe('ROUTING-VER-1')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('发布前可预览工程变更影响链且不触发发布', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    await wrapper.findAll('select')[0]!.setValue('EngineeringBom')
    await wrapper.find('#eco-vid-0').setValue('EBOM-FG:A')
    await flushPromises()

    await findButton(wrapper, '预览影响')!.trigger('click')
    await flushPromises()

    expect(stub.previewImpact).toHaveBeenCalledWith(expect.objectContaining({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      effectiveDate: '2026-03-01',
      affectedVersions: [{ versionKind: 'EngineeringBom', versionId: 'EBOM-FG:A' }],
    }))
    expect(stub.releaseChange).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('MBOM MBOM-1 / B')
    expect(wrapper.text()).toContain('MRP 候选')
    expect(wrapper.text()).toContain('APS 排程')
    expect(wrapper.text()).toContain('暂无入口')
    expect(wrapper.find('[data-router-link]').exists()).toBe(true)
    expect(wrapper.text()).toContain('变更会影响 MRP、MES、APS 和在制执行候选')
  })

  it('校验拦截：变更信息未填点发布出现汇总提示且不发请求', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.releaseChange).not.toHaveBeenCalled()
  })

  it('受影响版本可增删：增加一行后多一组 select+input', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    const before = wrapper.findAll('select').length
    await findButton(wrapper, '增加一条')!.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('select').length).toBe(before + 1)
  })

  it('查看：行「查看」拉 get-by-id 渲染受影响版本', async () => {
    stub.fetchChangeDetail.mockResolvedValue({
      changeNumber: 'ECO-1',
      reason: '更换供应商物料',
      status: 'Released',
      affectedVersions: [
        { versionKind: 'ManufacturingBom', versionId: 'MBOM-VER-9' },
      ],
    })
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    expect(stub.fetchChangeDetail).toHaveBeenCalledWith('ECO-1')
    const sheet = wrapper.find('[data-testid="sheet"]')
    expect(sheet.text()).toContain('制造 BOM')
    expect(sheet.text()).toContain('MBOM-VER-9')
  })
})
