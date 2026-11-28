import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import EcoPage from './eco.vue'

const stub = vi.hoisted(() => ({
  impacts: vi.fn(),
  reschedule: vi.fn(),
  cancelSchedule: vi.fn(),
  permissionCodes: [] as string[],
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

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  status: undefined as string | undefined,
  skip: 0,
  take: 10,
})
const impactPreview = shallowRef()

const routeState = reactive({ query: {} as Record<string, string> })
vi.mock('vue-router', async (original) => ({
  ...(await original<typeof import('vue-router')>()),
  useRoute: () => routeState,
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: stub.permissionCodes } }),
}))

vi.mock('@nerv-iip/api-client', async (original) => {
  const eboms = [
    { bomCode: 'EBOM-FG', revision: 'A', parentItemCode: 'SKU-FG' },
    ...Array.from({ length: 100 }, (_, index) => ({
      bomCode: `EBOM-${index + 2}`,
      revision: 'A',
      parentItemCode: 'SKU-FG',
    })),
  ]
  const list =
    <T>(items: T[]) =>
    ({ query }: { query: { skip: number; take: number } }) => ({
      query: async () => ({
        success: true,
        data: { items: items.slice(query.skip, query.skip + query.take), total: items.length },
      }),
    })
  return {
    ...(await original<typeof import('@nerv-iip/api-client')>()),
    listBusinessConsoleMesEngineeringChangeWorkOrderImpacts: stub.impacts,
    rescheduleBusinessConsoleEngineeringChange: stub.reschedule,
    cancelScheduledBusinessConsoleEngineeringChange: stub.cancelSchedule,
    listBusinessConsoleEngineeringBomsQueryOptions: list(eboms),
    listBusinessConsoleEngineeringManufacturingBomsQueryOptions: list([]),
    listBusinessConsoleEngineeringRoutingsQueryOptions: list([
      { routingCode: 'RT-1', revision: 'B', skuCode: 'SKU-FG' },
    ]),
    listBusinessConsoleEngineeringDocumentsQueryOptions: list([]),
    listBusinessConsoleEngineeringProductionVersionsQueryOptions: list([
      {
        productionVersionId: 'PV-1',
        skuCode: 'SKU-1',
        validFrom: '2026-01-01T00:00:00Z',
        isDefault: true,
      },
    ]),
  }
})

