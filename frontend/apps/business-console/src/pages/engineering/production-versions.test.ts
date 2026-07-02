import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref, shallowRef } from 'vue'

import ProductionVersionsPage from './production-versions.vue'

const stub = vi.hoisted(() => ({
  createProductionVersion: vi.fn().mockResolvedValue({ data: { productionVersionId: 'pv-new' } }),
  updateProductionVersion: vi.fn().mockResolvedValue({ data: { productionVersionId: 'pv-1' } }),
  archiveProductionVersion: vi.fn().mockResolvedValue(undefined),
  resolve: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const pvRow = {
  productionVersionId: 'pv-1',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  skuCode: 'SKU-1',
  mbomVersionId: 'MBOM-1',
  routingVersionId: 'RT-1',
  validFrom: '2026-01-01',
  validTo: '2026-12-31',
  lotSizeMin: 10,
  lotSizeMax: 500,
  priority: 5,
  isDefault: true,
  status: 'active',
}

const filters = reactive({ organizationId: 'org-001', environmentId: 'env-dev', skuCode: undefined as string | undefined, status: undefined as string | undefined, skip: 0, take: 10 })
const resolved = shallowRef<Record<string, unknown> | undefined>(undefined)
const resolvedOnce = ref(false)

vi.mock('@/composables/useProductEngineering', () => ({
  useEngineeringProductionVersions: () => ({
    archiveProductionVersion: stub.archiveProductionVersion,
    archivePending: shallowRef(false),
    archiveError: shallowRef(undefined),
    createProductionVersion: stub.createProductionVersion,
    createPending: shallowRef(false),
    createError: shallowRef(undefined),
    filters,
    productionVersions: computed(() => [pvRow]),
    productionVersionsError: shallowRef(undefined),
    productionVersionsPending: shallowRef(false),
    productionVersionsTotal: computed(() => 1),
    refresh: vi.fn(),
    updateProductionVersion: stub.updateProductionVersion,
    updatePending: shallowRef(false),
    updateError: shallowRef(undefined),
  }),
  usePublishedMboms: () => ({
    filters: reactive({}),
    mboms: computed(() => [{ bomCode: 'MBOM-1', revision: 'A', skuCode: 'SKU-1', status: 'Published' }]),
    mbomsError: shallowRef(undefined),
    mbomsPending: shallowRef(false),
    refreshMboms: vi.fn(),
  }),
  usePublishedRoutings: () => ({
    filters: reactive({}),
    routings: computed(() => [{ routingCode: 'RT-1', revision: 'A', skuCode: 'SKU-1', status: 'Published' }]),
    routingsError: shallowRef(undefined),
    routingsPending: shallowRef(false),
    refreshRoutings: vi.fn(),
  }),
  useProductionVersionResolve: () => ({
    resolve: stub.resolve,
    clear: vi.fn(() => { resolvedOnce.value = false; resolved.value = undefined }),
    resolved,
    resolvePending: shallowRef(false),
    resolvedOnce,
  }),
}))

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessSkus: () => ({
    skus: computed(() => [{ code: 'SKU-1', displayName: '智能网关主机' }]),
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
// AlertDialogPro 全家为真 .vue 包装（__name 即 Pro 名），故 global.stubs 键用 Pro 名；
// content 用轻量 stub 避免真实 Teleport 在 jsdom 卸载抛错。
const alertDialogStubs = {
  AlertDialogPro: { template: '<div><slot /></div>' },
  AlertDialogProContent: { template: '<div data-testid="confirm"><slot /></div>' },
  AlertDialogProHeader: { template: '<div><slot /></div>' },
  AlertDialogProFooter: { template: '<div><slot /></div>' },
  AlertDialogProTitle: { template: '<h2><slot /></h2>' },
  AlertDialogProDescription: { template: '<p><slot /></p>' },
  AlertDialogProCancel: { template: '<button type="button"><slot /></button>' },
  AlertDialogProAction: { emits: ['click'], template: '<button type="button" data-testid="confirm-archive" @click="$emit(\'click\', $event)"><slot /></button>' },
}

const allStubs = { ...layoutStub, ...dialogStubs, ...datePickerStub, ...formSelectStubs, ...alertDialogStubs }

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

async function openCreateAndFill(wrapper: ReturnType<typeof mount>) {
  await wrapper.findAll('button').find((b) => b.text().includes('新建生产版本'))!.trigger('click')
  await flushPromises()
  const selects = wrapper.findAll('select')
  // 顺序：物料、MBOM、工艺路线、状态筛选、resolve 物料……取表单内前三个。
  await selects[0]!.setValue('SKU-1')
  await selects[1]!.setValue('MBOM-1')
  await selects[2]!.setValue('RT-1')
  // 生效起：表单内第一个 date input。
  const dates = wrapper.findAll('input[type="date"]')
  await dates[0]!.setValue('2026-03-01')
  await flushPromises()
}

beforeEach(() => {
  stub.createProductionVersion.mockClear()
  stub.updateProductionVersion.mockClear()
  stub.archiveProductionVersion.mockClear()
  stub.resolve.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.skuCode = undefined
  filters.status = undefined
  resolved.value = undefined
  resolvedOnce.value = false
})

describe('engineering production-versions page', () => {
  it('渲染标题、版本行（物料显名 + 绑定 + 有效期 + 批量 + 默认 + 状态）', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('生产版本')
    // 物料显名（复用基础数据 SKU 列表）
    expect(wrapper.text()).toContain('智能网关主机')
    // 绑定的 MBOM / 路线
    expect(wrapper.text()).toContain('MBOM-1')
    expect(wrapper.text()).toContain('RT-1')
    // 有效期格式化
    expect(wrapper.text()).toContain('2026-01-01 至 2026-12-31')
    // 批量区间
    expect(wrapper.text()).toContain('10 - 500')
    // 默认徽标 + 有效状态
    expect(wrapper.text()).toContain('默认')
    expect(wrapper.text()).toContain('有效')
  })

  it('新建：填全必填后提交，create 收到 SKU + MBOM + 路线 + 有效期 + 批量 + 默认', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await openCreateAndFill(wrapper)
    // 批量下/上限
    const numberInputs = wrapper.findAll('input[type="number"]')
    await numberInputs[0]!.setValue('20')
    await numberInputs[1]!.setValue('300')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createProductionVersion).toHaveBeenCalledTimes(1)
    const body = stub.createProductionVersion.mock.calls[0]![0] as Record<string, unknown>
    expect(body.skuCode).toBe('SKU-1')
    expect(body.mbomVersionId).toBe('MBOM-1')
    expect(body.routingVersionId).toBe('RT-1')
    expect(body.validFrom).toBe('2026-03-01')
    expect(body.lotSizeMin).toBe(20)
    expect(body.lotSizeMax).toBe(300)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('新建：有效期起 validFrom 默认今天', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().includes('新建生产版本'))!.trigger('click')
    await flushPromises()

    const d = new Date()
    const ymd = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
    // 表单内第一个 date input 即生效起。
    expect((wrapper.findAll('input[type="date"]')[0]!.element as HTMLInputElement).value).toBe(ymd)
  })

  it('校验拦截：必填未填点保存出现汇总提示且不发创建请求', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().includes('新建生产版本'))!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createProductionVersion).not.toHaveBeenCalled()
  })

  it('校验拦截：批量下限大于上限不通过（min ≤ max）', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await openCreateAndFill(wrapper)
    const numberInputs = wrapper.findAll('input[type="number"]')
    await numberInputs[0]!.setValue('500')
    await numberInputs[1]!.setValue('100')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createProductionVersion).not.toHaveBeenCalled()
  })

  it('编辑：行「编辑」回填后提交调用 update（物料不可改）', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '编辑')!.trigger('click')
    await flushPromises()
    expect(wrapper.text()).toContain('编辑生产版本')

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.updateProductionVersion).toHaveBeenCalledTimes(1)
    const [id, body] = stub.updateProductionVersion.mock.calls[0]! as [string, Record<string, unknown>]
    expect(id).toBe('pv-1')
    expect(body.mbomVersionId).toBe('MBOM-1')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('归档：点「归档」出现二次确认，确认后带原因调用 archive', async () => {
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '归档')!.trigger('click')
    await flushPromises()

    // 填原因后确认
    await wrapper.find('#archive-reason').setValue('工艺变更')
    await wrapper.find('[data-testid="confirm-archive"]').trigger('click')
    await flushPromises()

    expect(stub.archiveProductionVersion).toHaveBeenCalledTimes(1)
    const [id, reason] = stub.archiveProductionVersion.mock.calls[0]! as [string, string]
    expect(id).toBe('pv-1')
    expect(reason).toBe('工艺变更')
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('resolve：填条件点解析调用 resolve；命中后展示绑定', async () => {
    stub.resolve.mockImplementation(async () => {
      resolved.value = { skuCode: 'SKU-1', mbomVersionId: 'MBOM-1', routingVersionId: 'RT-1', status: 'active' }
      resolvedOnce.value = true
    })
    const wrapper = mount(ProductionVersionsPage, { global: { stubs: allStubs } })
    await flushPromises()

    // <select> 顺序：表单内 物料/MBOM/路线（3）、resolve 卡物料（第 4）、状态筛选（第 5）。
    const selects = wrapper.findAll('select')
    await selects[3]!.setValue('SKU-1')
    await flushPromises()

    await wrapper.findAll('button').find((b) => b.text().trim() === '解析')!.trigger('click')
    await flushPromises()

    expect(stub.resolve).toHaveBeenCalledTimes(1)
    const arg = stub.resolve.mock.calls[0]![0] as Record<string, unknown>
    expect(arg.skuCode).toBe('SKU-1')
    expect(wrapper.text()).toContain('命中物料')
  })
})
