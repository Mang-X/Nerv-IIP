import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import ItemsPage from './items.vue'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: ['business.engineering.items.manage'] } }),
}))

const stub = vi.hoisted(() => ({
  createItemRevision: vi.fn().mockResolvedValue({ data: {} }),
  fetchItemDetail: vi.fn(),
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

const itemRow = {
  itemCode: 'ITEM-1',
  revision: 'A',
  name: '主控板',
  status: 'Published',
  createdAtUtc: '2026-01-01T00:00:00Z',
  updatedAtUtc: '2026-01-02T00:00:00Z',
}

const filters = reactive({
  organizationId: 'org-001',
  environmentId: 'env-dev',
  itemCode: undefined as string | undefined,
  status: undefined as string | undefined,
  skip: 0,
  take: 10,
})

vi.mock('@/composables/engineering', () => ({
  useBomItemCatalog: () => ({
    engineeringEntries: shallowRef([{ code: 'ITEM-1', name: '控制主板' }]),
    engineeringRevisions: shallowRef([{ itemCode: 'ITEM-1', revision: 'A' }]),
    engineeringError: shallowRef(undefined),
    skuError: shallowRef(undefined),
    skuEntries: shallowRef([{ code: 'SKU-FG', name: '成品' }]),
    pending: shallowRef(false),
    load: vi.fn(),
  }),
  useEngineeringItems: () => ({
    items: computed(() => [itemRow]),
    itemsError: shallowRef(undefined),
    itemsPending: shallowRef(false),
    itemsTotal: computed(() => 1),
    filters,
    refresh: vi.fn(),
    createItemRevision: stub.createItemRevision,
    createPending: shallowRef(false),
    createError: shallowRef(undefined),
    fetchItemDetail: stub.fetchItemDetail,
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
  NvCheckbox: {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template:
      '<input type="checkbox" :checked="modelValue" @change="$emit(\'update:modelValue\', $event.target.checked)" />',
  },
}

const allStubs = {
  ...layoutStub,
  ...dialogStubs,
  ...sheetStubs,
  ...formSelectStubs,
  NvEntityPicker: {
    props: ['modelValue', 'options'],
    emits: ['update:modelValue'],
    template: `<select :value="modelValue" @change="$emit('update:modelValue', $event.target.value)">
      <option value=""></option>
      <option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option>
    </select>`,
  },
}

function findButton(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('button').find((b) => b.text().trim() === text)
}

beforeEach(() => {
  stub.createItemRevision.mockClear()
  stub.fetchItemDetail.mockReset()
  stub.fetchItemDetail.mockResolvedValue(undefined)
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  filters.itemCode = undefined
  filters.status = undefined
})

describe('engineering items page', () => {
  it('渲染标题与物料修订行（编码/名称/状态用 Published）', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: { ...layoutStub } } })
    await flushPromises()

    expect(wrapper.text()).toContain('工程物料')
    expect(wrapper.text()).toContain('ITEM-1')
    expect(wrapper.text()).toContain('主控板')
    expect(wrapper.text()).toContain('已发布')
  })

  it('用已有 SKU 编码创建工程修订', async () => {
    const wrapper = mount(ItemsPage, {
      global: {
        stubs: {
          ...allStubs,
          NvEntityPicker: {
            props: ['options'],
            emits: ['update:modelValue'],
            template:
              '<button type="button" @click="$emit(\'update:modelValue\', options[0].value)">{{ options[0].label }}</button>',
          },
        },
      },
    })
    await findButton(wrapper, '新建修订')!.trigger('click')
    await wrapper.get('form select').setValue('sku')
    await wrapper.get('#item-sku').trigger('click')
    await wrapper.get('#item-rev').setValue('A')
    await wrapper.get('form').trigger('submit')
    expect(stub.createItemRevision).toHaveBeenCalledWith(
      expect.objectContaining({ itemCode: 'SKU-FG', name: '成品', revision: 'A' }),
    )
  })

  it('新建修订向导：新建物料模式下不传 itemCode，create 收到正确 body', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '新建修订')!.trigger('click')
    await flushPromises()

    await wrapper.find('#item-rev').setValue('B')
    await wrapper.find('#item-name').setValue('新主控板')
    // 勾选立即发布
    await wrapper.find('input[type="checkbox"]').setValue(true)
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(stub.createItemRevision).toHaveBeenCalledTimes(1)
    const body = stub.createItemRevision.mock.calls[0]![0] as Record<string, unknown>
    expect(body.itemCode).toBeUndefined()
    expect(body.revision).toBe('B')
    expect(body.name).toBe('新主控板')
    expect(body.release).toBe(true)
    expect(stub.toastSuccess).toHaveBeenCalled()
  })

  it('校验拦截：未填修订/名称点提交出现汇总提示且不发请求', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await flushPromises()
    await findButton(wrapper, '新建修订')!.trigger('click')
    await flushPromises()

    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('请完整填写带 * 的必填项')
    expect(stub.createItemRevision).not.toHaveBeenCalled()
  })

  it('查看：行「查看」拉 get-by-id 渲染真实修订明细', async () => {
    stub.fetchItemDetail.mockResolvedValue({
      itemCode: 'ITEM-1',
      revision: 'A',
      name: '主控板详情',
      status: 'Published',
    })
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await flushPromises()

    await findButton(wrapper, '查看')!.trigger('click')
    await flushPromises()

    expect(stub.fetchItemDetail).toHaveBeenCalledWith('ITEM-1', 'A')
    const sheet = wrapper.find('[data-testid="sheet"]')
    expect(sheet.text()).toContain('主控板详情')
  })
  it('派生修订时提示已占用的修订号并阻止提交', async () => {
    const wrapper = mount(ItemsPage, { global: { stubs: allStubs } })
    await findButton(wrapper, '新建修订')!.trigger('click')
    await wrapper.get('form select').setValue('existing')
    await wrapper.get('#item-code').setValue('ITEM-1')
    await wrapper.get('#item-name').setValue('控制主板')
    await wrapper.get('#item-rev').setValue('A')
    await wrapper.get('form').trigger('submit')
    expect(wrapper.text()).toContain('该物料已有此修订号')
    expect(stub.createItemRevision).not.toHaveBeenCalled()
    await wrapper.get('#item-rev').setValue('B')
    await wrapper.get('form').trigger('submit')
    expect(stub.createItemRevision).toHaveBeenCalledWith(
      expect.objectContaining({ itemCode: 'ITEM-1', revision: 'B' }),
    )
  })
})