vi.mock('@/composables/engineering', () => ({
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
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div data-testid="eco-dialog-content"><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div data-testid="eco-dialog-footer"><slot /></div>' },
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
const routerLinkStubs = {
  RouterLink: { props: ['to'], template: '<a data-router-link :data-to="to"><slot /></a>' },
}

const approvalPanelStub = {
  BusinessDocumentApprovalPanel: {
    props: [
      'modelValue',
      'allowStart',
      'documentId',
      'sourceService',
      'documentType',
      'preferredTemplateCode',
    ],
    emits: ['update:modelValue'],
    template:
      '<section data-testid="approval-panel" :data-allow-start="String(allowStart)" :data-document-id="documentId" :data-source-service="sourceService" :data-document-type="documentType" :data-preferred-template-code="preferredTemplateCode"><button type="button" @click="$emit(\'update:modelValue\', \'APR-9\')">关联审批链</button><button v-if="allowStart && documentId" type="button" @click="$emit(\'update:modelValue\', \'CHAIN-NEW\')">发起审批</button><span>{{ modelValue }}</span></section>',
  },
}

const allStubs = {
  NvEntityPicker: {
    props: ['modelValue', 'options', 'disabled'],
    emits: ['update:modelValue'],
    template: `<select :value="modelValue" :disabled="disabled" @change="$emit('update:modelValue', $event.target.value)">
      <option value=""></option>
      <option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option>
    </select>`,
  },

  ...layoutStub,
  ...dialogStubs,
  ...sheetStubs,
  ...datePickerStub,
  ...formSelectStubs,
  ...routerLinkStubs,
  ...approvalPanelStub,
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  routeState.query = {}
  stub.permissionCodes = []
  stub.impacts.mockReset()
  stub.reschedule.mockReset().mockResolvedValue({ data: { success: true } })
  stub.cancelSchedule.mockReset().mockResolvedValue({ data: { success: true } })
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
        {
          code: 'production-version-not-found',
          severity: 'warning',
          message: "Production version 'PV-404' was not found.",
          relatedVersionId: 'PV-404',
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

  it('状态筛选包含计划生效与已发布', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('计划生效')
    const statusSelect = wrapper
      .findAll('select')
      .find((select) => select.find('option[value="Scheduled"]').exists())!
    await statusSelect.setValue('Scheduled')
    expect(filters.status).toBe('Scheduled')
    await statusSelect.setValue('Published')
    expect(filters.status).toBe('Published')
    expect(text).not.toContain('草稿')
    expect(text).not.toContain('待审')
  })

  it('发布前说明未来生效日会进入计划生效状态', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '发布变更')!.trigger('click')

    const datePicker = wrapper.get('form input[type="date"]')
    const helpId = datePicker.attributes('aria-describedby')
    expect(helpId).toBeTruthy()
    expect(wrapper.get(`[id="${helpId}"]`).text()).toContain(
      '选择未来日期，提交后进入「计划生效」。此次变更在生效前不会更新下游版本。',
    )
  })

  it('发布向导：填变更信息 + 一条受影响版本提交，release 收到正确 body', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    await wrapper.find('#eco-change-number').setValue('ECO-20260801-000001')
    await wrapper.find('#eco-reason').setValue('工艺优化')
    await findButton(wrapper, '关联审批链')!.trigger('click')
    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    // 受影响版本：第一个 select 是对象种类
    await wrapper.findAll('select')[0]!.setValue('Routing')
    await wrapper.find('#eco-vid-0').setValue('RT-1:B')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseChange).toHaveBeenCalledTimes(1)
    const body = stub.releaseChange.mock.calls[0]![0] as Record<string, unknown>
    expect(body.changeNumber).toBe('ECO-20260801-000001')
    expect(body.reason).toBe('工艺优化')
    expect(body.approvalReferenceId).toBe('APR-9')
    expect(body.effectiveDate).toBe('2026-03-01')
    const affected = body.affectedVersions as Array<Record<string, unknown>>
    expect(affected).toHaveLength(1)
    expect(affected[0]!.versionKind).toBe('Routing')
    expect(affected[0]!.versionId).toBe('RT-1:B')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('工程变更可用同一 ECO 号发起审批，审批链入口不是静默禁用', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()
    await wrapper.find('#eco-change-number').setValue('ECO-20260801-000001')
    await flushPromises()

    const panel = wrapper.find('[data-testid="approval-panel"]')
    expect(panel.attributes('data-allow-start')).toBe('true')
    expect(panel.attributes('data-document-id')).toBe('ECO-20260801-000001')
    expect(panel.attributes('data-source-service')).toBe('product-engineering')
    expect(panel.attributes('data-document-type')).toBe('engineering-change-order')
    expect(panel.attributes('data-preferred-template-code')).toBe('engineering-change-order')
    expect(findButton(wrapper, '发起审批')).toBeDefined()

    await findButton(wrapper, '发起审批')!.trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="approval-panel"]').text()).toContain('CHAIN-NEW')
  })

  it('发布失败时保留弹框并给出明确提示', async () => {
    stub.releaseChange.mockRejectedValueOnce(new Error('审批链未通过'))
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()
    await wrapper.find('#eco-change-number').setValue('ECO-20260801-000001')
    await wrapper.find('#eco-reason').setValue('工艺优化')
    await findButton(wrapper, '关联审批链')!.trigger('click')
    await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
    await wrapper.findAll('select')[0]!.setValue('Routing')
    await wrapper.find('#eco-vid-0').setValue('RT-1:B')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseChange).toHaveBeenCalledTimes(1)
    expect(stub.toastError).toHaveBeenCalled()
    expect(wrapper.find('[data-testid="eco-dialog-content"]').exists()).toBe(true)
  })

  it('使用 NvDialogContent 与 NvDialogFooter，底部操作仍在弹框结构内', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="eco-dialog-content"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="eco-dialog-footer"]').text()).toContain('发布变更')
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

    expect(stub.previewImpact).toHaveBeenCalledWith(
      expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        effectiveDate: '2026-03-01',
        affectedVersions: [{ versionKind: 'EngineeringBom', versionId: 'EBOM-FG:A' }],
      }),
    )
    expect(stub.releaseChange).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('MBOM MBOM-1 / B')
    expect(wrapper.text()).toContain('MRP 候选')
    expect(wrapper.text()).toContain('APS 排程')
    expect(wrapper.text()).toContain('暂无入口')
    expect(wrapper.find('[data-router-link]').exists()).toBe(true)
    expect(wrapper.text()).toContain('变更会影响 MRP、MES、APS 和在制执行候选')
    // 风险级别与服务端英文原文都换成中文，版本主键不上屏。
    expect(wrapper.text()).toContain('警告')
    expect(wrapper.text()).toContain('找不到该生产版本，请确认版本是否存在。')
    expect(wrapper.text()).not.toContain('warning')
    expect(wrapper.text()).not.toContain('was not found')
    expect(wrapper.text()).not.toContain('PV-404')
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

  it('受影响版本可增删：增加一行后多一组类型和版本选择器', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布变更')!.trigger('click')
    await flushPromises()

    const before = wrapper.findAll('select').length
    await findButton(wrapper, '增加一条')!.trigger('click')
    await flushPromises()
    expect(wrapper.findAll('select').length).toBe(before + 2)
  })

  it.each(['改期', '取消调度'])('计划生效可二次确认%s，期间不读取实际影响', async (action) => {
    stub.permissionCodes = [
      'business.engineering.changes.read',
      'business.engineering.changes.manage',
      'business.mes.work-orders.read',
    ]
    stub.fetchChangeDetail.mockResolvedValue({
      ...changeRow,
      status: 'Scheduled',
      effectiveDate: '2099-02-01',
    })
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="sheet"]').text()).toContain('计划生效')
    expect(stub.impacts).not.toHaveBeenCalled()

    await findButton(wrapper, action)!.trigger('click')
    const confirmation = wrapper.findAll('[data-testid="eco-dialog-content"]').at(-1)!
    expect(confirmation.text()).toContain(
      action === '改期' ? '确认工程变更改期' : '确认取消工程变更调度',
    )
    expect(stub.reschedule).not.toHaveBeenCalled()
    expect(stub.cancelSchedule).not.toHaveBeenCalled()
    await confirmation.get('#eco-schedule-reason').setValue('等新工装验收')
    if (action === '改期') await confirmation.get('#eco-schedule-date').setValue('2099-02-10')
    await confirmation.get('form').trigger('submit')
    await flushPromises()

    const body = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      changeNumber: 'ECO-1',
      reason: '等新工装验收',
    }
    if (action === '改期') {
      expect(stub.reschedule).toHaveBeenCalledExactlyOnceWith({
        body: { ...body, effectiveDate: '2099-02-10' },
        throwOnError: true,
      })
      expect(stub.cancelSchedule).not.toHaveBeenCalled()
    } else {
      expect(stub.cancelSchedule).toHaveBeenCalledExactlyOnceWith({ body, throwOnError: true })
      expect(stub.reschedule).not.toHaveBeenCalled()
    }
    expect(stub.impacts).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('只有工程变更读权限时仍可看变更详情，隐藏工单影响且不发请求', async () => {
    stub.permissionCodes = ['business.engineering.changes.read']
    stub.fetchChangeDetail.mockResolvedValue({ ...changeRow, status: 'Published' })
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()
    const sheet = wrapper.get('[data-testid="sheet"]')
    expect(sheet.text()).toContain('受影响版本')
    expect(sheet.text()).not.toContain('在制影响')
    expect(stub.impacts).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('已生效详情按服务端总数翻页，显示实际工单及处置状态', async () => {
    stub.permissionCodes = ['business.engineering.changes.read', 'business.mes.work-orders.read']
    stub.fetchChangeDetail.mockResolvedValue({ ...changeRow, status: 'Published' })
    stub.impacts
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            total: 11,
            items: [
              { changeNumber: 'ECO-1', workOrderId: 'WO-202602-01', disposition: 'auto-rebound' },
            ],
          },
        },
      })
      .mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            total: 11,
            items: [
              {
                changeNumber: 'ECO-1',
                workOrderId: 'WO-202602-11',
                disposition: 'abort-work-order',
              },
            ],
          },
        },
      })
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    const sheet = wrapper.get('[data-testid="sheet"]')
    expect(sheet.get('[data-to="/mes/work-orders/WO-202602-01"]').text()).toBe('WO-202602-01')
    expect(sheet.text()).toContain('已重绑')
    await sheet.get('button[aria-label="下一页"]').trigger('click')
    await flushPromises()
    expect(stub.impacts.mock.calls.map(([options]) => options.query)).toEqual([
      {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        changeNumber: 'ECO-1',
        skip: 0,
        take: 10,
      },
      {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        changeNumber: 'ECO-1',
        skip: 10,
        take: 10,
      },
    ])
    expect(sheet.text()).toContain('已中止')
    expect(sheet.text()).not.toContain('WO-202602-01')
    wrapper.unmount()
  })

  it('从工单链接打开指定 ECO 详情，无需该变更位于当前列表页', async () => {
    routeState.query = { changeNumber: 'ECO/2602-01' }
    stub.permissionCodes = ['business.engineering.changes.read']
    stub.fetchChangeDetail.mockResolvedValue({
      ...changeRow,
      changeNumber: 'ECO/2602-01',
      reason: '更新精加工夹具',
    })
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()

    expect(stub.fetchChangeDetail).toHaveBeenCalledExactlyOnceWith('ECO/2602-01')
    expect(wrapper.get('[data-testid="sheet"]').text()).toContain('ECO/2602-01 · 更新精加工夹具')
    wrapper.unmount()
  })

  it('查看：行「查看」拉 get-by-id 渲染受影响版本', async () => {
    stub.fetchChangeDetail.mockResolvedValue({
      changeNumber: 'ECO-1',
      reason: '更换供应商物料',
      status: 'Released',
      affectedVersions: [
        { versionKind: 'manufacturing-bom', versionId: 'MBOM-VER-9' },
        { versionKind: 'production-version', versionId: 'PV-1' },
        { versionKind: 'ProductionVersion', versionId: 'PV-GONE' },
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
    // 生产版本存的是版本主键，上屏的是「物料 · 生效日」。
    expect(sheet.text()).toContain('SKU-1 · 生效 2026-01-01')
    expect(sheet.text()).not.toContain('PV-1')
    // 读不到名称（无权限、清单截断或仍在加载）时说中性话，不回吐主键。
    expect(sheet.text()).toContain('暂无法显示版本名称')
    expect(sheet.text()).not.toContain('PV-GONE')
  })
  it('第 101 条 EBOM 可选，目录外编号和修订可直接指定', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布变更')!.trigger('click')
    await wrapper.get('#eco-change-number').setValue('ECO-20260801-000101')
    await wrapper.get('#eco-reason').setValue('修订物料清单')
    await findButton(wrapper, '关联审批链')!.trigger('click')
    await wrapper.findAll('select')[0]!.setValue('EngineeringBom')

    const version = wrapper.get<HTMLSelectElement>('#eco-vid-0')
    expect(version.find('option[value="EBOM-101:A"]').exists()).toBe(true)
    await version.setValue('EBOM-101:A')
    await findButton(wrapper, '预览影响')!.trigger('click')
    expect(stub.previewImpact).toHaveBeenCalledWith(
      expect.objectContaining({
        affectedVersions: [{ versionKind: 'EngineeringBom', versionId: 'EBOM-101:A' }],
      }),
    )

    await wrapper.get('input[aria-label="编号:修订"]').setValue('EBOM-6001:B')
    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(stub.releaseChange).toHaveBeenCalledWith(
      expect.objectContaining({
        affectedVersions: [{ versionKind: 'EngineeringBom', versionId: 'EBOM-6001:B' }],
      }),
    )
  })

  it('对象类型切换时清空旧版本，候选显示编号、修订和父项', async () => {
    const wrapper = mount(EcoPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '发布变更')!.trigger('click')
    const kind = wrapper.findAll('select')[0]!
    await kind.setValue('EngineeringBom')
    await flushPromises()
    const version = wrapper.get('#eco-vid-0')
    expect(version.text()).toContain('EBOM-FG · 修订 A · SKU-FG')
    await version.setValue('EBOM-FG:A')
    await kind.setValue('Routing')
    expect(version.text()).toContain('RT-1 · 修订 B · SKU-FG')
    expect((version.element as HTMLSelectElement).value).toBe('')
    expect(version.text()).not.toContain('EBOM-FG')
  })
})
