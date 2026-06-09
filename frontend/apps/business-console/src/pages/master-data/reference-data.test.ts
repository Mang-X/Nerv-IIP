import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ReferenceDataPage from './reference-data.vue'

const stub = vi.hoisted(() => ({
  createCode: vi.fn().mockResolvedValue({ data: { code: 'cold-chain' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

// 列表桩数据带 codeSet 分组字段。
const codeRows = [
  { resourceType: 'reference-data', codeSet: 'material-type', code: 'raw-material', displayName: '原材料', active: true },
  { resourceType: 'reference-data', codeSet: 'material-type', code: 'finished-goods', displayName: '成品', active: false },
]

function stubCodes() {
  return {
    codes: computed(() => codeRows),
    codesError: shallowRef(undefined),
    codesPending: shallowRef(false),
    codesTotal: computed(() => codeRows.length),
    createCode: stub.createCode,
    createCodeError: shallowRef(undefined),
    createCodePending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'reference-data', skip: 0, take: 10, codeSet: 'material-type' }),
    refreshCodes: vi.fn(),
  }
}

function stubActions() {
  return {
    update: vi.fn(),
    disable: vi.fn(),
    enable: vi.fn(),
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useReferenceDataCodes: () => stubCodes(),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 对话框就地渲染（不 teleport），便于填写表单。
const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogHeader: { template: '<div><slot /></div>' },
  DialogFooter: { template: '<div><slot /></div>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
  DialogDescription: { template: '<p><slot /></p>' },
}
// 把 reka-ui Select 换成原生 <select>，让测试能 setValue（这里所属字典已由切分组带入合法值）。
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

// 先切到可维护分组（仓储条件，platform-preset），开新建对话框并填编码/名称（所属字典已带入）。
async function switchAndFillValid(wrapper: ReturnType<typeof mount>) {
  const storageTab = wrapper.find('nav[aria-label="字典分组"]').findAll('button').find((b) => b.text().includes('仓储条件'))!
  await storageTab.trigger('click')
  await flushPromises()
  await wrapper.findAll('button').find((b) => b.text().includes('新建字典条目'))!.trigger('click')
  await flushPromises()
  await wrapper.find('#ref-code').setValue('cold-chain')
  await wrapper.find('#ref-name').setValue('冷链')
  await flushPromises()
}

describe('master-data reference-data page', () => {
  it('renders the title and a CodeSet master list', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('数据字典')
    const nav = wrapper.find('nav[aria-label="字典分组"]')
    expect(nav.exists()).toBe(true)
    expect(nav.text()).toContain('物料类型')
    expect(nav.text()).toContain('仓储条件')
  })

  it('renders the selected CodeSet entries on the right', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('原材料')
    expect(wrapper.text()).toContain('成品')
  })

  it('switches the selected CodeSet when a left item is clicked', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const button = wrapper.find('nav[aria-label="字典分组"]').findAll('button').find((b) => b.text().includes('仓储条件'))!
    await button.trigger('click')
    await flushPromises()

    expect(button.attributes('aria-pressed')).toBe('true')
  })

  it('opens the create dialog with a CodeSet select', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 默认 CodeSet material-type 为系统枚举（不可新增），先切到可维护分组再开新建对话框。
    const storageTab = wrapper.find('nav[aria-label="字典分组"]').findAll('button').find((b) => b.text().includes('仓储条件'))!
    await storageTab.trigger('click')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建字典条目'))!.trigger('click')
    await flushPromises()

    const body = document.body.textContent ?? ''
    expect(body).toContain('新建字典条目')
    expect(body).toContain('所属字典')
  })

  it('exposes per-row actions', async () => {
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('blocks create on empty required fields with a summary alert and no create call', async () => {
    stub.createCode.mockClear()
    const wrapper = mount(ReferenceDataPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 默认 material-type 为系统枚举不可新增，先切到可维护分组（仓储条件）再开新建对话框。
    const storageTab = wrapper.find('nav[aria-label="字典分组"]').findAll('button').find((b) => b.text().includes('仓储条件'))!
    await storageTab.trigger('click')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建字典条目'))!.trigger('click')
    await flushPromises()

    // 对话框 teleport 到 body；编码/名称留空 → 提交触发汇总提示。
    const form = document.body.querySelector('form')
    expect(form).toBeTruthy()
    form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('请完整填写带 * 的必填项')
    expect(stub.createCode).not.toHaveBeenCalled()
  })

  it('填全必填后提交：调用 createCode（含所属字典）并弹成功 toast', async () => {
    stub.createCode.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(ReferenceDataPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createCode).toHaveBeenCalledTimes(1)
    const body = stub.createCode.mock.calls[0]![0] as { codeSet: string, code: string, name: string }
    expect(body.codeSet).toBe('storage-condition')
    expect(body.code).toBe('cold-chain')
    expect(body.name).toBe('冷链')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('提交失败：弹错误 toast（人话）且不重置表单', async () => {
    stub.createCode.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.createCode.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(ReferenceDataPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await switchAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createCode).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 表单未被重置：名称保留。
    expect((wrapper.find('#ref-name').element as HTMLInputElement).value).toBe('冷链')
  })
})
