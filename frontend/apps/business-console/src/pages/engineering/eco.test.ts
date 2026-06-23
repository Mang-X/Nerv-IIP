import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import EcoPage from './eco.vue'

const stub = vi.hoisted(() => ({
  releaseChange: vi.fn().mockResolvedValue({ data: {} }),
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
    fetchChangeDetail: stub.fetchChangeDetail,
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
  stub.releaseChange.mockClear()
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
    await wrapper.find('#eco-approval').setValue('APR-9')
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
