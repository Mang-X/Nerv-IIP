import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import PartnersPage from './partners.vue'

const stub = vi.hoisted(() => ({
  createPartner: vi.fn().mockResolvedValue({ data: { code: 'P-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({
    name: '广汽集团',
    partnerType: 'customer',
    partnerRoles: ['carrier'],
    taxId: '91440000MA5R',
  }),
}))

// 列表桩数据带真实 typed 角色字段（partnerType 主角色 + partnerRoles 附加角色）。
const partnerRows = [
  { resourceType: 'business-partner', code: 'P-001', displayName: '广汽集团', active: true, partnerType: 'customer', partnerRoles: ['carrier'], taxId: '91440000MA5R' },
  { resourceType: 'business-partner', code: 'P-002', displayName: '中石化润滑油', active: true, partnerType: 'supplier' },
]

function stubPartners() {
  return {
    createPartner: stub.createPartner,
    createPartnerError: shallowRef(undefined),
    createPartnerPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', resourceType: 'business-partner', skip: 0, take: 10 }),
    partners: computed(() => partnerRows),
    partnersError: shallowRef(undefined),
    partnersPending: shallowRef(false),
    partnersTotal: computed(() => partnerRows.length),
    refreshPartners: vi.fn(),
  }
}

function stubActions() {
  return {
    update: actionStub.update,
    disable: vi.fn(),
    enable: vi.fn(),
    fetchDetail: actionStub.fetchDetail,
    updatePending: shallowRef(false),
    disablePending: shallowRef(false),
    enablePending: shallowRef(false),
    actionError: shallowRef(undefined),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessPartners: () => stubPartners(),
  useMasterDataResourceActions: () => stubActions(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 把 RowActions 的下拉换成同步渲染插槽的轻量桩，让「编辑」菜单项可直接点击。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  // RowActions 内的下拉项已迁到 Pro（DropdownMenuProItem 是真 .vue 包装，stub 按 Pro 名）。
  DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 行操作里 RowActions 的下拉内容 + 二次确认弹窗已迁到 Pro（DropdownMenuProContent / AlertDialogProContent
// 含 reka portal/Teleport，jsdom 下卸载会崩）就地渲染，避免渲染崩溃。
const alertDialogStubs = {
  DropdownMenuProContent: { template: '<div><slot /></div>' },
  DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
  AlertDialogPro: { template: '<div><slot /></div>' },
  AlertDialogProTrigger: { template: '<div><slot /></div>' },
  AlertDialogProContent: { template: '<div><slot /></div>' },
  AlertDialogProHeader: { template: '<div><slot /></div>' },
  AlertDialogProFooter: { template: '<div><slot /></div>' },
  AlertDialogProTitle: { template: '<h2><slot /></h2>' },
  AlertDialogProDescription: { template: '<p><slot /></p>' },
  AlertDialogProCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogProAction: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 对话框就地渲染（不 teleport），便于填写表单。
const dialogStubs = {
  // DialogPro/DialogProTrigger/DialogProClose 是 reka-ui 原语再导出，组件名仍是 DialogRoot/DialogTrigger/DialogClose。
  DialogPro: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  DialogProTrigger: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
}
// 把 reka-ui Select 换成原生 <select>，让测试能 setValue。
const selectStubs = {
  SelectPro: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
  },
  SelectProTrigger: { template: '<span><slot /></span>' },
  // SelectProValue 是 reka-ui SelectValue 再导出，组件名仍是 SelectValue。
  SelectProValue: { template: '<span />' },
  SelectValue: { template: '<span />' },
  SelectProContent: { template: '<slot />' },
  SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
}

// 打开「新建伙伴」并填好默认空的必填项（名称；主角色默认 customer 合法；编码由系统自动生成）。
async function openAndFillValid(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('button').find((b) => b.text().includes('新建伙伴'))!.trigger('click')
  await flushPromises()
  await wrapper.find('#partner-name').setValue('新伙伴公司')
  await flushPromises()
}

describe('master-data partners page', () => {
  it('renders the title, real role labels (not code-guessed) and counts', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('业务伙伴')
    expect(wrapper.text()).toContain('广汽集团')
    // 主角色 customer -> 客户，附加角色 carrier -> 承运商；中石化为供应商。
    expect(wrapper.text()).toContain('客户')
    expect(wrapper.text()).toContain('承运商')
    expect(wrapper.text()).toContain('供应商')
  })

  it('exposes a role filter select and a create-partner button', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.find('[aria-label="伙伴角色"]').exists()).toBe(true)
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建伙伴'))).toBe(true)
  })

  it('opens the create dialog with an explicit primary-role select and extra-role checkboxes', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().includes('新建伙伴'))!.trigger('click')
    await flushPromises()

    const body = document.body.textContent ?? ''
    expect(body).toContain('新建业务伙伴')
    expect(body).toContain('主角色')
    expect(body).toContain('附加角色')
  })

  it('exposes per-row actions', async () => {
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('opens the partner dialog in edit mode (full-field) when a row 编辑 is triggered', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(PartnersPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 详情被拉取用于全字段回填（第一行编码 P-001）。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('P-001')
    // 对话框进入编辑态：标题含「编辑业务伙伴」，编码只读。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑业务伙伴')
    const codeInput = document.getElementById('partner-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('blocks create on empty required fields with a summary alert and no create call', async () => {
    stub.createPartner.mockClear()
    const wrapper = mount(PartnersPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 打开「新建伙伴」对话框（重置后 name 为空 → 非法；编码已由系统自动生成）。
    await wrapper.findAll('button').find((b) => b.text().includes('新建伙伴'))!.trigger('click')
    await flushPromises()

    // 对话框 teleport 到 body，从 body 取就地表单触发提交。
    const form = document.body.querySelector('form')
    expect(form).toBeTruthy()
    form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('请完整填写带 * 的必填项')
    expect(stub.createPartner).not.toHaveBeenCalled()
  })

  it('填全必填后提交：调用 createPartner（含主角色）并弹成功 toast', async () => {
    stub.createPartner.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(PartnersPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, ...alertDialogStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createPartner).toHaveBeenCalledTimes(1)
    const body = stub.createPartner.mock.calls[0]![0] as { code?: string, name: string, partnerType: string }
    expect(body.code).toBeUndefined()
    expect(body.name).toBe('新伙伴公司')
    expect(body.partnerType).toBe('customer')
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('提交失败：弹错误 toast（人话）且不重置表单', async () => {
    stub.createPartner.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.createPartner.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(PartnersPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs, ...alertDialogStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createPartner).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 表单未被重置：名称保留。
    expect((wrapper.find('#partner-name').element as HTMLInputElement).value).toBe('新伙伴公司')
  })
})
