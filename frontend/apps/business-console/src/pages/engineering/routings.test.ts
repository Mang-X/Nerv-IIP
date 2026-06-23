import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref, shallowRef } from 'vue'

import RoutingsPage from './routings.vue'

const stub = vi.hoisted(() => ({
  releaseRouting: vi.fn().mockResolvedValue({ data: {} }),
  fetchRoutingDetail: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const routingRow = {
  routingCode: 'RT-1',
  revision: 'A',
  skuCode: 'SKU-1',
  status: 'Published',
  effectiveDate: '2026-01-01',
}

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skuCode: undefined as string | undefined, status: undefined as string | undefined, skip: 0, take: 10 })
const workCenters = ref([
  { code: 'WC-A', displayName: '焊接中心' },
  { code: 'WC-B', displayName: '装配中心' },
])
// 标准工序主数据（#397）：每条带默认工作中心 + 标准工时，选中后自动带出。
const standardOperations = ref([
  { operationCode: 'OP-WELD', operationName: '焊接', defaultWorkCenterCode: 'WC-A', standardMinutes: 6, enabled: true },
  { operationCode: 'OP-ASSY', operationName: '装配', defaultWorkCenterCode: 'WC-B', standardMinutes: 9, enabled: true },
])

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringRoutings: () => ({
    routings: computed(() => [routingRow]),
    routingsError: shallowRef(undefined),
    routingsPending: shallowRef(false),
    routingsTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    releaseRouting: stub.releaseRouting,
    releasePending: shallowRef(false),
    releaseError: shallowRef(undefined),
    fetchRoutingDetail: stub.fetchRoutingDetail,
  }),
  useStandardOperations: () => ({
    standardOperations: computed(() => standardOperations.value),
    standardOperationsPending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    skus: computed(() => [{ code: 'SKU-1', displayName: '智能网关主机' }]),
  }),
  useBusinessMasterDataResources: () => ({
    resources: computed(() => workCenters.value),
    resourcesPending: shallowRef(false),
    resourcesError: shallowRef(undefined),
    refreshResources: vi.fn(),
    filters: reactive({}),
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
const routerLinkStub = { RouterLink: { props: ['to'], template: '<a><slot /></a>' } }

const allStubs = { ...layoutStub, ...dialogStubs, ...sheetStubs, ...datePickerStub, ...formSelectStubs, ...routerLinkStub }

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

async function openAndFillHeader(wrapper: ReturnType<typeof mount>) {
  await findButton(wrapper, '发布新版本')!.trigger('click')
  await flushPromises()
  const selects = wrapper.findAll('select')
  await selects[0]!.setValue('SKU-1') // 产出物料
  await wrapper.find('#rt-rev').setValue('B')
  await wrapper.findAll('input[type="date"]')[0]!.setValue('2026-03-01')
  await flushPromises()
}

beforeEach(() => {
  stub.releaseRouting.mockClear()
  stub.fetchRoutingDetail.mockReset()
  stub.fetchRoutingDetail.mockResolvedValue(undefined)
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.skuCode = undefined
  filters.status = undefined
  workCenters.value = [
    { code: 'WC-A', displayName: '焊接中心' },
    { code: 'WC-B', displayName: '装配中心' },
  ]
  standardOperations.value = [
    { operationCode: 'OP-WELD', operationName: '焊接', defaultWorkCenterCode: 'WC-A', standardMinutes: 6, enabled: true },
    { operationCode: 'OP-ASSY', operationName: '装配', defaultWorkCenterCode: 'WC-B', standardMinutes: 9, enabled: true },
  ]
})

describe('engineering routings page', () => {
  it('渲染标题、路线行（产出物料显名 + 状态用 Published）', async () => {
    const wrapper = mount(RoutingsPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('工艺路线')
    expect(wrapper.text()).toContain('智能网关主机')
    expect(wrapper.text()).toContain('RT-1')
    expect(wrapper.text()).toContain('已发布')
  })

  it('发布向导：填头 + 一道工序提交，release 收到有序 operations', async () => {
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await openAndFillHeader(wrapper)

    const selects = wrapper.findAll('select')
    // 顺序：产出物料(0)、工序行工作中心(1)、工序行标准工序(2)…… 状态筛选最后。
    await selects[1]!.setValue('WC-A')
    await selects[2]!.setValue('OP-WELD')
    await wrapper.find('#rt-min-0').setValue('12')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseRouting).toHaveBeenCalledTimes(1)
    const body = stub.releaseRouting.mock.calls[0]![0] as Record<string, unknown>
    expect(body.skuCode).toBe('SKU-1')
    const ops = body.operations as Array<Record<string, unknown>>
    expect(ops).toHaveLength(1)
    expect(ops[0]!.sequence).toBe(10)
    expect(ops[0]!.workCenterCode).toBe('WC-A')
    // 工序从标准工序选：提交带 operationCode + 工序名。
    expect(ops[0]!.operationCode).toBe('OP-WELD')
    expect(ops[0]!.operationName).toBe('焊接')
    expect(ops[0]!.standardMinutes).toBe(12)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('工序可增删：增加工序后两道，序号自动 10/20', async () => {
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    await findButton(wrapper, '增加工序')!.trigger('click')
    await flushPromises()

    const seqInputs = wrapper.findAll('#rt-seq-0, #rt-seq-1')
    expect((seqInputs[0]!.element as HTMLInputElement).value).toBe('10')
    expect((seqInputs[1]!.element as HTMLInputElement).value).toBe('20')
  })

  it('工序可排序：下移把第一道与第二道序号互换', async () => {
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await openAndFillHeader(wrapper)
    await findButton(wrapper, '增加工序')!.trigger('click')
    await flushPromises()

    // 每行两个 Select：工作中心 + 标准工序。两行后顺序为 sku(0)、行0 WC(1)、行0 op(2)、行1 WC(3)、行1 op(4)。
    const selects = wrapper.findAll('select')
    await selects[1]!.setValue('WC-A')
    await selects[2]!.setValue('OP-WELD')
    await wrapper.find('#rt-min-0').setValue('5')
    await selects[3]!.setValue('WC-B')
    await selects[4]!.setValue('OP-ASSY')
    await wrapper.find('#rt-min-1').setValue('8')
    await flushPromises()

    await wrapper.find('button[aria-label="下移工序"]').trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.releaseRouting).toHaveBeenCalledTimes(1)
    const body = stub.releaseRouting.mock.calls[0]![0] as Record<string, unknown>
    const ops = body.operations as Array<Record<string, unknown>>
    // 下移后第一道变成原第二道（装配），序号随显示顺序为 10。
    expect(ops[0]!.operationCode).toBe('OP-ASSY')
    expect(ops[0]!.operationName).toBe('装配')
    expect(ops[0]!.sequence).toBe(10)
    expect(ops[1]!.operationCode).toBe('OP-WELD')
    expect(ops[1]!.operationName).toBe('焊接')
    expect(ops[1]!.sequence).toBe(20)
  })

  it('无工作中心时给出「去基础数据维护」出路且禁用增加工序', async () => {
    workCenters.value = []
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('去基础数据维护工作中心')
    expect(findButton(wrapper, '增加工序')!.attributes('disabled')).toBeDefined()
  })

  it('校验拦截：必填未填点发布出现汇总提示且不发请求', async () => {
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.releaseRouting).not.toHaveBeenCalled()
  })

  it('打开向导：生效日默认今天', async () => {
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    const d = new Date()
    const ymd = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    expect((wrapper.findAll('input[type="date"]')[0]!.element as HTMLInputElement).value).toBe(ymd)
  })

  it('查看：行「查看」拉 get-by-id 渲染真实工序行（序号/工作中心/工序名/工时）', async () => {
    stub.fetchRoutingDetail.mockResolvedValue({
      routingCode: 'RT-1',
      revision: 'A',
      skuCode: 'SKU-1',
      status: 'Published',
      operations: [
        { sequence: 20, workCenterCode: 'WC-B', operationCode: 'OP-ASSY', operationName: '装配', standardMinutes: 8 },
        { sequence: 10, workCenterCode: 'WC-A', operationCode: 'OP-WELD', operationName: '焊接', standardMinutes: 12 },
      ],
    })
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    expect(stub.fetchRoutingDetail).toHaveBeenCalledWith('RT-1', 'A')
    const sheet = wrapper.find('[data-testid="sheet"]')
    expect(sheet.text()).toContain('焊接')
    expect(sheet.text()).toContain('装配')
    expect(sheet.text()).toContain('焊接中心')
    // 不再标注「待后端」。
    expect(sheet.text()).not.toContain('待后端')
    // 按序号排序：第一道为序号 10（焊接）。
    const firstRow = sheet.findAll('tbody tr')[0]!
    expect(firstRow.text()).toContain('焊接')
  })

  it('无标准工序时给出「去标准工序维护」出路且禁用增加工序', async () => {
    standardOperations.value = []
    const wrapper = mount(RoutingsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '发布新版本')!.trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('去标准工序维护')
    expect(findButton(wrapper, '增加工序')!.attributes('disabled')).toBeDefined()
  })
})
