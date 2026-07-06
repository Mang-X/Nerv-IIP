import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DevicesPage from './devices.vue'

const stub = vi.hoisted(() => ({
  create: vi.fn().mockResolvedValue({ data: { code: 'EQ-NEW' } }),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const actionStub = vi.hoisted(() => ({
  update: vi.fn(),
  fetchDetail: vi.fn().mockResolvedValue({
    model: 'KR-210',
    manufacturer: 'KUKA',
    serialNo: 'SN-9001',
    assetClassCode: 'ROBOT',
    siteCode: 'PLANT-A',
    workshopCode: 'WS-A',
    lineCode: 'LINE-A',
    workCenterCode: 'WC-A',
    stationCode: 'ST-01',
    purchaseDate: '2025-01-15',
    purchaseCost: 125000,
    purchaseCurrencyCode: 'CNY',
    warrantyExpiresOn: '2027-01-14',
    supplierPartnerCode: 'SUP-ACME',
    parentDeviceId: 'EQ-PARENT',
    components: [{ componentCode: 'MOTOR', componentName: '伺服电机', quantity: 1, critical: true }],
    criticality: 'high',
    maintainable: true,
  }),
}))

function stubResource(resourceType: string) {
  const rows = resourceType === 'device-asset'
    ? [{
        resourceType: 'device-asset',
        code: 'EQ-01',
        displayName: '焊接机器人',
        active: true,
        siteCode: 'PLANT-A',
        workshopCode: 'WS-A',
        lineCode: 'LINE-A',
        workCenterCode: 'WC-A',
        stationCode: 'ST-01',
        purchaseDate: '2025-01-15',
        purchaseCost: 125000,
        purchaseCurrencyCode: 'CNY',
        warrantyExpiresOn: '2027-01-14',
        supplierPartnerCode: 'SUP-ACME',
        parentDeviceId: 'EQ-PARENT',
        snapshotVersion: '1',
      }]
    : resourceType === 'site'
      ? [{ resourceType: 'site', code: 'PLANT-A', displayName: '宁波工厂', active: true, snapshotVersion: '1' }]
    : resourceType === 'production-line'
      ? [{ resourceType: 'production-line', code: 'LINE-A', displayName: '前桥线', active: true, siteCode: 'PLANT-A', workshopCode: 'WS-A', snapshotVersion: '1' }]
      : resourceType === 'work-center'
        ? [{ resourceType: 'work-center', code: 'WC-A', displayName: '焊接中心', active: true, lineCode: 'LINE-A', snapshotVersion: '1' }]
        : []
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    items: computed(() => rows),
    total: computed(() => rows.length),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    refresh: vi.fn(),
    create: stub.create,
    createError: shallowRef(undefined),
    createPending: shallowRef(false),
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

function stubWorkshops() {
  const rows = [{ resourceType: 'workshop', code: 'WS-A', displayName: '总装车间', active: true, siteCode: 'PLANT-A' }]
  return {
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    workshops: computed(() => rows),
    workshopsTotal: computed(() => rows.length),
    workshopsError: shallowRef(undefined),
    workshopsPending: shallowRef(false),
    refreshWorkshops: vi.fn(),
    createWorkshop: vi.fn(),
    createWorkshopError: shallowRef(undefined),
    createWorkshopPending: shallowRef(false),
  }
}

vi.mock('@/composables/useBusinessMasterData', () => ({
  useMasterDataResource: (resourceType: string) => stubResource(resourceType),
  useMasterDataResourceActions: () => stubActions(),
  useBusinessWorkshops: () => stubWorkshops(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

// 把 RowActions 的下拉（reka-ui，懒挂载到 body）换成同步渲染插槽的轻量桩，
// 让「编辑」菜单项可直接点击，从而断言行操作触发 @edit 后对话框进入编辑态。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  // RowActions 内的下拉项已迁到 Pro（DropdownMenuProItem 是真 .vue 包装，stub 按 Pro 名）。
  DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
}
// 对话框就地渲染（不 teleport），便于断言/填写表单内容。
const dialogStubs = {
  // DialogPro/DialogProTrigger 是 reka-ui 原语再导出，组件名仍是 DialogRoot/DialogTrigger。
  DialogPro: { template: '<div><slot /></div>' },
  DialogRoot: { template: '<div><slot /></div>' },
  DialogProTrigger: { template: '<div><slot /></div>' },
  DialogTrigger: { template: '<div><slot /></div>' },
  DialogProContent: { template: '<div><slot /></div>' },
  DialogProHeader: { template: '<div><slot /></div>' },
  DialogProFooter: { template: '<div><slot /></div>' },
  DialogProTitle: { template: '<h2><slot /></h2>' },
  DialogProDescription: { template: '<p><slot /></p>' },
  // 行操作里 RowActions 的下拉内容已迁到 Pro（DropdownMenuProContent 含 reka portal/Teleport，
  // jsdom 卸载会崩）就地渲染，避免渲染崩溃。
  DropdownMenuProContent: { template: '<div><slot /></div>' },
  DropdownMenuProItem: { emits: ['click'], template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>' },
  // 行操作里的 AlertDialog 已迁到 Pro（AlertDialogProContent 含 reka portal/Teleport，jsdom 卸载会崩）就地渲染。
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

// 打开「新建设备」并把默认空的必填项填成合法值（型号/厂商/SN/资产类为文本，产线/工作中心为 Select）。
async function openAndFillValid(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('button').find((b) => b.text().includes('新建设备'))!.trigger('click')
  await flushPromises()
  // 新建态不再有编码输入框（编码由系统自动生成）。
  await wrapper.find('#dev-model').setValue('KR-210')
  await wrapper.find('#dev-maker').setValue('KUKA')
  await wrapper.find('#dev-serial').setValue('SN-9001')
  await wrapper.find('#dev-class').setValue('ROBOT')
  const siteSelect = wrapper.findAll('select').find((s) => s.findAll('option').some((o) => o.text().includes('宁波工厂')))
  await siteSelect!.setValue('PLANT-A')
  const workshopSelect = wrapper.findAll('select').find((s) => s.findAll('option').some((o) => o.text().includes('总装车间')))
  await workshopSelect!.setValue('WS-A')
  const lineSelect = wrapper.findAll('select').find((s) => s.findAll('option').some((o) => o.text().includes('前桥线')))
  await lineSelect!.setValue('LINE-A')
  const wcSelect = wrapper.findAll('select').find((s) => s.findAll('option').some((o) => o.text().includes('焊接中心')))
  await wcSelect!.setValue('WC-A')
  await wrapper.find('#dev-station').setValue('ST-01')
  await wrapper.find('#dev-purchase-date').setValue('2025-01-15')
  await wrapper.find('#dev-purchase-cost').setValue('125000')
  await wrapper.find('#dev-warranty').setValue('2027-01-14')
  await wrapper.find('#dev-supplier').setValue('SUP-ACME')
  await wrapper.find('#dev-parent').setValue('EQ-PARENT')
  await wrapper.find('#dev-component-code-0').setValue('MOTOR')
  await wrapper.find('#dev-component-name-0').setValue('伺服电机')
  await flushPromises()
}

describe('master-data devices page', () => {
  it('renders the title, sample row and create button', async () => {
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    expect(wrapper.text()).toContain('设备台账')
    expect(wrapper.text()).toContain('焊接机器人')
    expect(wrapper.findAll('button').some((b) => b.text().includes('新建设备'))).toBe(true)
  })

  it('exposes per-row actions (detail / rename / disable)', async () => {
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    const triggers = wrapper.findAll('button').filter((b) => b.attributes('aria-label')?.includes('操作'))
    expect(triggers.length).toBeGreaterThan(0)
  })

  it('opens the device dialog in edit mode (full-field) when a row 编辑 is triggered', async () => {
    actionStub.fetchDetail.mockClear()
    const wrapper = mount(DevicesPage, { global: { stubs: { ...layoutStub, ...rowActionStubs } } })
    await flushPromises()

    const editItem = wrapper.findAll('button').find((b) => b.text().trim() === '编辑')
    expect(editItem).toBeTruthy()
    await editItem!.trigger('click')
    await flushPromises()

    // 详情被拉取用于全字段回填。
    expect(actionStub.fetchDetail).toHaveBeenCalledWith('EQ-01')
    // 对话框进入编辑态：标题含「编辑设备」，编码只读。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑设备')
    const codeInput = document.getElementById('dev-code') as HTMLInputElement | null
    expect(codeInput?.disabled).toBe(true)
  })

  it('blocks create on empty required fields with a summary alert and no create call', async () => {
    stub.create.mockClear()
    const wrapper = mount(DevicesPage, { global: { stubs: layoutStub } })
    await flushPromises()

    // 打开「新建设备」对话框（重置为默认，必填项留空 → 非法）。
    const createBtn = wrapper.findAll('button').find((b) => b.text().includes('新建设备'))
    await createBtn!.trigger('click')
    await flushPromises()

    // 对话框 teleport 到 body，从 body 取就地表单触发提交。
    const form = document.body.querySelector('form')
    expect(form).toBeTruthy()
    form!.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(document.body.textContent).toContain('请完整填写带 * 的必填项')
    expect(stub.create).not.toHaveBeenCalled()
  })

  it('填全必填后提交：调用 create（含产线/工作中心）并弹成功 toast', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(DevicesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as {
      code?: string
      model: string
      siteCode: string
      workshopCode: string
      lineCode: string
      workCenterCode: string
      stationCode: string
      purchaseDate?: string
      purchaseCost?: number
      warrantyExpiresOn?: string
      supplierPartnerCode?: string
      parentDeviceId?: string
      components?: Array<{ componentCode?: string, componentName?: string, quantity?: number }>
    }
    expect(body.code).toBeUndefined()
    expect(body.model).toBe('KR-210')
    expect(body.siteCode).toBe('PLANT-A')
    expect(body.workshopCode).toBe('WS-A')
    expect(body.lineCode).toBe('LINE-A')
    expect(body.workCenterCode).toBe('WC-A')
    expect(body.stationCode).toBe('ST-01')
    expect(body.purchaseDate).toBe('2025-01-15')
    expect(body.purchaseCost).toBe(125000)
    expect(body.warrantyExpiresOn).toBe('2027-01-14')
    expect(body.supplierPartnerCode).toBe('SUP-ACME')
    expect(body.parentDeviceId).toBe('EQ-PARENT')
    expect(body.components).toEqual([{ componentCode: 'MOTOR', componentName: '伺服电机', quantity: 1, critical: false }])
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('提交失败：弹错误 toast（人话）且不重置表单', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.create.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(DevicesPage, { global: { stubs: { ...layoutStub, ...dialogStubs, ...selectStubs } } })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 表单未被重置（仍可重试）：型号保留。
    expect((wrapper.find('#dev-model').element as HTMLInputElement).value).toBe('KR-210')
  })
})
