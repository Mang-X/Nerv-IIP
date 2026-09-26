import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import DeviceFormDialog from '@/components/masterData/DeviceFormDialog.vue'
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
    components: [
      { componentCode: 'MOTOR', componentName: '伺服电机', quantity: 1, critical: true },
    ],
    criticality: 'high',
    maintainable: true,
  }),
}))

function stubResource(resourceType: string) {
  const rows =
    resourceType === 'device-asset'
      ? [
          {
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
          },
        ]
      : resourceType === 'site'
        ? [
            {
              resourceType: 'site',
              code: 'PLANT-A',
              displayName: '宁波工厂',
              active: true,
              snapshotVersion: '1',
            },
          ]
        : resourceType === 'production-line'
          ? [
              {
                resourceType: 'production-line',
                code: 'LINE-A',
                displayName: '前桥线',
                active: true,
                siteCode: 'PLANT-A',
                workshopCode: 'WS-A',
                snapshotVersion: '1',
              },
            ]
          : resourceType === 'work-center'
            ? [
                {
                  resourceType: 'work-center',
                  code: 'WC-A',
                  displayName: '焊接中心',
                  active: true,
                  lineCode: 'LINE-A',
                  snapshotVersion: '1',
                },
              ]
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
  const rows = [
    {
      resourceType: 'workshop',
      code: 'WS-A',
      displayName: '总装车间',
      active: true,
      siteCode: 'PLANT-A',
    },
  ]
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
  useCreateMasterDataResource: () => ({
    create: stub.create,
    error: shallowRef(undefined),
    pending: shallowRef(false),
  }),
  // 设备类别改成取 `asset-class` 数据字典、供应商改成取业务伙伴目录（都不再手输编码）；
  // 设备弹窗另取工厂（唯一时缺省选中）与设备台账（父设备候选）。
  useBusinessMasterDataResources: (resourceType: string) => {
    const rows =
      resourceType === 'reference-data'
        ? [{ resourceType: 'reference-data', code: 'CNC', displayName: '数控机床', active: true }]
        : stubResource(resourceType).items.value
    return {
      filters: reactive({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 200,
      }),
      resources: computed(() => rows),
      resourcesTotal: computed(() => rows.length),
      resourcesError: shallowRef(undefined),
      resourcesPending: shallowRef(false),
      refreshResources: vi.fn(),
    }
  },
  useBusinessPartners: () => ({
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 200 }),
    partners: computed(() => [
      {
        resourceType: 'business-partner',
        code: 'SUP-001',
        displayName: '第一供应商',
        active: true,
        partnerType: 'supplier',
      },
    ]),
    partnersTotal: computed(() => 1),
    partnersError: shallowRef(undefined),
    partnersPending: shallowRef(false),
    refreshPartners: vi.fn(),
  }),
}))

vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => ({ organizationId: 'org-001', environmentId: 'env-dev' }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const layoutStub = {
  BusinessLayout: { template: '<main><slot /></main>' },
  // 层级字段的目录选择器（取数、就地新增由 DirectoryPicker 自己的用例覆盖）桩成带同名 id 的输入位，
  // `data-parent` 记下调用方给的上级收窄条件，`data-create-context` 记下交给新增弹窗的上下文。
  DirectoryPicker: {
    props: ['modelValue', 'id', 'parent', 'createContext'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" :data-parent="JSON.stringify(parent ?? null)" :data-create-context="JSON.stringify(createContext ?? null)" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}

// 把 RowActions 的下拉（reka-ui，懒挂载到 body）换成同步渲染插槽的轻量桩，
// 让「编辑」菜单项可直接点击，从而断言行操作触发 @edit 后对话框进入编辑态。
const rowActionStubs = {
  RowActions: { template: '<div><slot /></div>' },
  // RowActions 内的下拉项已迁到 Pro（NvDropdownMenuItem 是真 .vue 包装，stub 按 Pro 名）。
  NvDropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
}
// 对话框就地渲染（不 teleport），便于断言/填写表单内容。
const dialogStubs = {
  // NvDialog/NvDialogTrigger 是 reka-ui 原语的带 name 浅拷贝别名（barrel 已补 name），
  // 组件名即 Nv 名，故 stub 键按 Nv 名写。
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  // 行操作里 RowActions 的下拉内容已迁到 Pro（NvDropdownMenuContent 含 reka portal/Teleport，
  // jsdom 卸载会崩）就地渲染，避免渲染崩溃。
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  NvDropdownMenuItem: {
    emits: ['click'],
    template: '<button type="button" @click="$emit(\'click\', $event)"><slot /></button>',
  },
  // 行操作里的 AlertDialog 已迁到 Pro（NvAlertDialogContent 含 reka portal/Teleport，jsdom 卸载会崩）就地渲染。
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogTrigger: { template: '<div><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
}
// 设备类别/供应商/父设备已从自由文本改成只选控件（内部自带 reka Dialog，会撞上上面的
// NvDialog 桩）。桩成带同名 id 的输入位，用例继续用 `#dev-*` 表达「选中了某个候选」。
const pickerStubs = {
  NvEntityPicker: {
    props: ['modelValue', 'options', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
  NvSearchSelect: {
    props: ['modelValue', 'options', 'id'],
    emits: ['update:modelValue'],
    template:
      '<input :id="id" :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  },
}
// 把 reka-ui Select 换成原生 <select>，让测试能 setValue 完成"填表→提交"。
const selectStubs = {
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
}

// 打开「新建设备」并把默认空的必填项填成合法值。
async function openAndFillValid(wrapper: ReturnType<typeof mount>) {
  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('新建设备'))!
    .trigger('click')
  await flushPromises()
  await fillValid(wrapper)
}

async function fillValid(wrapper: ReturnType<typeof mount>) {
  // 新建态不再有编码输入框（编码由系统自动生成）。
  await wrapper.find('#dev-model').setValue('KR-210')
  await wrapper.find('#dev-maker').setValue('KUKA')
  await wrapper.find('#dev-serial').setValue('SN-9001')
  await wrapper.find('#dev-class').setValue('ROBOT')
  await wrapper.find('#dev-site').setValue('PLANT-A')
  await wrapper.find('#dev-workshop').setValue('WS-A')
  await wrapper.find('#dev-line').setValue('LINE-A')
  await wrapper.find('#dev-wc').setValue('WC-A')
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

    const triggers = wrapper
      .findAll('button')
      .filter((b) => b.attributes('aria-label')?.includes('操作'))
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
    // 对话框进入编辑态：标题含「编辑设备」，编码走只读上下文区（非 disabled 输入框）。
    const body = document.body.textContent ?? ''
    expect(body).toContain('编辑设备')
    expect(document.getElementById('dev-code')).toBeNull()
    const carried = document.body.querySelector('[data-slot="carried-context"]')
    expect(carried?.textContent).toContain('EQ-01')
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

    expect(stub.create).not.toHaveBeenCalled()
  })

  it('填全必填后提交：调用 create（含产线/工作中心）并弹成功 toast', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(DevicesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
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
      purchaseCurrencyCode?: string
      warrantyExpiresOn?: string
      supplierPartnerCode?: string
      parentDeviceId?: string
      components?: Array<{ componentCode?: string; componentName?: string; quantity?: number }>
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
    expect(body.purchaseCurrencyCode).toBe('CNY')
    expect(body.warrantyExpiresOn).toBe('2027-01-14')
    expect(body.supplierPartnerCode).toBe('SUP-ACME')
    expect(body.parentDeviceId).toBe('EQ-PARENT')
    expect(body.components).toEqual([
      { componentCode: 'MOTOR', componentName: '伺服电机', quantity: 1, critical: false },
    ])
    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  it('部件数量非法时不提交，并提交所选币种编码', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    const wrapper = mount(DevicesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
    await flushPromises()
    await openAndFillValid(wrapper)
    await wrapper.find('#dev-currency').setValue('USD')
    await wrapper.find('#dev-component-qty-0').setValue('0')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).not.toHaveBeenCalled()

    await wrapper.find('#dev-component-qty-0').setValue('1.5')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    const body = stub.create.mock.calls[0]![0] as {
      purchaseCurrencyCode?: string
      components?: Array<{
        componentCode?: string
        componentName?: string
        quantity?: number
        critical?: boolean
      }>
    }
    expect(body.purchaseCurrencyCode).toBe('USD')
    expect(body.components).toEqual([
      { componentCode: 'MOTOR', componentName: '伺服电机', quantity: 1.5, critical: false },
    ])
    expect(stub.toastError).not.toHaveBeenCalled()
  })

  // 工位只能从所选产线下选；换了产线，原先选的工位、工作中心不再属于它，要清掉让用户重选，
  // 不能留着一个界面上看不见、提交时却带上去的旧值。
  // 就地新增工位时，产线由表单带给新增弹窗（弹窗据此把产线只读带出，见 facilities.test），
  // 建出来的工位才一定挂在设备已选的产线下。
  it('工位的新增弹窗拿到的是表单当前选的产线', async () => {
    const wrapper = mount(DevicesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
    await flushPromises()
    await openAndFillValid(wrapper)
    const stationContext = () =>
      JSON.parse(wrapper.get('#dev-station').attributes('data-create-context')!)
    expect(stationContext()).toEqual({ lineCode: 'LINE-A' })

    await wrapper.find('#dev-line').setValue('LINE-B')
    await flushPromises()
    expect(stationContext()).toEqual({ lineCode: 'LINE-B' })
  })

  // 就地新增车间、产线、工作中心时，表单已选的上级交给新增弹窗带出为只读归属；键名对不上时
  // 弹窗会让用户重新自选上级，建出来的项可能挂到别的工厂 / 产线下，又被自动选进设备表单。
  it('车间、产线、工作中心的新增弹窗拿到的是表单当前选的上级', async () => {
    const wrapper = mount(DevicesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
    await flushPromises()
    await openAndFillValid(wrapper)
    const createContext = (id: string) =>
      JSON.parse(wrapper.get(id).attributes('data-create-context')!)
    expect(createContext('#dev-workshop')).toEqual({ siteCode: 'PLANT-A' })
    expect(createContext('#dev-line')).toEqual({ siteCode: 'PLANT-A', workshopCode: 'WS-A' })
    expect(createContext('#dev-wc')).toEqual({ siteCode: 'PLANT-A', lineCode: 'LINE-A' })
  })

  it('换产线后工位、工作中心清空并按新产线收窄，不带旧工位提交', async () => {
    stub.create.mockClear()
    const wrapper = mount(DevicesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
    await flushPromises()
    await openAndFillValid(wrapper)
    expect(JSON.parse(wrapper.get('#dev-station').attributes('data-parent')!)).toEqual({
      lineCode: 'LINE-A',
    })

    await wrapper.find('#dev-line').setValue('LINE-B')
    await flushPromises()

    expect((wrapper.find('#dev-station').element as HTMLInputElement).value).toBe('')
    expect((wrapper.find('#dev-wc').element as HTMLInputElement).value).toBe('')
    expect((wrapper.find('#dev-workshop').element as HTMLInputElement).value).toBe('WS-A')
    expect(JSON.parse(wrapper.get('#dev-station').attributes('data-parent')!)).toEqual({
      lineCode: 'LINE-B',
    })
    await wrapper.find('form').trigger('submit')
    await flushPromises()
    expect(stub.create).not.toHaveBeenCalled()
  })

  // 设备选择器就地新增（directory-creators/equipment.ts）靠 created 拿到新建设备的编码去自动选中。
  it('弹窗新建成功：发出 created，带服务端回传的编码，并关闭', async () => {
    stub.create.mockClear()
    const wrapper = mount(DeviceFormDialog, {
      props: { open: true },
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
    await flushPromises()
    await fillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    expect(wrapper.emitted('created')).toEqual([[{ code: 'EQ-NEW', name: 'KR-210' }]])
    expect(wrapper.emitted('update:open')).toEqual([[false]])
  })

  it('提交失败：弹错误 toast（人话）且不重置表单', async () => {
    stub.create.mockClear()
    stub.toastSuccess.mockClear()
    stub.toastError.mockClear()
    stub.create.mockRejectedValueOnce(new Error('downstream-invalid-response'))
    const wrapper = mount(DevicesPage, {
      global: { stubs: { ...layoutStub, ...dialogStubs, ...pickerStubs, ...selectStubs } },
    })
    await flushPromises()
    await openAndFillValid(wrapper)

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.create).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalledWith(
      '保存设备失败：服务暂时不可用，操作结果可能尚未确认；请刷新列表核实后再重试。',
    )
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 表单未被重置（仍可重试）：型号保留。
    expect((wrapper.find('#dev-model').element as HTMLInputElement).value).toBe('KR-210')
  })
})
